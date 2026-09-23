using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VSR.Models;

namespace VSR.Services;

public class AuthService
{
    private static readonly string SavedCredentialFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VNPT");
    private static readonly string SavedCredentialFile = Path.Combine(SavedCredentialFolder, "saved_account.json");
    private static readonly string AccountPresetsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Account.json");

    public static string ComputeRealPersonHash(string text)
    {
        unchecked
        {
            int h = 1324;
            foreach (char ch in text)
            {
                h = ((h << 5) + h) + (int)ch;
            }
            return h.ToString();
        }
    }

    public async Task<(AuthSession? Session, string ErrorMessage)> LoginAsync(string username, string password, string apiUrl = "https://baocao.hatinh.gov.vn/ioc/RestService")
    {
        if (string.IsNullOrWhiteSpace(username))
            return (null, "Vui lòng nhập tên tài khoản");
        if (string.IsNullOrWhiteSpace(password))
            return (null, "Vui lòng nhập mật khẩu");

        try
        {
            var uri = new Uri(apiUrl);
            var baseUrl = $"{uri.Scheme}://{uri.Authority}";

            var cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                AllowAutoRedirect = true,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            // 1. Visit login page to initialize cookie session
            var loginPageUrl = $"{baseUrl}/ioc/login/login.jsp";
            try
            {
                await client.GetAsync(loginPageUrl);
            }
            catch (Exception ex)
            {
                return (null, $"Không thể kết nối đến máy chủ IOC ({ex.Message})");
            }

            // 2. Bypass captcha with RealPerson algorithm & validate credentials
            const string captchaText = "ABCDEF";
            var captchaHash = ComputeRealPersonHash(captchaText);

            var postData = new Dictionary<string, string>
            {
                { "txtName", username },
                { "txtPass", password },
                { "defaultReal", captchaText },
                { "defaultRealHash", captchaHash },
                { "csrfPreventionSalt", "e" }
            };

            var loginValidateUrl = $"{baseUrl}/ioc/servlet/login.ValidateUser";
            using var content = new FormUrlEncodedContent(postData);
            var response = await client.PostAsync(loginValidateUrl, content);

            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
            if (finalUrl.Contains("invalid="))
            {
                var match = Regex.Match(finalUrl, @"invalid=(\d+)");
                var code = match.Success ? match.Groups[1].Value : "0";
                return (null, GetLoginErrorMessage(code));
            }

            // 3. Visit manager.jsp to extract server variables (UUID, USER_ID, ORG_ID, TENANT_ID)
            var mgrUrl = $"{baseUrl}/ioc/main/manager.jsp?func=page/olap/FNC002&mode=input&obj_type=1";
            var mgrResponse = await client.GetAsync(mgrUrl);
            var html = await mgrResponse.Content.ReadAsStringAsync();

            var mUuid = Regex.Match(html, @"var\s+uuid\s*=\s*'([^']+)'");
            var mUser = Regex.Match(html, @"var\s+user_id\s*=\s*'([^']+)'");
            var mOrg = Regex.Match(html, @"var\s+org_id\s*=\s*'([^']+)'");
            var mOrgType = Regex.Match(html, @"var\s+org_type\s*=\s*'([^']+)'");
            var mTenant = Regex.Match(html, @"var\s+tenant_id\s*=\s*'([^']+)'");

            var serverUuid = mUuid.Success ? mUuid.Groups[1].Value : Guid.NewGuid().ToString();
            var serverUserId = mUser.Success ? mUser.Groups[1].Value : string.Empty;
            var serverOrgId = mOrg.Success ? mOrg.Groups[1].Value : string.Empty;
            var serverOrgType = mOrgType.Success ? mOrgType.Groups[1].Value : "7";
            var serverTenantId = mTenant.Success ? mTenant.Groups[1].Value : "85";

            var cookies = cookieContainer.GetCookies(uri);
            var cookieList = new List<string>();
            foreach (Cookie c in cookies)
            {
                cookieList.Add($"{c.Name}={c.Value}");
            }
            var cookieString = string.Join("; ", cookieList);

            if (!cookieString.Contains("JSESSIONID"))
            {
                return (null, "Không nhận được phiên đăng nhập JSESSIONID từ máy chủ");
            }

            var session = new AuthSession
            {
                Username = username,
                CookieString = cookieString,
                Uuid = serverUuid,
                UserId = serverUserId,
                OrgId = serverOrgId,
                OrgType = serverOrgType,
                TenantId = serverTenantId,
                ApiUrl = apiUrl
            };

            return (session, string.Empty);
        }
        catch (Exception ex)
        {
            return (null, $"Lỗi đăng nhập: {ex.Message}");
        }
    }

