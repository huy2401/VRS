using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using VSR.Models;

namespace VSR.Views;

public partial class RejectionReasonDialog : Window
{
    public RejectionReasonDialog()
    {
        InitializeComponent();
    }

    public RejectionReasonDialog(ReportItem report) : this()
    {
        ApplyReportInfo(report);

        // Mặc định cho báo cáo bị từ chối / cần đính chính
        var isRejected = report.StateId is 8 or 5 or 9 ||
                         report.Status is "8" or "5" or "9" ||
                         report.DisplayStatusName.Contains("từ chối") ||
                         report.DisplayStatusName.Contains("trả lại");

        if (isRejected)
        {
            Title = "Lý do báo cáo bị từ chối";
            HeaderBorder.Background = new SolidColorBrush(Color.Parse("#FEF2F2"));
            HeaderBorder.BorderBrush = new SolidColorBrush(Color.Parse("#FECACA"));
            TxtHeaderIcon.Text = "🚫";
            TxtHeaderTitle.Text = "LÝ DO BÁO CÁO BỊ TỪ CHỐI";
            TxtHeaderTitle.Foreground = new SolidColorBrush(Color.Parse("#DC2626"));
            TxtHeaderSubTitle.Text = "Nội dung phản hồi từ lãnh đạo / cấp trên";
            TxtHeaderSubTitle.Foreground = new SolidColorBrush(Color.Parse("#DC2626"));
            LblReason.Text = "Lý do từ chối:";
        }
        else
        {
            Title = "Thông tin trạng thái báo cáo";
            HeaderBorder.Background = new SolidColorBrush(Color.Parse("#EFF6FF"));
            HeaderBorder.BorderBrush = new SolidColorBrush(Color.Parse("#BFDBFE"));
            TxtHeaderIcon.Text = "💬";
            TxtHeaderTitle.Text = "THÔNG TIN GHI CHÚ BÁO CÁO";
            TxtHeaderTitle.Foreground = new SolidColorBrush(Color.Parse("#1E40AF"));
            TxtHeaderSubTitle.Text = "Nội dung ghi chú từ hệ thống IOC";
            TxtHeaderSubTitle.Foreground = new SolidColorBrush(Color.Parse("#1E40AF"));
            LblReason.Text = "Ghi chú:";
        }

        var reasonText = !string.IsNullOrWhiteSpace(report.Note) ? report.Note : report.SubmitOpinion;
        if (string.IsNullOrWhiteSpace(reasonText))
        {
            reasonText = "Báo cáo bị từ chối và yêu cầu chỉnh sửa lại số liệu từ cấp trên. Vui lòng kiểm tra lại số liệu các chỉ tiêu và thực hiện nộp lại.";
        }

        TxtReasonContent.Text = reasonText;
    }

    public RejectionReasonDialog(ReportItem report, string title, string subTitle, string reasonLabel = "Nội dung:", string content = "", string icon = "💬", string headerBg = "#F5F3FF", string headerBorder = "#DDD6FE", string headerFg = "#6D28D9") : this()
    {
        Title = title;
        ApplyReportInfo(report);

        HeaderBorder.Background = new SolidColorBrush(Color.Parse(headerBg));
        HeaderBorder.BorderBrush = new SolidColorBrush(Color.Parse(headerBorder));
        TxtHeaderIcon.Text = icon;
        TxtHeaderTitle.Text = title.ToUpper();
        TxtHeaderTitle.Foreground = new SolidColorBrush(Color.Parse(headerFg));
        TxtHeaderSubTitle.Text = subTitle;
        TxtHeaderSubTitle.Foreground = new SolidColorBrush(Color.Parse(headerFg));

        LblReason.Text = !string.IsNullOrWhiteSpace(reasonLabel) ? reasonLabel : "Nội dung:";
        TxtReasonContent.Text = !string.IsNullOrWhiteSpace(content) ? content : (!string.IsNullOrWhiteSpace(report.Note) ? report.Note : "Không có thông tin chi tiết.");
    }

    private void ApplyReportInfo(ReportItem report)
    {
        TxtReportName.Text = !string.IsNullOrWhiteSpace(report.ObjName) ? report.ObjName : "Báo cáo";
        var org = !string.IsNullOrWhiteSpace(report.ReportOrgName) ? report.ReportOrgName : (!string.IsNullOrWhiteSpace(report.OrgName) ? report.OrgName : "Không xác định");
        TxtOrgName.Text = org;
        TxtPeriod.Text = !string.IsNullOrWhiteSpace(report.TimeName) ? report.TimeName : "Không xác định";

        TxtStatus.Text = !string.IsNullOrWhiteSpace(report.DisplayStatusName) ? report.DisplayStatusName : "";
        StatusBadge.Background = new SolidColorBrush(Color.Parse(report.StatusBadgeBackground));
        TxtStatus.Foreground = new SolidColorBrush(Color.Parse(report.StatusBadgeForeground));
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
