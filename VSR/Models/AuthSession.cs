namespace VSR.Models;

public class AuthSession
{
    public string Username { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string CookieString { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string OrgType { get; set; } = "7";
    public string TenantId { get; set; } = "85";
    public string ApiUrl { get; set; } = "https://baocao.hatinh.gov.vn/ioc/RestService";
    public bool IsAuthenticated => !string.IsNullOrEmpty(CookieString) && !string.IsNullOrEmpty(Uuid);
}
