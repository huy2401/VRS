using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VSR.Models;
using VSR.Services;

namespace VSR.ViewModels;

public class LoginViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _apiUrl = "https://baocao.hatinh.gov.vn/ioc/RestService";
    private bool _rememberMe = true;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _hasError;
    private AccountCredential? _selectedSavedAccount;
    private bool _isAccountDropdownOpen;
    private bool _isLoginBlocked;
    private readonly System.Collections.Generic.List<AccountCredential> _allSavedAccounts = new();

    public ObservableCollection<AccountCredential> SavedAccounts { get; } = new();
    public ObservableCollection<AccountCredential> FilteredSavedAccounts { get; } = new();

    public event Action<AuthSession>? LoginSuccess;

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    private bool _revealPassword;
    public bool RevealPassword
    {
        get => _revealPassword;
        set => SetProperty(ref _revealPassword, value);
    }

    public string ApiUrl
    {
        get => _apiUrl;
        set => SetProperty(ref _apiUrl, value);
    }

    public bool RememberMe
    {
        get => _rememberMe;
        set => SetProperty(ref _rememberMe, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public bool IsAccountDropdownOpen
    {
        get => _isAccountDropdownOpen;
        set => SetProperty(ref _isAccountDropdownOpen, value);
    }

    /// <summary>Đăng nhập bị khóa khi đã phát hiện bản VSR bắt buộc cập nhật.</summary>
    public bool IsLoginBlocked
    {
        get => _isLoginBlocked;
        set
        {
            if (SetProperty(ref _isLoginBlocked, value))
                OnPropertyChanged(nameof(CanLogin));
        }
    }

    public bool CanLogin => !IsLoading && !IsLoginBlocked;

    public AccountCredential? SelectedSavedAccount
    {
        get => _selectedSavedAccount;
        set
        {
            if (SetProperty(ref _selectedSavedAccount, value) && value != null)
            {
                SelectAccount(value);
            }
        }
    }

    public LoginViewModel(AuthService authService)
    {
        _authService = authService;
    }

    public async Task InitializeAsync()
    {
        await ReloadSavedAccountsAsync();
    }

    public async Task ReloadSavedAccountsAsync(string? preferredUsername = null)
    {
        _allSavedAccounts.Clear();
        SavedAccounts.Clear();
        FilteredSavedAccounts.Clear();

        var allSaved = await _authService.LoadAllCredentialsAsync();

        foreach (var acc in allSaved)
        {
            _allSavedAccounts.Add(acc);
            SavedAccounts.Add(acc);
            FilteredSavedAccounts.Add(acc);
        }

        // Tự động điền tài khoản ưu tiên (hoặc tài khoản dùng gần nhất)
        AccountCredential? targetAccount = null;
        var lookupUser = !string.IsNullOrWhiteSpace(preferredUsername) ? preferredUsername : _username;
        if (!string.IsNullOrWhiteSpace(lookupUser))
        {
            targetAccount = SavedAccounts.FirstOrDefault(a => string.Equals(a.Username, lookupUser, StringComparison.OrdinalIgnoreCase));
        }

        targetAccount ??= SavedAccounts.FirstOrDefault();

        if (targetAccount != null)
        {
            SelectAccount(targetAccount);
        }
    }

    /// <summary>
    /// Hiển thị tất cả tài khoản trong danh sách gợi ý khi nhấp chuột
    /// </summary>
    public void ShowAllSavedAccounts()
    {
        FilteredSavedAccounts.Clear();
        foreach (var acc in _allSavedAccounts)
        {
            FilteredSavedAccounts.Add(acc);
        }
        IsAccountDropdownOpen = FilteredSavedAccounts.Count > 0;
    }

    /// <summary>
    /// So sánh và lọc danh sách gợi ý khi gõ phím
    /// </summary>
    public void FilterSavedAccounts(string query)
    {
        FilteredSavedAccounts.Clear();
        var q = query?.Trim().ToLower() ?? "";

        var matches = string.IsNullOrEmpty(q)
            ? _allSavedAccounts
            : _allSavedAccounts.Where(a => 
                (a.Username != null && a.Username.ToLower().Contains(q)) || 
                (a.UnitName != null && a.UnitName.ToLower().Contains(q))).ToList();

        foreach (var acc in matches)
        {
            FilteredSavedAccounts.Add(acc);
        }

        IsAccountDropdownOpen = FilteredSavedAccounts.Count > 0;
    }

    /// <summary>
    /// Điền tài khoản và mật khẩu khi người dùng chọn từ danh sách gợi ý
    /// </summary>
    public void SelectAccount(AccountCredential acc)
    {
        if (acc == null) return;
        _selectedSavedAccount = acc;
        Username = acc.Username;
        Password = acc.Password;
        if (!string.IsNullOrWhiteSpace(acc.ApiUrl))
        {
            ApiUrl = acc.ApiUrl;
        }
        IsAccountDropdownOpen = false;
        OnPropertyChanged(nameof(SelectedSavedAccount));
    }

    public async Task DeleteSavedAccountAsync(AccountCredential acc)
    {
        if (acc == null) return;
        await _authService.DeleteCredentialAsync(acc.Username);
        _allSavedAccounts.RemoveAll(x => string.Equals(x.Username, acc.Username, StringComparison.OrdinalIgnoreCase));
        SavedAccounts.Remove(acc);
        FilteredSavedAccounts.Remove(acc);

        if (Username == acc.Username)
        {
            if (SavedAccounts.Count > 0)
            {
                SelectAccount(SavedAccounts[0]);
            }
            else
            {
                _selectedSavedAccount = null;
                Username = string.Empty;
                Password = string.Empty;
                OnPropertyChanged(nameof(SelectedSavedAccount));
            }
        }

        IsAccountDropdownOpen = FilteredSavedAccounts.Count > 0;
    }

    public async Task ExecuteLoginAsync()
    {
        if (IsLoginBlocked)
        {
            HasError = true;
            StatusMessage = "Đã có phiên bản mới bắt buộc. Hãy cập nhật ứng dụng trước khi đăng nhập.";
            return;
        }

        if (IsLoading) return;

        IsLoading = true;
        OnPropertyChanged(nameof(CanLogin));
        StatusMessage = "Đang kết nối và đăng nhập máy chủ IOC...";
        HasError = false;

        var (session, error) = await _authService.LoginAsync(Username, Password, ApiUrl);

        if (session != null && session.IsAuthenticated)
        {
            StatusMessage = "Đăng nhập thành công!";
            HasError = false;

            if (RememberMe)
            {
                var cred = new AccountCredential
                {
                    Username = Username,
                    Password = Password,
                    ApiUrl = ApiUrl,
                    RememberMe = RememberMe,
                    UnitName = !string.IsNullOrWhiteSpace(session.UnitName) ? session.UnitName : Username
                };
                await _authService.SaveCredentialsAsync(cred);
            }

            IsLoading = false;
            OnPropertyChanged(nameof(CanLogin));
            LoginSuccess?.Invoke(session);
        }
        else
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanLogin));
            HasError = true;
            StatusMessage = string.IsNullOrWhiteSpace(error) ? "Đăng nhập không thành công" : error;
        }
    }
}
