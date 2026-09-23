using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VSR.Helpers;
using VSR.Models;

namespace VSR.Views;

public partial class CheckAssignedUnitsDialog : Window
{
    private enum FilterTab
    {
        All,
        Submitted,
        Pending
    }

    private readonly List<AssignedUnitStatus> _allUnits = new();
    private FilterTab _currentTab = FilterTab.All;
    private string _reportName = "";
    private string _timeName = "";

    public bool TriggerAggregation { get; private set; }
    public bool TriggerAggregationOnlyApproved { get; private set; }
    public bool TriggerAggregationAll { get; private set; }

    public CheckAssignedUnitsDialog()
    {
        InitializeComponent();
    }

    public CheckAssignedUnitsDialog(string reportName, string timeName, List<AssignedUnitStatus> assignedUnits) : this()
    {
        _reportName = reportName;
        _timeName = timeName;
        TxtSubtitle.Text = $"Biểu mẫu: {reportName}   |   Kỳ: {timeName}";

        _allUnits.Clear();
        if (assignedUnits != null)
        {
            _allUnits.AddRange(assignedUnits);
        }

        UpdateTabCounts();
        ApplyFilter();
    }

    private void UpdateTabCounts()
    {
        int total = _allUnits.Count;
        int submitted = _allUnits.Count(u => u.IsEligible);
        int pending = total - submitted;

        TxtTabAll.Text = "Tất cả";
        TxtTabSubmitted.Text = "Đơn vị đã gửi";
        TxtTabPending.Text = "Đơn vị chưa gửi";
    }

    private void ApplyFilter()
    {
        var kw = TxtSearch?.Text?.Trim() ?? "";
        var cleanKw = VietnameseNumberHelper.CleanSearchKey(kw);

        var filtered = _allUnits.Where(u =>
        {
            // 1. Tab filter
            if (_currentTab == FilterTab.Submitted && !u.IsEligible)
                return false;
            if (_currentTab == FilterTab.Pending && u.IsEligible)
                return false;

            // 2. Keyword filter
            if (!string.IsNullOrWhiteSpace(cleanKw))
            {
                var uName = VietnameseNumberHelper.CleanSearchKey(u.UnitName);
                var uStat = VietnameseNumberHelper.CleanSearchKey(u.StatusStr);
                var uDate = u.SubmitDate ?? "";
                if (!uName.Contains(cleanKw) && !uStat.Contains(cleanKw) && !uDate.Contains(cleanKw))
                    return false;
            }

            return true;
        }).ToList();

        if (UnitsGrid != null)
        {
            UnitsGrid.ItemsSource = filtered;
        }

        if (TxtPaginationInfo != null)
        {
            int showCount = filtered.Count;
            int total = _allUnits.Count;
            TxtPaginationInfo.Text = $"Hiển thị các dòng từ 1 đến {showCount} trong tổng số {total} dòng";
        }
    }

    private void SetActiveTab(FilterTab tab)
    {
        _currentTab = tab;

        BtnTabAll.Classes.Remove("tabBtnActive");
        BtnTabSubmitted.Classes.Remove("tabBtnActive");
        BtnTabPending.Classes.Remove("tabBtnActive");

        switch (tab)
        {
            case FilterTab.All:
                BtnTabAll.Classes.Add("tabBtnActive");
                break;
            case FilterTab.Submitted:
                BtnTabSubmitted.Classes.Add("tabBtnActive");
                break;
            case FilterTab.Pending:
                BtnTabPending.Classes.Add("tabBtnActive");
                break;
        }

        ApplyFilter();
    }

    private void OnTabAllClick(object? sender, RoutedEventArgs e) => SetActiveTab(FilterTab.All);
    private void OnTabSubmittedClick(object? sender, RoutedEventArgs e) => SetActiveTab(FilterTab.Submitted);
    private void OnTabPendingClick(object? sender, RoutedEventArgs e) => SetActiveTab(FilterTab.Pending);

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Tải xuống danh sách đơn vị gửi báo cáo",
                    DefaultExtension = "csv",
                    SuggestedFileName = $"Danh_sach_don_vi_{_timeName.Replace('/', '_')}.csv",
                    FileTypeChoices = new List<FilePickerFileType>
                    {
                        new("CSV File (*.csv)") { Patterns = new[] { "*.csv" } },
                        new("All Files (*.*)") { Patterns = new[] { "*.*" } }
                    }
                });

                if (file != null)
                {
                    var filePath = file.Path.LocalPath;
                    var sb = new StringBuilder();
                    sb.AppendLine("STT,Đơn vị,Ngày gửi,Trạng thái");
                    foreach (var u in _allUnits)
                    {
                        sb.AppendLine($"\"{u.Stt}\",\"{u.UnitName.Replace("\"", "\"\"")}\",\"{u.SubmitDate}\",\"{u.StatusStr.Replace("\"", "\"\"")}\"");
                    }

                    await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
                }
            }
        }
        catch { }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        TriggerAggregation = false;
        TriggerAggregationOnlyApproved = false;
        TriggerAggregationAll = false;
        Close(false);
    }

    private void OnAggregateApprovedClick(object? sender, RoutedEventArgs e)
    {
        TriggerAggregation = true;
        TriggerAggregationOnlyApproved = true;
        TriggerAggregationAll = false;
        Close(true);
    }

    private void OnAggregateAllClick(object? sender, RoutedEventArgs e)
    {
        TriggerAggregation = true;
        TriggerAggregationOnlyApproved = false;
        TriggerAggregationAll = true;
        Close(true);
    }
}
