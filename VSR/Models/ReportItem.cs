using System;
using VSR.ViewModels;

namespace VSR.Models;

public enum ReportCategoryType
{
    Input = 0,     // Mục 1: Nhập báo cáo số liệu
    Submit = 1,    // Mục 2: Gửi báo cáo
    Tracking = 2,  // Mục 3: Theo dõi trạng thái báo cáo
    Approval = 3,  // Mục 4: Duyệt báo cáo
    Aggregate = 4  // Mục 5: Tổng hợp báo cáo
}

public class ReportItem : ViewModelBase
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public int Stt { get; set; }
    public string InputGrantId { get; set; } = string.Empty;
    public string ObjId { get; set; } = string.Empty;
    public string ObjName { get; set; } = string.Empty;
    public string TimeId { get; set; } = string.Empty;
    public string TimeName { get; set; } = string.Empty;
    public string PeriodTypeName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string ApprovedDate { get; set; } = string.Empty;
    public string OrgName { get; set; } = string.Empty;

    public string ObjCode { get; set; } = string.Empty;
    public int? DecimalDigits { get; set; }

    private string _senderOrgName = string.Empty;
    /// <summary>
    /// Đơn vị giao báo cáo
    /// </summary>
    public string SenderOrgName
    {
        get => !string.IsNullOrWhiteSpace(_senderOrgName) ? _senderOrgName : (!string.IsNullOrWhiteSpace(OrgName) ? OrgName : string.Empty);
        set => _senderOrgName = value;
    }

    private string _reportOrgName = string.Empty;
    /// <summary>
    /// Đơn vị báo cáo (ví dụ: Sở Y tế, Sở Tài Chính, Sở GD&ĐT...)
    /// </summary>
    public string ReportOrgName
    {
        get => !string.IsNullOrWhiteSpace(_reportOrgName) ? _reportOrgName : (!string.IsNullOrWhiteSpace(OrgName) ? OrgName : string.Empty);
        set => _reportOrgName = value;
    }

    /// <summary>
    /// Mã ID của Đơn vị báo cáo (phục vụ lấy đúng danh sách chỉ tiêu phân quyền cho đơn vị đó)
    /// </summary>
    public string TargetOrgId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Lý do bị từ chối / Ghi chú từ cấp trên / Ý kiến trình lãnh đạo
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Ý kiến của người nhập khi trình lãnh đạo
    /// </summary>
    public string SubmitOpinion { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái yêu cầu đính chính (CORRECTION_REQ: 0 = Chưa gửi, 1 = Đã gửi yêu cầu)
    /// </summary>
    public int CorrectionReq { get; set; } = 0;

    /// <summary>
    /// Mã trạng thái số (STATE_ID)
    /// </summary>
    public int StateId { get; set; } = 0;

    /// <summary>
    /// Phân loại danh mục báo cáo hiển thị
    /// </summary>
    public ReportCategoryType Category { get; set; } = ReportCategoryType.Input;

    /// <summary>
    /// Tên trạng thái chuẩn hóa tiếng Việt hiển thị trên giao diện
    /// </summary>
    public string DisplayStatusName => ResolveStatusName();

    /// <summary>
    /// Ngày hiển thị trên bảng:
    /// - Tại mục Theo dõi trạng thái và Duyệt báo cáo: hiển thị "Ngày duyệt"
    ///   + Nếu trạng thái là "Báo cáo đã gửi" (StateId == 3): để trống
    ///   + Nếu trạng thái là "Báo cáo đã được duyệt" (StateId == 4): hiển thị ngày duyệt
    /// - Tại các mục khác (Nhập báo cáo, Gửi báo cáo, Tổng hợp báo cáo): hiển thị Ngày kết thúc (EndDate)
    /// </summary>
    public string DisplayDateColumn
    {
        get
        {
            if (IsTrackingCategory || IsApprovalCategory)
            {
                var isApproved = StateId == 4 || Status is "4" or "4.0" || DisplayStatusName.Contains("đã duyệt") || DisplayStatusName.Contains("phê duyệt");
                if (isApproved)
                {
                    return !string.IsNullOrWhiteSpace(ApprovedDate) ? ApprovedDate : EndDate;
                }
                // Nếu trạng thái là báo cáo đã gửi hoặc chưa duyệt thì để trống
                return string.Empty;
            }

            return EndDate;
        }
    }

    /// <summary>
    /// Mã số phục vụ sắp xếp ngày tháng theo thứ tự thời gian chuẩn (yyyyMMdd).
    /// Hỗ trợ cả định dạng ngày Việt Nam dd/MM/yyyy và các định dạng yyyy-MM-dd.
    /// Giá trị rỗng được đưa về 0 (đầu hoặc cuối tùy hướng sắp xếp).
    /// </summary>
    public long DateSortKey => CalculateDateSortKey(DisplayDateColumn);

    public static long CalculateDateSortKey(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return 0;

        var cleaned = dateStr.Trim();
        var parts = cleaned.Split(new[] { '/', '-', ' ', ':', '.' }, StringSplitOptions.RemoveEmptyEntries);

        // Case 1: dd/MM/yyyy [HH:mm:ss]
        if (parts.Length >= 3 && parts[2].Length == 4 &&
            int.TryParse(parts[0], out var d) &&
            int.TryParse(parts[1], out var m) &&
            int.TryParse(parts[2], out var y))
        {
            var h = (parts.Length >= 4 && int.TryParse(parts[3], out var hour)) ? hour : 0;
            var min = (parts.Length >= 5 && int.TryParse(parts[4], out var minute)) ? minute : 0;
            var s = (parts.Length >= 6 && int.TryParse(parts[5], out var sec)) ? sec : 0;
            return (y * 10000L + m * 100L + d) * 1000000L + (h * 10000L + min * 100L + s);
        }

        // Case 2: yyyy/MM/dd [HH:mm:ss]
        if (parts.Length >= 3 && parts[0].Length == 4 &&
            int.TryParse(parts[0], out var y2) &&
            int.TryParse(parts[1], out var m2) &&
            int.TryParse(parts[2], out var d2))
        {
            var h = (parts.Length >= 4 && int.TryParse(parts[3], out var hour)) ? hour : 0;
            var min = (parts.Length >= 5 && int.TryParse(parts[4], out var minute)) ? minute : 0;
            var s = (parts.Length >= 6 && int.TryParse(parts[5], out var sec)) ? sec : 0;
            return (y2 * 10000L + m2 * 100L + d2) * 1000000L + (h * 10000L + min * 100L + s);
        }

        if (DateTime.TryParse(cleaned, out var dt))
        {
            return (dt.Year * 10000L + dt.Month * 100L + dt.Day) * 1000000L +
                   (dt.Hour * 10000L + dt.Minute * 100L + dt.Second);
        }

        return 0;
    }

    /// <summary>
    /// Mã số phục vụ sắp xếp Kỳ dữ liệu từ nhỏ đến lớn (theo thứ tự thời gian)
    /// </summary>
    public long PeriodSortKey => CalculatePeriodSortKey(TimeName, StartDate, EndDate);

    public static long CalculatePeriodSortKey(string? timeName, string? startDate, string? endDate)
    {
        var raw = (timeName ?? "").Trim().ToLower();
        int year = 0;

        // 1. Tách Năm (Year)
        var yearMatch = System.Text.RegularExpressions.Regex.Match(raw, @"\b(19\d\d|20\d\d)\b");
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var y))
        {
            year = y;
        }
        else if (!string.IsNullOrWhiteSpace(endDate))
        {
            var endYearMatch = System.Text.RegularExpressions.Regex.Match(endDate, @"\b(19\d\d|20\d\d)\b");
            if (endYearMatch.Success && int.TryParse(endYearMatch.Value, out var ey))
            {
                year = ey;
            }
        }
        else if (!string.IsNullOrWhiteSpace(startDate))
        {
            var startYearMatch = System.Text.RegularExpressions.Regex.Match(startDate, @"\b(19\d\d|20\d\d)\b");
            if (startYearMatch.Success && int.TryParse(startYearMatch.Value, out var sy))
            {
                year = sy;
            }
        }

        if (year == 0) year = 2000;

        long subKey = 0;

        // 2. Tháng (Tháng 1 .. 12)
        var monthMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(?:tháng|thang|t)\s*0?(\d+)");
        if (monthMatch.Success && int.TryParse(monthMatch.Groups[1].Value, out var m))
        {
            subKey = 1000 + m * 100;
        }
        // 3. Quý (Quý 1 .. 4)
        else if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"(?:quý|quy|q)\s*([1-4])"))
        {
            var qMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(?:quý|quy|q)\s*([1-4])");
            if (int.TryParse(qMatch.Groups[1].Value, out var q))
            {
                subKey = 3000 + q * 250;
            }
        }
        // 4. 6 Tháng (6 tháng đầu năm / cuối năm)
        else if (raw.Contains("6 tháng") || raw.Contains("6thang") || raw.Contains("bán niên"))
        {
            if (raw.Contains("cuối") || raw.Contains("cuoi"))
                subKey = 5200;
            else
                subKey = 5100;
        }
        // 5. 9 Tháng (9 tháng)
        else if (raw.Contains("9 tháng") || raw.Contains("9thang"))
        {
            subKey = 6000;
        }
        // 6. Tuần (Tuần 1 .. 53)
        else if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"(?:tuần|tuan)\s*0?(\d+)"))
        {
            var wMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(?:tuần|tuan)\s*0?(\d+)");
            if (int.TryParse(wMatch.Groups[1].Value, out var w))
            {
                subKey = 500 + w * 10;
            }
        }
        // 7. Ngày (Ngày 1 .. 31)
        else if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"(?:ngày|ngay)\s*0?(\d+)"))
        {
            var dMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(?:ngày|ngay)\s*0?(\d+)");
            if (int.TryParse(dMatch.Groups[1].Value, out var d))
            {
                subKey = 100 + d;
            }
        }
        // 8. Năm (Năm 2026...)
        else if (raw.Contains("năm") || raw.Contains("nam"))
        {
            subKey = 8000;
        }
        else
        {
            subKey = 9000;
        }

        return (long)year * 100000 + subKey;
    }

    public bool IsInputCategory => Category == ReportCategoryType.Input;
    public bool IsSubmitCategory => Category == ReportCategoryType.Submit;
    public bool IsTrackingCategory => Category == ReportCategoryType.Tracking;
    public bool IsApprovalCategory => Category == ReportCategoryType.Approval;
    public bool IsAggregateCategory => Category == ReportCategoryType.Aggregate;

    // =========================================================================
    // ICON VISIBILITY FLAGS (Phân quyền độc lập 100% cho từng mục)
    // =========================================================================

    // --- MỤC 1 & 5: NHẬP BÁO CÁO & TỔNG HỢP BÁO CÁO ---
    /// <summary>
    /// Mục 1 & 5: ✏️ Cây bút (ở Nhập báo cáo số liệu và Tổng hợp báo cáo để xem/sửa/tổng hợp dữ liệu)
    /// </summary>
    public bool CanEdit => IsInputCategory || IsAggregateCategory;

    /// <summary>
    /// Mục 5: ⚡ Cây bút xem & tổng hợp báo cáo
    /// </summary>
    public bool CanAggregateEdit => IsAggregateCategory;

    /// <summary>
    /// Mục 1: 💬 Message xem lý do từ chối (chỉ xuất hiện ở báo cáo bị từ chối trong mục 1)
    /// </summary>
    public bool HasRejectionReason => IsInputCategory && (StateId is 8 or 5 or 9 || Status is "8" or "5" or "9" || !string.IsNullOrWhiteSpace(Note) || DisplayStatusName.Contains("từ chối") || DisplayStatusName.Contains("trả lại"));

    // --- MỤC 2: GỬI BÁO CÁO ---
    /// <summary>
    /// Mục 2: 👁️ Con mắt xem chi tiết báo cáo
    /// </summary>
    public bool CanSubmitView => IsSubmitCategory;

    /// <summary>
    /// Mục 2: ✈️ Gửi báo cáo lên cấp trên / Tỉnh
    /// </summary>
    public bool CanSendReport => IsSubmitCategory;

    /// <summary>
    /// Mục 2: 🚫 Từ chối báo cáo
    /// </summary>
    public bool CanSubmitReject => IsSubmitCategory;

    /// <summary>
    /// Mục 2: 💬 Xem ý kiến trình lãnh đạo đã nhập ở mục 1
    /// </summary>
    public bool HasSubmitOpinion => IsSubmitCategory && (!string.IsNullOrWhiteSpace(SubmitOpinion) || !string.IsNullOrWhiteSpace(Note));

    // --- MỤC 3: THEO DÕI TRẠNG THÁI BÁO CÁO ---
    /// <summary>
    /// Mục 3: 👁️ Con mắt xem chi tiết báo cáo
    /// </summary>
    public bool CanTrackingView => IsTrackingCategory;

    /// <summary>
    /// Mục 3: 🔄 Yêu cầu đính chính (CHỈ dành cho báo cáo Đã gửi (3) hoặc Đã duyệt (4), và chưa gửi yêu cầu)
    /// </summary>
    public bool CanRequestCorrection => IsTrackingCategory &&
        (StateId == 3 || StateId == 4 || Status is "3" or "3.0" or "4" or "4.0" || DisplayStatusName.ToLower().Contains("đã gửi") || DisplayStatusName.ToLower().Contains("đã duyệt") || DisplayStatusName.ToLower().Contains("phê duyệt")) &&
        CorrectionReq != 1;

    // --- MỤC 4: DUYỆT BÁO CÁO (CHỈ 2 LOẠI: ĐÃ GỬI & ĐÃ DUYỆT) ---
    /// <summary>
    /// Mục 4: 👁️ Con mắt xem báo cáo trong màn hình Duyệt báo cáo (Dành cho cả 2 loại: Đã gửi và Đã duyệt)
    /// </summary>
    public bool CanApprovalView => IsApprovalCategory;

    /// <summary>
    /// Mục 4: ✅ Icon Duyệt báo cáo (CHỈ dành cho Báo cáo đã được gửi - State 3)
    /// </summary>
    public bool CanApprovalApprove => IsApprovalCategory && (StateId == 3 || Status is "3" or "3.0" || DisplayStatusName.Contains("đã gửi"));

    /// <summary>
    /// Mục 4: 📝 Icon Duyệt yêu cầu đính chính (CHỈ dành cho Báo cáo đã được duyệt - State 4 KHI CÓ YÊU CẦU ĐÍNH CHÍNH)
    /// </summary>
    public bool CanApprovalApproveCorrection => IsApprovalCategory && (StateId == 4 || Status is "4" or "4.0" || DisplayStatusName.Contains("đã duyệt")) && CorrectionReq > 0;

    /// <summary>
    /// Mục 4: 🚫 Icon Từ chối báo cáo (Dành cho cả 2 loại báo cáo: Đã gửi hoặc Đã duyệt)
    /// </summary>
    public bool CanApprovalReject => IsApprovalCategory;

    // Compatibility aliases
    public bool CanView => CanSubmitView || CanTrackingView || CanApprovalView;
    public bool CanRejectReport => CanSubmitReject;
    public bool CanApproveReport => CanApprovalApprove;
    public bool CanApproveCorrection => CanApprovalApproveCorrection;
    public bool HasCorrectionRequest => CorrectionReq > 0;

    /// <summary>
    /// Thông báo thay đổi đồng loạt trạng thái để UI cập nhật ngay lập tức các nút và nhãn trạng thái
    /// </summary>
    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(IsInputCategory));
        OnPropertyChanged(nameof(IsSubmitCategory));
        OnPropertyChanged(nameof(IsTrackingCategory));
        OnPropertyChanged(nameof(IsApprovalCategory));
        OnPropertyChanged(nameof(IsAggregateCategory));
        OnPropertyChanged(nameof(StateId));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusName));
        OnPropertyChanged(nameof(CorrectionReq));
        OnPropertyChanged(nameof(DisplayStatusName));
        OnPropertyChanged(nameof(DisplayDateColumn));
        OnPropertyChanged(nameof(DateSortKey));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeForeground));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSubmitView));
        OnPropertyChanged(nameof(CanSendReport));
        OnPropertyChanged(nameof(CanSubmitReject));
        OnPropertyChanged(nameof(HasSubmitOpinion));
        OnPropertyChanged(nameof(CanApprovalApprove));
        OnPropertyChanged(nameof(CanApprovalApproveCorrection));
        OnPropertyChanged(nameof(CanApprovalReject));
        OnPropertyChanged(nameof(CanRequestCorrection));
        OnPropertyChanged(nameof(HasCorrectionRequest));
        OnPropertyChanged(nameof(HasRejectionReason));
    }

    private string ResolveStatusName()
    {
        if (!string.IsNullOrWhiteSpace(StatusName) && !StatusName.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            var clean = StatusName.Trim();
            if (clean.Length > 0)
            {
                if (clean.Contains("cấp đơn vị giao") || clean.Contains("đã duyệt cấp đơn vị"))
                    return "Báo cáo đã được duyệt";
                return clean;
            }
        }

        var raw = Status?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("null", StringComparison.OrdinalIgnoreCase))
            return "Đang nhập liệu";

        var sLower = raw.ToLower();

        // 1. Nhóm Đã giao (1)
        if (raw is "1" or "1.0" || sLower.Contains("đã giao") || sLower.Contains("da giao"))
            return "Đã giao";

        // 2. Nhóm Đang nhập liệu / tổng hợp (7)
        if (raw is "7" or "7.0" || sLower.Contains("đang nhập") || sLower.Contains("tổng hợp") || sLower.Contains("dang nhap"))
            return "Đang nhập liệu, tổng hợp";

        // 3. Nhóm Đã trình lãnh đạo (2)
        if (raw is "2" or "2.0" || sLower.Contains("đã trình") || sLower.Contains("trình lãnh đạo") || sLower.Contains("da trinh"))
            return "Đã trình lãnh đạo";

        // 4. Nhóm Báo cáo đã được gửi (3)
        if (raw is "3" or "3.0" || sLower.Contains("đã gửi") || sLower.Contains("da gui"))
            return "Báo cáo đã được gửi";

        // 5. Nhóm Báo cáo đã được duyệt cấp đơn vị giao (4) -> Rút gọn thành "Báo cáo đã được duyệt"
        if (raw is "4" or "4.0" || sLower.Contains("cấp đơn vị giao") || sLower.Contains("đã duyệt") || sLower.Contains("đã phê duyệt") || sLower.Contains("phe duyet"))
            return "Báo cáo đã được duyệt";

        // 6. Nhóm Báo cáo bị từ chối cấp đơn vị (8, 5, 9)
        if (raw is "8" or "8.0" || sLower.Contains("từ chối cấp đơn vị"))
            return "Báo cáo bị từ chối cấp đơn vị";

        if (raw is "5" or "5.0" || raw is "9" or "9.0" || sLower.Contains("nhập lại") || sLower.Contains("từ chối") || sLower.Contains("trả lại") || sLower.Contains("tu choi") || sLower.Contains("tra lai"))
            return "Báo cáo bị từ chối cấp đơn vị";

        // 7. Nhóm Báo cáo cần đính chính (6)
        if (raw is "6" or "6.0" || sLower.Contains("đính chính") || sLower.Contains("dinh chinh"))
            return "Báo cáo cần đính chính";

        return raw;
    }

    public string StatusBadgeBackground
    {
        get
        {
            var name = DisplayStatusName.ToLowerInvariant();

            // 1. Báo cáo đã được duyệt (Màu xanh lá)
            if (StateId == 4 || Status is "4" or "4.0" || name.Contains("đã duyệt") || name.Contains("phê duyệt"))
                return "#DCFCE7"; // Xanh lá nhạt

            // 2. Bị từ chối / cần đính chính (Màu đỏ)
            if (StateId is 8 or 5 or 9 or 6 || Status is "8" or "8.0" or "5" or "5.0" or "9" or "9.0" or "6" or "6.0" ||
                name.Contains("từ chối") || name.Contains("trả lại") || name.Contains("đính chính") || name.Contains("nhập lại"))
                return "#FEE2E2"; // Đỏ nhạt

            // 3. Báo cáo đã được gửi (Màu vàng)
            if (StateId == 3 || Status is "3" or "3.0" || name.Contains("đã gửi"))
                return "#FEF3C7"; // Vàng nhạt

            // 4. Còn lại: Đã giao, Đã trình lãnh đạo, Đang nhập liệu, tổng hợp (Màu xanh dương)
            return "#E0F2FE"; // Xanh dương nhạt
        }
    }

    public string StatusBadgeForeground
    {
        get
        {
            var name = DisplayStatusName.ToLowerInvariant();

            // 1. Báo cáo đã được duyệt (Màu xanh lá đậm)
            if (StateId == 4 || Status is "4" or "4.0" || name.Contains("đã duyệt") || name.Contains("phê duyệt"))
                return "#15803D"; // Xanh lá đậm

            // 2. Bị từ chối / cần đính chính (Màu đỏ đậm)
            if (StateId is 8 or 5 or 9 or 6 || Status is "8" or "8.0" or "5" or "5.0" or "9" or "9.0" or "6" or "6.0" ||
                name.Contains("từ chối") || name.Contains("trả lại") || name.Contains("đính chính") || name.Contains("nhập lại"))
                return "#DC2626"; // Đỏ đậm

            // 3. Báo cáo đã được gửi (Màu vàng đậm)
            if (StateId == 3 || Status is "3" or "3.0" || name.Contains("đã gửi"))
                return "#B45309"; // Vàng đậm

            // 4. Còn lại: Đã giao, Đã trình lãnh đạo, Đang nhập liệu, tổng hợp (Màu xanh dương đậm)
            return "#0369A1"; // Xanh dương đậm
        }
    }
}
