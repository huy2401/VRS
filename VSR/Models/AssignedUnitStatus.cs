using System;
using VSR.ViewModels;

namespace VSR.Models;

public class AssignedUnitStatus : ViewModelBase
{
    private int _stt;
    private string _unitName = string.Empty;
    private string _orgCode = string.Empty;
    private string _orgId = string.Empty;
    private int _statusCode;
    private string _statusStr = "Đang nhập liệu/tổng hợp";
    private string _submitDate = string.Empty;

    public int StatusCode
    {
        get => _statusCode;
        set
        {
            if (SetProperty(ref _statusCode, value))
            {
                OnPropertyChanged(nameof(IsApproved));
                OnPropertyChanged(nameof(IsEligible));
            }
        }
    }

    public int Stt
    {
        get => _stt;
        set => SetProperty(ref _stt, value);
    }

    public string UnitName
    {
        get => _unitName;
        set => SetProperty(ref _unitName, value);
    }

    /// <summary>
    /// Mã đơn vị (dùng cho trường "org" trong header API getReport, ví dụ: STC, CT, CTK, SCT, VPUBT, SNV...)
    /// </summary>
    public string OrgCode
    {
        get => _orgCode;
        set => SetProperty(ref _orgCode, value);
    }

    public string OrgId
    {
        get => _orgId;
        set => SetProperty(ref _orgId, value);
    }

    /// <summary>
    /// Trạng thái nộp báo cáo (STATUS_STR, ví dụ: "Đã giao", "Đang nhập liệu/tổng hợp", "Đã trình lãnh đạo", "Báo cáo đã được gửi", "Báo cáo đã được duyệt...")
    /// </summary>
    public string StatusStr
    {
        get => _statusStr;
        set
        {
            if (SetProperty(ref _statusStr, value))
            {
                OnPropertyChanged(nameof(IsEligible));
                OnPropertyChanged(nameof(StatusBadgeBackground));
                OnPropertyChanged(nameof(StatusBadgeForeground));
                OnPropertyChanged(nameof(AggregationEligibilityText));
                OnPropertyChanged(nameof(AggregationEligibilityForeground));
            }
        }
    }

    public string SubmitDate
    {
        get => _submitDate;
        set => SetProperty(ref _submitDate, value);
    }

    /// <summary>
    /// Đơn vị có trạng thái ĐÃ DUYỆT (StatusCode == 4 hoặc chứa "đã được duyệt", "đã phê duyệt")
    /// </summary>
    public bool IsApproved
    {
        get
        {
            if (StatusCode == 4) return true;
            if (string.IsNullOrWhiteSpace(StatusStr)) return false;
            var s = StatusStr.Trim().ToLowerInvariant();
            return s.Contains("đã được duyệt") || s.Contains("đã phê duyệt") || s.Contains("da duoc duyet") || s.Contains("da phe duyet");
        }
    }

    /// <summary>
    /// Ngoại trừ "STATUS_STR": "Đã giao" (hoặc StatusCode == 1), các STATUS còn lại đều hợp lệ để tổng hợp
    /// </summary>
    public bool IsEligible
    {
        get
        {
            if (StatusCode == 1) return false;
            if (string.IsNullOrWhiteSpace(StatusStr)) return true;
            var s = StatusStr.Trim();
            return !s.Equals("Đã giao", StringComparison.OrdinalIgnoreCase) &&
                   !s.Equals("Da giao", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string StatusBadgeBackground
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StatusStr)) return "#F1F5F9";
            var s = StatusStr.ToLower();
            if (s == "đã giao" || s == "da giao") return "#F1F5F9"; // Xám nhạt
            if (s.Contains("duyệt") || s.Contains("phê duyệt")) return "#DCFCE7"; // Xanh lá
            if (s.Contains("đã gửi") || s.Contains("trình")) return "#DBEAFE"; // Xanh dương
            if (s.Contains("từ chối") || s.Contains("trả lại")) return "#FEE2E2"; // Đỏ nhạt
            return "#FEF3C7"; // Vàng nhạt (Đang nhập)
        }
    }

    public string StatusBadgeForeground
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StatusStr)) return "#64748B";
            var s = StatusStr.ToLower();
            if (s == "đã giao" || s == "da giao") return "#64748B";
            if (s.Contains("duyệt") || s.Contains("phê duyệt")) return "#166534";
            if (s.Contains("đã gửi") || s.Contains("trình")) return "#1E40AF";
            if (s.Contains("từ chối") || s.Contains("trả lại")) return "#991B1B";
            return "#92400E";
        }
    }

    public string AggregationEligibilityText => IsEligible
        ? "✅ Hợp lệ (Sẽ tổng hợp & ghi đè)"
        : "⏳ Đã giao (Bỏ qua)";

    public string AggregationEligibilityForeground => IsEligible
        ? "#16A34A"
        : "#94A3B8";
}
