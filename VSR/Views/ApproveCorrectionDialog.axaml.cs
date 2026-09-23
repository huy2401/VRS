using Avalonia.Controls;
using Avalonia.Interactivity;
using VSR.Models;

namespace VSR.Views;

public enum CorrectionDecision
{
    Cancel,
    Agree,      // Đồng ý: Trả lại cho đơn vị giao để đính chính
    Disagree    // Không đồng ý: Giữ nguyên trạng thái, hủy yêu cầu đính chính
}

public partial class ApproveCorrectionDialog : Window
{
    public CorrectionDecision Decision { get; private set; } = CorrectionDecision.Cancel;
    public string ResponseNote { get; private set; } = string.Empty;

    public ApproveCorrectionDialog()
    {
        InitializeComponent();
    }

    public ApproveCorrectionDialog(ReportItem report) : this()
    {
        TxtReportName.Text = report.ObjName;
        TxtPeriod.Text = report.TimeName;
        TxtOrg.Text = !string.IsNullOrWhiteSpace(report.ReportOrgName) ? report.ReportOrgName : report.OrgName;

        var reason = !string.IsNullOrWhiteSpace(report.Note) ? report.Note : report.SubmitOpinion;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            TxtRequestReason.Text = reason;
        }
        else
        {
            TxtRequestReason.Text = "Đơn vị gửi yêu cầu mở lại quyền chỉnh sửa để đính chính số liệu báo cáo.";
        }
    }

    public ApproveCorrectionDialog(System.Collections.Generic.List<ReportItem> reports) : this()
    {
        Title = $"Xử lý yêu cầu đính chính hàng loạt ({reports.Count} báo cáo)";
        TxtReportName.Text = $"Đã chọn {reports.Count} biểu mẫu báo cáo có yêu cầu đính chính";
        TxtPeriod.Text = string.Join(", ", System.Linq.Enumerable.Take(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(reports, r => r.TimeName)), 3));
        TxtOrg.Text = string.Join(", ", System.Linq.Enumerable.Take(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(reports, r => !string.IsNullOrWhiteSpace(r.ReportOrgName) ? r.ReportOrgName : r.OrgName)), 3));

        TxtRequestReason.Text = $"Đang thực hiện xử lý đồng loạt cho {reports.Count} biểu mẫu báo cáo được các đơn vị yêu cầu mở lại quyền chỉnh sửa số liệu.";
    }

    private void OnAgreeClick(object? sender, RoutedEventArgs e)
    {
        Decision = CorrectionDecision.Agree;
        ResponseNote = TxtResponseNote.Text?.Trim() ?? string.Empty;
        Close(true);
    }

    private void OnDisagreeClick(object? sender, RoutedEventArgs e)
    {
        Decision = CorrectionDecision.Disagree;
        ResponseNote = TxtResponseNote.Text?.Trim() ?? string.Empty;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Decision = CorrectionDecision.Cancel;
        Close(false);
    }
}
