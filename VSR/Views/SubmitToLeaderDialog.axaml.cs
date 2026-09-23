using Avalonia.Controls;
using Avalonia.Interactivity;
using VSR.Models;

namespace VSR.Views;

public partial class SubmitToLeaderDialog : Window
{
    public bool IsConfirmed { get; private set; } = false;
    public string OpinionText { get; private set; } = string.Empty;

    public SubmitToLeaderDialog()
    {
        InitializeComponent();
    }

    public SubmitToLeaderDialog(ReportItem report, int filledCount, int unfilledCount) : this()
    {
        TxtReportInfo.Text = $"{report.ObjName} ({report.TimeName})";
        RunFilled.Text = filledCount.ToString();
        RunUnfilled.Text = unfilledCount.ToString();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        OpinionText = TxtOpinion.Text?.Trim() ?? string.Empty;
        IsConfirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close(false);
    }
}
