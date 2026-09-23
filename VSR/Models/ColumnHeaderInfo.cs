namespace VSR.Models;

public class ColumnHeaderInfo
{
    public string AttrId { get; set; } = string.Empty;
    public string AttrCode { get; set; } = "CTKTXH";
    public string FldCode { get; set; } = "FN01";
    public string HeaderName { get; set; } = "Giá trị";
    public string Field { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public bool IsVisible { get; set; } = true;
    public double ColWidth { get; set; } = 150;
    public string DataType { get; set; } = "Real"; // "Integer", "Real", "String"
    public int DecimalDigits { get; set; } = 4;    // Cấu hình số thập phân (mặc định 4)
    // Giới hạn ký tự do IOC cấu hình. IOC trả 0 (hoặc không trả) được hiểu là 1.000 ký tự.
    public int MaxLength { get; set; } = 1000;
    public string Align { get; set; } = "Right";   // "Left", "Right", "Center"
    public double FontSize { get; set; } = 12;

    public bool IsStringColumn =>
        DataType.Equals("String", System.StringComparison.OrdinalIgnoreCase) ||
        DataType == "3" ||
        DataType.ToLower().Contains("chuỗi") ||
        DataType.ToLower().Contains("text") ||
        DataType.ToLower().Contains("varchar") ||
        DataType.ToLower().Contains("char") ||
        DataType.ToLower().Contains("date");

    public bool IsIntegerColumn =>
        DataType.Equals("Integer", System.StringComparison.OrdinalIgnoreCase) ||
        DataType == "1" ||
        DataType.ToLower().Contains("nguyên") ||
        DataType.ToLower().Contains("int");

    public bool IsNumericColumn => !IsStringColumn;
}
