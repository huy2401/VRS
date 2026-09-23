namespace VSR.Models;

public class AccountCredential
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = "https://baocao.hatinh.gov.vn/ioc/RestService";
    public bool RememberMe { get; set; } = true;
    public bool AutoLogin { get; set; } = true;
    public string UnitName { get; set; } = string.Empty;
}
