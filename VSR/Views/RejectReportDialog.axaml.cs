using Avalonia.Controls;
using Avalonia.Interactivity;
using VSR.Models;

namespace VSR.Views;

public partial class RejectReportDialog : Window
{
    public bool IsConfirmed { get; private set; } = false;
    public string RejectionReason { get; private set; } = string.Empty;

    public RejectReportDialog()
    {
        InitializeComponent();
    }

    public RejectReportDialog(ReportItem report) : this()
    {
        TxtReportInfo.Text = $"{report.ObjName} ({report.TimeName})";
    }

    public RejectReportDialog(int count) : this()
    {
        TxtReportInfo.Text = $"{count} báo cáo đã được chọn để từ chối";
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        RejectionReason = TxtReason.Text?.Trim() ?? string.Empty;
        IsConfirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close(false);
    }
}
