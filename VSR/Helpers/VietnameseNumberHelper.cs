using System;
using System.Globalization;
using System.Linq;

namespace VSR.Helpers;

public static class VietnameseNumberHelper
{
    private static readonly NumberFormatInfo ViNfi = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = new[] { 3 }
    };

    /// <summary>
    /// Loại bỏ dấu tiếng Việt (ví dụ: "Mã chỉ tiêu" -> "Ma chi tieu", "đơn vị" -> "don vi")
    /// </summary>
    public static string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalizedString.Length);

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                if (c == 'đ' || c == 'Đ')
                {
                    sb.Append(c == 'đ' ? 'd' : 'D');
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>
    /// Chuẩn hóa chuỗi tìm kiếm / khớp tiêu đề (loại bỏ dấu, khoảng trắng, ký tự đặc biệt, chuyển về chữ thường)
    /// </summary>
    public static string CleanSearchKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var noAccents = RemoveDiacritics(s);
        return new string(noAccents.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    /// <summary>
    /// Chuẩn hóa mã chỉ tiêu / STT (giữ lại chữ cái, số, gạch dưới, gạch ngang, dấu chấm)
    /// </summary>
    public static string CleanCode(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var noAccents = RemoveDiacritics(s);
        return new string(noAccents.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.').ToArray()).ToLowerInvariant();
    }

    /// <summary>
    /// Kiểm tra xem một chuỗi có thực sự là số hay không (không phải chữ, mã ký hiệu, ngày tháng, văn bản)
    /// </summary>
    public static bool IsNumericValue(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        if (s == "-" || s == "," || s == ".") return false;

        // Nếu chuỗi chứa chữ cái hoặc ký tự đặc biệt như _, /, :, @, #, $, %, ^, &, *, (, )
        foreach (var c in s)
        {
            if (char.IsLetter(c) || c == '_' || c == '/' || c == ':' || c == ';' || c == '@' || c == '#' || c == '$' || c == '(' || c == ')')
            {
                return false;
            }
        }

        // Định dạng ngày tháng dạng yyyy-MM-dd, dd-MM-yyyy, dd/MM/yyyy
        if (s.Contains('-') && s.IndexOf('-') > 0)
        {
            return false;
        }

        // Kiểm tra dấu trừ chỉ được xuất hiện ở đầu chuỗi (số âm)
        if (s.StartsWith('-') || s.StartsWith('+'))
        {
            s = s[1..].Trim();
        }

        if (string.IsNullOrEmpty(s)) return false;

        // Lấy tất cả ký tự không phải dấu chấm, phẩy, khoảng trắng
        var digitsOnly = s.Replace(".", "").Replace(",", "").Replace(" ", "");
        if (digitsOnly.Length == 0 || !digitsOnly.All(char.IsDigit))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Định dạng số thực theo chuẩn Việt Nam: Phân cách hàng nghìn bằng '.', phân cách thập phân bằng ','
    /// Nếu đầu vào là văn bản, ngày tháng, mã thì giữ nguyên không can thiệp.
    /// Ví dụ: 1234 -> 1.234, 183806 -> 183.806, 188607.56 -> 188.607,56, "NQ138/NQ-CP" -> "NQ138/NQ-CP"
    /// </summary>
    public static string FormatVietnameseNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var s = input.Trim();
        if (!IsNumericValue(s))
        {
            return s;
        }

        if (s == "-" || s == "," || s == ".")
            return s;

        bool isNegative = s.StartsWith('-');
        if (isNegative) s = s[1..].Trim();

        string intPartRaw;
        string decPartRaw = "";
        bool hasDecimal = false;

        // 1. Phẩy (,) luôn là dấu thập phân trong định dạng Việt Nam
        if (s.Contains(','))
        {
            var parts = s.Split(new[] { ',' }, 2);
            intPartRaw = parts[0].Replace(".", "");
            decPartRaw = parts.Length > 1 ? parts[1] : "";
            hasDecimal = true;
        }
        // 2. Chứa dấu chấm (.) và không có dấu phẩy
        else if (s.Contains('.'))
        {
            if (s.Count(c => c == '.') > 1)
            {
                // Nhiều dấu chấm -> phân cách hàng nghìn (VD: 1.234.567)
                intPartRaw = s.Replace(".", "");
            }
            else
            {
                // 1 dấu chấm:
                var parts = s.Split(new[] { '.' }, 2);
                var left = parts[0];
                var right = parts.Length > 1 ? parts[1] : "";

                // Nếu phần sau dấu chấm có đúng 3 chữ số và phần trước khác "0" -> là phân cách hàng nghìn tiếng Việt (VD: 183.806 -> 183806)
                if (right.Length == 3 && left != "0" && !left.StartsWith("0"))
                {
                    intPartRaw = left + right;
                }
                else
                {
                    // Dấu chấm thập phân từ Excel/JSON (VD: 4801.56 hoặc 0.5)
                    intPartRaw = left;
                    decPartRaw = right;
                    hasDecimal = true;
                }
            }
        }
        else
        {
            intPartRaw = s;
        }

        // Lọc chỉ giữ chữ số
        var intDigits = new string(intPartRaw.Where(char.IsDigit).ToArray());
        var decDigits = new string(decPartRaw.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(intDigits) && string.IsNullOrEmpty(decDigits))
        {
            return isNegative ? "-" : "";
        }

        string formattedInt = "";
        if (long.TryParse(intDigits, out var num))
        {
            formattedInt = num.ToString("#,##0", ViNfi);
        }
        else if (!string.IsNullOrEmpty(intDigits))
        {
            formattedInt = FormatLargeDigits(intDigits);
        }
        else
        {
            formattedInt = "0";
        }

        string result = (isNegative ? "-" : "") + formattedInt;
        if (hasDecimal)
        {
            result += "," + decDigits;
        }

        return result;
    }

    /// <summary>
    /// Chuyển đổi chuỗi định dạng tiếng Việt (183.806 hoặc 4.801,56) thành chuỗi số thực chuẩn (183806 hoặc 4801.56)
    /// Nếu là văn bản, ngày tháng, mã thì trả về nguyên bản chuỗi.
    /// </summary>
    public static string ToStandardDecimalString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var s = input.Trim();
        if (!IsNumericValue(s))
            return s;

        if (s == "-" || s == "," || s == ".")
            return "";

        bool isNegative = s.StartsWith('-');
        if (isNegative) s = s[1..].Trim();

        string intPart = "";
        string decPart = "";

        if (s.Contains(','))
        {
            var parts = s.Split(new[] { ',' }, 2);
            intPart = new string(parts[0].Where(char.IsDigit).ToArray());
            decPart = parts.Length > 1 ? new string(parts[1].Where(char.IsDigit).ToArray()) : "";
        }
        else if (s.Contains('.'))
        {
            if (s.Count(c => c == '.') > 1)
            {
                // Nhiều dấu chấm -> phân cách hàng nghìn (VD: 1.234.567 -> 1234567)
                intPart = new string(s.Where(char.IsDigit).ToArray());
            }
            else
            {
                var parts = s.Split(new[] { '.' }, 2);
                var left = new string(parts[0].Where(char.IsDigit).ToArray());
                var right = parts.Length > 1 ? new string(parts[1].Where(char.IsDigit).ToArray()) : "";

                // Nếu có đúng 3 chữ số sau dấu chấm và phần trước != 0 -> phân cách hàng nghìn (VD: 183.806 -> 183806)
                if (right.Length == 3 && left != "0" && !left.StartsWith("0"))
                {
                    intPart = left + right;
                }
                else
                {
                    intPart = left;
                    decPart = right;
                }
            }
        }
        else
        {
            intPart = new string(s.Where(char.IsDigit).ToArray());
        }

        if (string.IsNullOrEmpty(intPart) && string.IsNullOrEmpty(decPart))
            return "";

        if (string.IsNullOrEmpty(intPart)) intPart = "0";

        var res = (isNegative ? "-" : "") + intPart;
        if (!string.IsNullOrEmpty(decPart))
        {
            res += "." + decPart;
        }

        return res;
    }

    /// <summary>
    /// Định dạng giá trị số nhận từ IOC để hiển thị theo chuẩn Việt Nam.
    /// IOC luôn dùng dấu chấm làm dấu thập phân trong ATTR_VAL, kể cả khi phần thập phân có 3 chữ số.
    /// Ví dụ: "183.806" -&gt; "183,806" và "4985.366" -&gt; "4.985,366".
    /// </summary>
    public static string FormatVietnameseNumberFromIoc(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var s = input.Trim();
        if (!IsNumericValue(s) || s is "-" or "," or ".")
            return s;

        var isNegative = s.StartsWith('-');
        if (isNegative || s.StartsWith('+'))
            s = s[1..].Trim();

        // Dữ liệu không thuộc IOC hoặc đã được định dạng theo Việt Nam vẫn giữ xử lý thông thường.
        if (s.Contains(','))
            return FormatVietnameseNumber(input);

        var parts = s.Split('.', 2);
        var integerDigits = new string(parts[0].Where(char.IsDigit).ToArray());
        var decimalDigits = parts.Length > 1
            ? new string(parts[1].Where(char.IsDigit).ToArray())
            : string.Empty;

        if (string.IsNullOrEmpty(integerDigits)) integerDigits = "0";

        var formattedInteger = long.TryParse(integerDigits, out var integerValue)
            ? integerValue.ToString("#,##0", ViNfi)
            : FormatLargeDigits(integerDigits);

        return (isNegative ? "-" : "") + formattedInteger +
               (parts.Length > 1 && !string.IsNullOrEmpty(decimalDigits) ? "," + decimalDigits : string.Empty);
    }

    /// <summary>
    /// Định dạng số double theo chuẩn Việt Nam (phân cách hàng nghìn bằng '.', phân cách thập phân bằng ',').
    /// Đảm bảo không bao giờ bị nhầm phần thập phân 3 chữ số thành phân cách hàng nghìn.
    /// Ví dụ: 4985.366 -> "4.985,366", 183.806 -> "183,806".
    /// </summary>
    public static string FormatVietnameseDouble(double value, int maxDecimals = 4)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return string.Empty;

        if (Math.Abs(value) < 1e-9)
            value = 0;

        string format = maxDecimals > 0 ? "0." + new string('#', maxDecimals) : "0";
        var str = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        return FormatVietnameseNumberFromIoc(str);
    }

    private static string FormatLargeDigits(string digits)
    {
        var sb = new System.Text.StringBuilder();
        int len = digits.Length;
        for (int i = 0; i < len; i++)
        {
            if (i > 0 && (len - i) % 3 == 0)
            {
                sb.Append('.');
            }
            sb.Append(digits[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Chuyển đổi số hiển thị có dấu chấm phân cách hàng nghìn thành chuỗi thô khi người dùng nhấn chuột vào ô để chỉnh sửa (tương tự Excel).
    /// Ví dụ: "1.234.567,89" -> "1234567,89", "183.806" -> "183806"
    /// </summary>
    public static string ToEditNumberString(string? input, int maxDecimalDigits = 4)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Trim();

        bool isNegative = s.StartsWith('-');
        if (isNegative || s.StartsWith('+'))
            s = s[1..].Trim();

        if (s.Contains(','))
        {
            var parts = s.Split(',', 2);
            var intDigits = new string(parts[0].Where(char.IsDigit).ToArray());
            var decDigits = parts.Length > 1 ? new string(parts[1].Where(char.IsDigit).ToArray()) : "";
            if (maxDecimalDigits > 0 && decDigits.Length > maxDecimalDigits)
            {
                decDigits = decDigits.Substring(0, maxDecimalDigits);
            }
            if (string.IsNullOrEmpty(intDigits)) intDigits = "0";
            if (maxDecimalDigits <= 0)
            {
                return (isNegative ? "-" : "") + intDigits;
            }
            return (isNegative ? "-" : "") + intDigits + (string.IsNullOrEmpty(decDigits) ? "" : "," + decDigits);
        }
        else if (s.Contains('.'))
        {
            if (s.Count(c => c == '.') > 1)
            {
                var digits = new string(s.Where(char.IsDigit).ToArray());
                return (isNegative ? "-" : "") + digits;
            }
            else
            {
                var parts = s.Split('.', 2);
                var left = new string(parts[0].Where(char.IsDigit).ToArray());
                var right = parts.Length > 1 ? new string(parts[1].Where(char.IsDigit).ToArray()) : "";

                if (right.Length == 3 && left != "0" && !left.StartsWith("0"))
                {
                    // Dấu chấm phân cách hàng nghìn
                    return (isNegative ? "-" : "") + left + right;
                }
                else
                {
                    // Dấu chấm thập phân từ IOC
                    if (string.IsNullOrEmpty(left)) left = "0";
                    if (maxDecimalDigits > 0 && right.Length > maxDecimalDigits)
                    {
                        right = right.Substring(0, maxDecimalDigits);
                    }
                    if (maxDecimalDigits <= 0)
                    {
                        return (isNegative ? "-" : "") + left;
                    }
                    return (isNegative ? "-" : "") + left + (string.IsNullOrEmpty(right) ? "" : "," + right);
                }
            }
        }
        else
        {
            var digits = new string(s.Where(char.IsDigit).ToArray());
            return (isNegative ? "-" : "") + digits;
        }
    }

    /// <summary>
    /// Kiểm tra chuỗi nhập cho kiểu Số nguyên: Chỉ gồm chữ số, tối đa 15 ký tự (cho phép dấu - ở đầu).
    /// </summary>
    public static bool IsValidIntegerInput(string text, int maxDigits = 15)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (text == "-") return true;

        var s = text;
        if (s.StartsWith('-')) s = s[1..];

        if (!s.All(char.IsDigit)) return false;
        if (s.Length > maxDigits) return false;

        return true;
    }

    /// <summary>
    /// Kiểm tra chuỗi nhập cho kiểu Số thực: Chỉ gồm chữ số và tối đa 1 dấu phẩy ',', phần nguyên tối đa 15 ký tự, phần thập phân tối đa theo cấu hình.
    /// </summary>
    public static bool IsValidRealInput(string text, int maxIntegerDigits = 15, int maxDecimalDigits = 4)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (text == "-" || text == "," || text == "-,")
        {
            return maxDecimalDigits > 0 || !text.Contains(',');
        }

        var s = text;
        if (s.StartsWith('-')) s = s[1..];

        int commaCount = s.Count(c => c == ',');
        if (commaCount > 1) return false;
        if (commaCount == 1 && maxDecimalDigits <= 0) return false;

        if (!s.All(c => char.IsDigit(c) || c == ',')) return false;

        if (commaCount == 1)
        {
            var parts = s.Split(',', 2);
            var intPart = parts[0];
            var decPart = parts.Length > 1 ? parts[1] : "";

            if (intPart.Length > maxIntegerDigits) return false;
            if (decPart.Length > maxDecimalDigits) return false;
        }
        else
        {
            if (s.Length > maxIntegerDigits) return false;
        }

        return true;
    }

    /// <summary>
    /// Chuẩn hóa chuỗi số nguyên khi người dùng dán (Paste) dữ liệu từ ngoài vào.
    /// </summary>
    public static string SanitizeIntegerString(string? input, int maxDigits = 15)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Trim();
        bool isNeg = s.StartsWith('-');
        if (s == "-") return "-";
        var digits = new string(s.Where(char.IsDigit).ToArray());
        if (digits.Length > maxDigits)
        {
            digits = digits.Substring(0, maxDigits);
        }
        if (string.IsNullOrEmpty(digits))
        {
            return isNeg ? "-" : "";
        }
        return (isNeg ? "-" : "") + digits;
    }

    /// <summary>
    /// Chuẩn hóa chuỗi số thực khi người dùng dán (Paste) dữ liệu từ ngoài vào.
    /// </summary>
    public static string SanitizeRealString(string? input, int maxIntegerDigits = 15, int maxDecimalDigits = 4)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Trim();
        bool isNeg = s.StartsWith('-');

        string intPartRaw;
        string decPartRaw = "";
        bool hasComma = false;

        if (s.Contains(','))
        {
            var parts = s.Split(',', 2);
            intPartRaw = parts[0];
            decPartRaw = parts.Length > 1 ? parts[1] : "";
            hasComma = true;
        }
        else if (s.Contains('.') && maxDecimalDigits > 0)
        {
            if (s.Count(c => c == '.') > 1)
            {
                intPartRaw = s.Replace(".", "");
            }
            else
            {
                var parts = s.Split('.', 2);
                intPartRaw = parts[0];
                decPartRaw = parts.Length > 1 ? parts[1] : "";
                hasComma = true;
            }
        }
        else
        {
            intPartRaw = s;
        }

        var intDigits = new string(intPartRaw.Where(char.IsDigit).ToArray());
        if (intDigits.Length > maxIntegerDigits)
        {
            intDigits = intDigits.Substring(0, maxIntegerDigits);
        }

        var decDigits = new string(decPartRaw.Where(char.IsDigit).ToArray());
        if (decDigits.Length > maxDecimalDigits)
        {
            decDigits = decDigits.Substring(0, maxDecimalDigits);
        }

        if (string.IsNullOrEmpty(intDigits) && string.IsNullOrEmpty(decDigits))
        {
            if (hasComma && maxDecimalDigits > 0)
            {
                return (isNeg ? "-" : "") + ",";
            }
            return isNeg ? "-" : "";
        }

        string result = (isNeg ? "-" : "") + (string.IsNullOrEmpty(intDigits) ? "0" : intDigits);
        if (hasComma && maxDecimalDigits > 0)
        {
            result += "," + decDigits;
        }

        return result;
    }
}
