using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VSR.Services;

public class VersionInfo
{
    public string CurrentVersion { get; set; } = "1.0.0";
    public string LatestVersion { get; set; } = string.Empty;
    public bool HasUpdate { get; set; }
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}

public class UpdateService
{
    // GitHub Releases API của ứng dụng VSR.
    public const string GitHubVersionCheckUrl = "https://api.github.com/repos/huy2401/VRS/releases/latest";

    public static string AppVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task<VersionInfo> CheckForUpdatesAsync(string checkUrl = GitHubVersionCheckUrl)
    {
        var info = new VersionInfo
        {
            CurrentVersion = AppVersion,
            LatestVersion = AppVersion,
            HasUpdate = false
        };

        if (string.IsNullOrWhiteSpace(checkUrl))
        {
            return info;
        }

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.Add("User-Agent", "VSR-App");

            var json = await client.GetStringAsync(checkUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Hỗ trợ cả định dạng GitHub Release API (tag_name, body, html_url)
            // hoặc file version.json đơn giản (version, notes, url)
            string latest = "";
            if (root.TryGetProperty("tag_name", out var tagProp))
                latest = tagProp.GetString()?.TrimStart('v', 'V') ?? "";
            else if (root.TryGetProperty("version", out var vProp))
                latest = vProp.GetString()?.TrimStart('v', 'V') ?? "";

            string notes = "";
            if (root.TryGetProperty("body", out var bodyProp))
                notes = bodyProp.GetString() ?? "";
            else if (root.TryGetProperty("notes", out var nProp))
                notes = nProp.GetString() ?? "";

            string url = "";
            // Ưu tiên đúng asset .exe của bản phát hành, không dùng URL trang Release.
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                var executable = assets.EnumerateArray().FirstOrDefault(asset =>
                    asset.TryGetProperty("name", out var name) &&
                    name.GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

                if (executable.ValueKind != JsonValueKind.Undefined &&
                    executable.TryGetProperty("browser_download_url", out var downloadProp))
                {
                    url = downloadProp.GetString() ?? "";
                }
            }

            if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("html_url", out var urlProp))
                url = urlProp.GetString() ?? "";
            else if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("url", out var uProp))
                url = uProp.GetString() ?? "";

            if (!string.IsNullOrWhiteSpace(latest))
            {
                info.LatestVersion = latest;
                info.ReleaseNotes = notes;
                info.DownloadUrl = url;

                if (Version.TryParse(latest, out var latestVer) && Version.TryParse(AppVersion, out var currVer))
                {
                    info.HasUpdate = latestVer > currVer;
                }
                else
                {
                    info.HasUpdate = !string.Equals(latest, AppVersion, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi mạng khi kiểm tra phiên bản
        }

        return info;
    }

    /// <summary>Tải VSR.exe mới, thay thế bản hiện tại sau khi ứng dụng đã thoát, rồi khởi động lại.</summary>
    public async Task<(bool Success, string Message)> DownloadAndInstallAsync(
        string downloadUrl,
        IProgress<(long BytesRead, long TotalBytes, double Percentage)>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return (false, "Không tìm thấy file VSR.exe của bản phát hành mới.");

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            try
            {
                executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return (false, "Không xác định được file VSR.exe hiện tại để cập nhật.");

        var downloadedPath = executablePath + ".new";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.Add("User-Agent", "VSR-App");

            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = new FileStream(downloadedPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        double pct = (double)totalRead / totalBytes * 100.0;
                        progress?.Report((totalRead, totalBytes, pct));
                    }
                    else
                    {
                        progress?.Report((totalRead, -1L, -1.0));
                    }
                }
            }

            if (!File.Exists(downloadedPath) || new FileInfo(downloadedPath).Length == 0)
                return (false, "File cập nhật tải về không hợp lệ hoặc bị rỗng.");

            var scriptPath = Path.Combine(Path.GetTempPath(), $"VSR-update-{Guid.NewGuid():N}.cmd");
            var script = "@echo off\r\n" +
                         "setlocal enabledelayedexpansion\r\n" +
                         $"set \"SOURCE={downloadedPath}\"\r\n" +
                         $"set \"TARGET={executablePath}\"\r\n" +
                         "set RETRIES=0\r\n" +
                         ":LOOP\r\n" +
                         "if not exist \"!SOURCE!\" exit /b 1\r\n" +
                         "move /y \"!SOURCE!\" \"!TARGET!\" >nul 2>&1\r\n" +
                         "if not errorlevel 1 goto LAUNCH\r\n" +
                         "set /a RETRIES+=1\r\n" +
                         "if !RETRIES! geq 30 exit /b 1\r\n" +
                         "timeout /t 1 /nobreak >nul\r\n" +
                         "goto LOOP\r\n" +
                         ":LAUNCH\r\n" +
                         "timeout /t 1 /nobreak >nul\r\n" +
                         "start \"\" \"!TARGET!\"\r\n" +
                         "del \"%~f0\"\r\n";

            await File.WriteAllTextAsync(scriptPath, script);

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            return (true, "Đã tải bản mới thành công. Đang khởi động lại...");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(downloadedPath)) File.Delete(downloadedPath); } catch { }
            return (false, $"Không thể tải bản cập nhật: {ex.Message}");
        }
    }
}