    private static string GetLoginErrorMessage(string code) => code switch
    {
        "1" => "Tên đăng nhập hoặc mật khẩu không chính xác (Mã 1)",
        "3" => "Mã kiểm tra Captcha không hợp lệ (Mã 3)",
        "4" => "Tài khoản đang bị tạm khóa (Mã 4)",
        "9" => "Sai mật khẩu đăng nhập (Mã 9)",
        "10" => "Tài khoản bị vô hiệu hóa do đăng nhập sai nhiều lần (Mã 10)",
        _ => $"Đăng nhập không thành công (Mã lỗi: {code})"
    };

    public async Task SaveCredentialsAsync(AccountCredential credential)
    {
        try
        {
            if (!Directory.Exists(SavedCredentialFolder))
            {
                Directory.CreateDirectory(SavedCredentialFolder);
            }

            var list = await LoadAllCredentialsAsync();
            // Xóa tài khoản cũ nếu cùng username để đưa lên đầu danh sách (dùng gần nhất)
            list.RemoveAll(x => string.Equals(x.Username, credential.Username, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, credential);

            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SavedCredentialFile, json);
        }
        catch { }
    }

    public async Task<List<AccountCredential>> LoadAllCredentialsAsync()
    {
        var list = new List<AccountCredential>();
        try
        {
            if (File.Exists(SavedCredentialFile))
            {
                var json = await File.ReadAllTextAsync(SavedCredentialFile);
                if (json.TrimStart().StartsWith("["))
                {
                    var parsed = JsonSerializer.Deserialize<List<AccountCredential>>(json);
                    if (parsed != null) list = parsed;
                }
                else
                {
                    var single = JsonSerializer.Deserialize<AccountCredential>(json);
                    if (single != null && !string.IsNullOrWhiteSpace(single.Username))
                    {
                        list.Add(single);
                    }
                }
            }
        }
        catch { }
        return list;
    }

    public async Task<AccountCredential?> LoadCredentialsAsync()
    {
        var list = await LoadAllCredentialsAsync();
        return list.FirstOrDefault();
    }

    public async Task DeleteCredentialAsync(string username)
    {
        try
        {
            if (!Directory.Exists(SavedCredentialFolder))
            {
                Directory.CreateDirectory(SavedCredentialFolder);
            }

            var list = await LoadAllCredentialsAsync();
            list.RemoveAll(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SavedCredentialFile, json);
        }
        catch { }
    }

    public async Task ClearSavedCredentialsAsync()
    {
        try
        {
            if (File.Exists(SavedCredentialFile))
            {
                File.Delete(SavedCredentialFile);
            }
        }
        catch { }
        await Task.CompletedTask;
    }

    public async Task<Dictionary<string, AccountCredential>> LoadPresetAccountsAsync()
    {
        var result = new Dictionary<string, AccountCredential>();
        try
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Account.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Account.json"),
                AccountPresetsFile
            };

            foreach (var p in searchPaths)
            {
                if (File.Exists(p))
                {
                    var json = await File.ReadAllTextAsync(p);
                    using var doc = JsonDocument.Parse(json);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var unitName = prop.Name;
                        var elem = prop.Value;
                        var username = elem.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
                        var password = elem.TryGetProperty("password", out var pwd) ? pwd.GetString() ?? "" : "";

                        if (!string.IsNullOrEmpty(username))
                        {
                            result[unitName] = new AccountCredential
                            {
                                UnitName = unitName,
                                Username = username,
                                Password = password
                            };
                        }
                    }
                    break;
                }
            }
        }
        catch { }
        return result;
    }
}
