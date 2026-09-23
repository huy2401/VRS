using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VSR.Models;
using VSR.Services;

namespace VSR.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly ReportService _reportService;

    private bool _isLoggedIn;
    private AuthSession? _session;
    private bool _isLoadingReports;
    private string _statusMessage = string.Empty;

    // Navigation state: 0 = Nhập báo cáo số liệu, 1 = Gửi báo cáo
    private int _selectedNavIndex = 0;

    // Unified Search & Filters
    private string _searchKeyword = string.Empty;
    private string? _selectedReportFilter = "Tất cả biểu mẫu báo cáo";
    private string _selectedPeriodFilter = "Tất cả kỳ";
    private string _selectedStatusFilter = "Tất cả trạng thái";
    private string _selectedOrgFilter = "Tất cả đơn vị báo cáo";

    public ObservableCollection<string> OrgFilters { get; } = new();

    private List<ReportItem> _allReports = new();
    private readonly UpdateService _updateService = new();

    public LoginViewModel LoginVM { get; }

    public string AppVersion => UpdateService.AppVersion;
    public string AppVersionDisplay => $"Phiên bản {UpdateService.AppVersion}";

    private bool _hasUpdate;
    public bool HasUpdate
    {
        get => _hasUpdate;
        set => SetProperty(ref _hasUpdate, value);
    }

    private string _updateMessage = string.Empty;
    public string UpdateMessage
    {
        get => _updateMessage;
        set => SetProperty(ref _updateMessage, value);
    }

    private string _updateDownloadUrl = string.Empty;
    public string UpdateDownloadUrl
    {
        get => _updateDownloadUrl;
        set => SetProperty(ref _updateDownloadUrl, value);
    }

    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set => SetProperty(ref _isLoggedIn, value);
    }

    public AuthSession? Session
    {
        get => _session;
        set
        {
            if (SetProperty(ref _session, value))
            {
                OnPropertyChanged(nameof(UserDisplayInfo));
                OnPropertyChanged(nameof(SessionDetails));
            }
        }
    }

    public string UserDisplayInfo => Session != null
        ? $"{Session.Username} (Org ID: {Session.OrgId}, User ID: {Session.UserId})"
        : "Chưa đăng nhập";

    public string SessionDetails => Session != null
        ? $"UUID: {Session.Uuid} | Tenant: {Session.TenantId} | {Session.ApiUrl}"
        : "";

    public bool IsLoadingReports
    {
        get => _isLoadingReports;
        set
        {
            if (SetProperty(ref _isLoadingReports, value))
            {
                OnPropertyChanged(nameof(IsEmptyStateVisible));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public int SelectedNavIndex
    {
        get => _selectedNavIndex;
        set
        {
            if (SetProperty(ref _selectedNavIndex, value))
            {
                OnPropertyChanged(nameof(IsInputTabSelected));
                OnPropertyChanged(nameof(IsSubmitTabSelected));
                OnPropertyChanged(nameof(IsTrackingTabSelected));
                OnPropertyChanged(nameof(IsApprovalTabSelected));
                OnPropertyChanged(nameof(IsAggregateTabSelected));
                OnPropertyChanged(nameof(IsSelectionColumnVisible));
                OnPropertyChanged(nameof(IsSttColumnVisible));
                OnPropertyChanged(nameof(CanShowBatchAggregate));
                OnPropertyChanged(nameof(SelectedInputReportsCount));
                OnPropertyChanged(nameof(HasSelectedInputReports));
                OnPropertyChanged(nameof(SelectedAggregateReportsCount));
                OnPropertyChanged(nameof(HasSelectedAggregateReports));
                OnPropertyChanged(nameof(SelectedSubmitReportsCount));
                OnPropertyChanged(nameof(HasSelectedSubmitReports));
                OnPropertyChanged(nameof(SelectedApprovalReportsCount));
                OnPropertyChanged(nameof(HasSelectedApprovalReports));
                OnPropertyChanged(nameof(CurrentTabTitle));
                OnPropertyChanged(nameof(CurrentTabDescription));
                OnPropertyChanged(nameof(CurrentTabBadgeBackground));
                OnPropertyChanged(nameof(CurrentTabBadgeForeground));

                // Visual properties for Tab 1
                OnPropertyChanged(nameof(InputTabBackground));
                OnPropertyChanged(nameof(InputTabBorderBrush));
                OnPropertyChanged(nameof(InputTabBorderThickness));
                OnPropertyChanged(nameof(InputTabForeground));
                OnPropertyChanged(nameof(InputTabFontWeight));
                OnPropertyChanged(nameof(InputTabSubForeground));
                OnPropertyChanged(nameof(InputTabBadgeBackground));
                OnPropertyChanged(nameof(InputTabBadgeBorder));
                OnPropertyChanged(nameof(InputTabBadgeForeground));

                // Visual properties for Tab 2
                OnPropertyChanged(nameof(SubmitTabBackground));
                OnPropertyChanged(nameof(SubmitTabBorderBrush));
                OnPropertyChanged(nameof(SubmitTabBorderThickness));
                OnPropertyChanged(nameof(SubmitTabForeground));
                OnPropertyChanged(nameof(SubmitTabFontWeight));
                OnPropertyChanged(nameof(SubmitTabSubForeground));
                OnPropertyChanged(nameof(SubmitTabBadgeBackground));
                OnPropertyChanged(nameof(SubmitTabBadgeBorder));
                OnPropertyChanged(nameof(SubmitTabBadgeForeground));

                // Visual properties for Tab 3
                OnPropertyChanged(nameof(TrackingTabBackground));
                OnPropertyChanged(nameof(TrackingTabBorderBrush));
                OnPropertyChanged(nameof(TrackingTabBorderThickness));
                OnPropertyChanged(nameof(TrackingTabForeground));
                OnPropertyChanged(nameof(TrackingTabFontWeight));
                OnPropertyChanged(nameof(TrackingTabSubForeground));
                OnPropertyChanged(nameof(TrackingTabBadgeBackground));
                OnPropertyChanged(nameof(TrackingTabBadgeBorder));
                OnPropertyChanged(nameof(TrackingTabBadgeForeground));

                // Visual properties for Tab 4
                OnPropertyChanged(nameof(ApprovalTabBackground));
                OnPropertyChanged(nameof(ApprovalTabBorderBrush));
                OnPropertyChanged(nameof(ApprovalTabBorderThickness));
                OnPropertyChanged(nameof(ApprovalTabForeground));
                OnPropertyChanged(nameof(ApprovalTabFontWeight));
                OnPropertyChanged(nameof(ApprovalTabSubForeground));
                OnPropertyChanged(nameof(ApprovalTabBadgeBackground));
                OnPropertyChanged(nameof(ApprovalTabBadgeBorder));
                OnPropertyChanged(nameof(ApprovalTabBadgeForeground));

                // Visual properties for Tab 5 (Tổng hợp báo cáo)
                OnPropertyChanged(nameof(AggregateTabBackground));
                OnPropertyChanged(nameof(AggregateTabBorderBrush));
                OnPropertyChanged(nameof(AggregateTabBorderThickness));
                OnPropertyChanged(nameof(AggregateTabForeground));
                OnPropertyChanged(nameof(AggregateTabFontWeight));
                OnPropertyChanged(nameof(AggregateTabSubForeground));
                OnPropertyChanged(nameof(AggregateTabBadgeBackground));
                OnPropertyChanged(nameof(AggregateTabBadgeBorder));
                OnPropertyChanged(nameof(AggregateTabBadgeForeground));

                UpdateStatusFilterOptions();
                UpdateAvailableOrgNames();
                ApplyFilter();
                OnPropertyChanged(nameof(CanShowBatchApproveButton));
                OnPropertyChanged(nameof(CanShowBatchRejectButton));
                OnPropertyChanged(nameof(CanShowBatchCorrectionButton));
            }
        }
    }

    public bool IsInputTabSelected => SelectedNavIndex == 0;
    public bool IsSubmitTabSelected => SelectedNavIndex == 1;
    public bool IsTrackingTabSelected => SelectedNavIndex == 2;
    public bool IsApprovalTabSelected => SelectedNavIndex == 3;
    public bool IsAggregateTabSelected => SelectedNavIndex == 4;
    public bool IsSelectionColumnVisible => IsInputTabSelected || IsSubmitTabSelected || IsApprovalTabSelected || IsAggregateTabSelected;
    public bool IsSttColumnVisible => IsTrackingTabSelected;
    public bool CanShowBatchAggregate => IsAggregateTabSelected;
    public bool CanShowBatchApproveButton => IsApprovalTabSelected && SelectedStatusFilter != "Báo cáo đã được duyệt";
    public bool CanShowBatchRejectButton => IsApprovalTabSelected && SelectedStatusFilter == "Báo cáo đã được duyệt";
    public bool CanShowBatchCorrectionButton => IsApprovalTabSelected && SelectedStatusFilter != "Báo cáo đã được duyệt";

    private int _threadCount = 3;
    public int ThreadCount
    {
        get => _threadCount;
        set => SetProperty(ref _threadCount, Math.Max(1, Math.Min(20, value)));
    }

    private bool _isBatchAggregating;
    public bool IsBatchAggregating
    {
        get => _isBatchAggregating;
        set => SetProperty(ref _isBatchAggregating, value);
    }

    private double _batchProgress;
    public double BatchProgress
    {
        get => _batchProgress;
        set => SetProperty(ref _batchProgress, value);
    }

    private string _batchProgressText = string.Empty;
    public string BatchProgressText
    {
        get => _batchProgressText;
        set => SetProperty(ref _batchProgressText, value);
    }

    // Visual state for Tab 1 (Nhập báo cáo số liệu)
    public string InputTabBackground => IsInputTabSelected ? "#FFF7ED" : "Transparent";
    public string InputTabBorderBrush => IsInputTabSelected ? "#EA580C" : "#E2E8F0";
    public string InputTabBorderThickness => IsInputTabSelected ? "1.5" : "1";
    public string InputTabForeground => IsInputTabSelected ? "#C2410C" : "#334155";
    public string InputTabFontWeight => IsInputTabSelected ? "Bold" : "Normal";
    public string InputTabSubForeground => IsInputTabSelected ? "#EA580C" : "#94A3B8";
    public string InputTabBadgeBackground => IsInputTabSelected ? "#FFEDD5" : "#F8FAFC";
    public string InputTabBadgeBorder => IsInputTabSelected ? "#EA580C" : "#E2E8F0";
    public string InputTabBadgeForeground => IsInputTabSelected ? "#9A3412" : "#64748B";

    // Visual state for Tab 2 (Gửi báo cáo)
    public string SubmitTabBackground => IsSubmitTabSelected ? "#EFF6FF" : "Transparent";
    public string SubmitTabBorderBrush => IsSubmitTabSelected ? "#2563EB" : "#E2E8F0";
    public string SubmitTabBorderThickness => IsSubmitTabSelected ? "1.5" : "1";
    public string SubmitTabForeground => IsSubmitTabSelected ? "#1E40AF" : "#334155";
    public string SubmitTabFontWeight => IsSubmitTabSelected ? "Bold" : "Normal";
    public string SubmitTabSubForeground => IsSubmitTabSelected ? "#2563EB" : "#94A3B8";
    public string SubmitTabBadgeBackground => IsSubmitTabSelected ? "#DBEAFE" : "#F8FAFC";
    public string SubmitTabBadgeBorder => IsSubmitTabSelected ? "#2563EB" : "#E2E8F0";
    public string SubmitTabBadgeForeground => IsSubmitTabSelected ? "#1E3A8A" : "#64748B";

    // Visual state for Tab 3 (Theo dõi trạng thái báo cáo)
    public string TrackingTabBackground => IsTrackingTabSelected ? "#F0FDF4" : "Transparent";
    public string TrackingTabBorderBrush => IsTrackingTabSelected ? "#16A34A" : "#E2E8F0";
    public string TrackingTabBorderThickness => IsTrackingTabSelected ? "1.5" : "1";
    public string TrackingTabForeground => IsTrackingTabSelected ? "#15803D" : "#334155";
    public string TrackingTabFontWeight => IsTrackingTabSelected ? "Bold" : "Normal";
    public string TrackingTabSubForeground => IsTrackingTabSelected ? "#16A34A" : "#94A3B8";
    public string TrackingTabBadgeBackground => IsTrackingTabSelected ? "#DCFCE7" : "#F8FAFC";
    public string TrackingTabBadgeBorder => IsTrackingTabSelected ? "#16A34A" : "#E2E8F0";
    public string TrackingTabBadgeForeground => IsTrackingTabSelected ? "#14532D" : "#64748B";

    // Visual state for Tab 4 (Duyệt báo cáo)
    public string ApprovalTabBackground => IsApprovalTabSelected ? "#F5F3FF" : "Transparent";
    public string ApprovalTabBorderBrush => IsApprovalTabSelected ? "#7C3AED" : "#E2E8F0";
    public string ApprovalTabBorderThickness => IsApprovalTabSelected ? "1.5" : "1";
    public string ApprovalTabForeground => IsApprovalTabSelected ? "#6D28D9" : "#334155";
    public string ApprovalTabFontWeight => IsApprovalTabSelected ? "Bold" : "Normal";
    public string ApprovalTabSubForeground => IsApprovalTabSelected ? "#7C3AED" : "#94A3B8";
    public string ApprovalTabBadgeBackground => IsApprovalTabSelected ? "#EDE9FE" : "#F8FAFC";
    public string ApprovalTabBadgeBorder => IsApprovalTabSelected ? "#7C3AED" : "#E2E8F0";
    public string ApprovalTabBadgeForeground => IsApprovalTabSelected ? "#5B21B6" : "#64748B";

    // Visual state for Tab 5 (Tổng hợp báo cáo)
    public string AggregateTabBackground => IsAggregateTabSelected ? "#FEF3C7" : "Transparent";
    public string AggregateTabBorderBrush => IsAggregateTabSelected ? "#D97706" : "#E2E8F0";
    public string AggregateTabBorderThickness => IsAggregateTabSelected ? "1.5" : "1";
    public string AggregateTabForeground => IsAggregateTabSelected ? "#B45309" : "#334155";
    public string AggregateTabFontWeight => IsAggregateTabSelected ? "Bold" : "Normal";
    public string AggregateTabSubForeground => IsAggregateTabSelected ? "#D97706" : "#94A3B8";
    public string AggregateTabBadgeBackground => IsAggregateTabSelected ? "#FDE68A" : "#F8FAFC";
    public string AggregateTabBadgeBorder => IsAggregateTabSelected ? "#D97706" : "#E2E8F0";
    public string AggregateTabBadgeForeground => IsAggregateTabSelected ? "#92400E" : "#64748B";

    public string CurrentTabTitle => SelectedNavIndex switch
    {
        0 => "Danh sách nhập báo cáo số liệu",
        1 => "Danh sách gửi báo cáo",
        2 => "Danh sách trạng thái báo cáo đã gửi",
        3 => "Danh sách duyệt báo cáo",
        _ => "Danh sách tổng hợp báo cáo"
    };

    public string CurrentTabDescription => SelectedNavIndex switch
    {
        0 => "Bao gồm các báo cáo đang nhập liệu và bị từ chối / yêu cầu nhập lại",
        1 => "Bao gồm các báo cáo đã trình lãnh đạo và lãnh đạo đã phê duyệt",
        2 => "Bao gồm các báo cáo đã gửi lên cấp trên và báo cáo đã được duyệt",
        3 => "Bao gồm các báo cáo chờ duyệt (Đã gửi) và báo cáo đã được duyệt cấp đơn vị",
        _ => "Bao gồm các biểu mẫu tổng hợp số liệu trực tiếp từ các đơn vị sở ban ngành theo API IOC"
    };

    public string CurrentTabBadgeBackground => SelectedNavIndex switch
    {
        0 => "#FFF7ED",
        1 => "#EFF6FF",
        2 => "#F0FDF4",
        3 => "#F5F3FF",
        _ => "#FEF3C7"
    };

    public string CurrentTabBadgeForeground => SelectedNavIndex switch
    {
        0 => "#C2410C",
        1 => "#1E40AF",
        2 => "#15803D",
        3 => "#6D28D9",
        _ => "#B45309"
    };

    /// <summary>
    /// Từ khóa tìm kiếm trong dropdown (gõ vào ô search thứ 2 để lọc danh sách)
    /// </summary>
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                UpdateFilteredReportNames();
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// Biểu mẫu báo cáo được chọn (ô trên cùng)
    /// </summary>
    public string? SelectedReportFilter
    {
        get => _selectedReportFilter;
        set
        {
            if (SetProperty(ref _selectedReportFilter, value))
            {
                OnPropertyChanged(nameof(SelectedReportDisplay));
                ApplyFilter();
            }
        }
    }

    public string SelectedReportDisplay => !string.IsNullOrWhiteSpace(SelectedReportFilter)
        ? SelectedReportFilter
        : "Tất cả biểu mẫu báo cáo";

    public string FilterText
    {
        get => _searchKeyword;
        set => SearchKeyword = value;
    }

    public string SelectedPeriodFilter
    {
        get => _selectedPeriodFilter;
        set
        {
            if (SetProperty(ref _selectedPeriodFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedOrgFilter
    {
        get => _selectedOrgFilter;
        set
        {
            if (SetProperty(ref _selectedOrgFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    private bool _hasLoadedApprovalApprovedReports;

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                OnPropertyChanged(nameof(CanShowBatchApproveButton));
                OnPropertyChanged(nameof(CanShowBatchRejectButton));
                OnPropertyChanged(nameof(CanShowBatchCorrectionButton));

                if (SelectedNavIndex == 3 && value == "Báo cáo đã được duyệt" && !_hasLoadedApprovalApprovedReports)
                {
                    _ = LoadApprovedApprovalReportsAsync();
                }
                else
                {
                    ApplyFilter();
                }
            }
        }
    }

    public async Task LoadApprovedApprovalReportsAsync()
    {
        if (_hasLoadedApprovalApprovedReports || Session == null || !Session.IsAuthenticated) return;

        IsLoadingReports = true;
        StatusMessage = "Đang tải danh sách báo cáo đã được duyệt...";

        var (reports, error) = await _reportService.FetchApprovalReportsAsync(Session, includeApproved: true);

        IsLoadingReports = false;

        if (string.IsNullOrEmpty(error))
        {
            _hasLoadedApprovalApprovedReports = true;

            // Thêm các báo cáo đã duyệt vào _allReports
            var existingKeys = new HashSet<string>(_allReports.Where(r => r.IsApprovalCategory).Select(r => $"{r.InputGrantId}_{r.ObjId}_{r.TimeId}_{r.TargetOrgId}"));

            foreach (var r in reports.Where(r => r.StateId == 4))
            {
                var key = $"{r.InputGrantId}_{r.ObjId}_{r.TimeId}_{r.TargetOrgId}";
                if (!existingKeys.Contains(key))
                {
                    _allReports.Add(r);
                    existingKeys.Add(key);
                }
            }

            StatusMessage = $"Đã tải danh sách báo cáo đã duyệt ({reports.Count(r => r.StateId == 4)} báo cáo).";
            UpdateAvailableReportNames();
            UpdateAvailableOrgNames();
            ApplyFilter();
        }
        else
        {
            StatusMessage = $"Lỗi tải báo cáo đã duyệt: {error}";
            ApplyFilter();
        }
    }

    public ObservableCollection<ReportItem> FilteredReports { get; } = new();
    public ObservableCollection<string> AvailableReportNames { get; } = new();
    public ObservableCollection<string> FilteredReportNames { get; } = new();
    public ObservableCollection<string> PeriodFilters { get; } = new();
    public ObservableCollection<string> StatusFilters { get; } = new();

    public int TotalCount => _allReports.Count;
    public int FilteredCount => FilteredReports.Count;
    public bool IsEmptyStateVisible => FilteredCount == 0 && !IsLoadingReports;
    public int InputReportsCount => _allReports.Count(r => r.IsInputCategory);
    public int SubmitReportsCount => _allReports.Count(r => r.IsSubmitCategory);
    public int TrackingReportsCount => _allReports.Count(r => r.IsTrackingCategory);
    
    /// <summary>
    /// Số lượng huy hiệu Duyệt báo cáo: Chỉ tính các Báo cáo đã gửi & Yêu cầu đính chính (Báo cáo cần xử lý)
    /// </summary>
    public int ApprovalReportsCount => _allReports.Count(r => r.IsApprovalCategory && (r.StateId == 3 || r.Status == "3" || r.CorrectionReq > 0 || r.DisplayStatusName.Contains("đã gửi")));
    
    public int AggregateReportsCount => _allReports.Count(r => r.IsAggregateCategory);
    public int CurrentTabTotalCount => SelectedNavIndex switch
    {
        0 => InputReportsCount,
        1 => SubmitReportsCount,
        2 => TrackingReportsCount,
        3 => ApprovalReportsCount,
        _ => AggregateReportsCount
    };

    public int SelectedInputReportsCount => FilteredReports.Count(r => r.IsSelected && r.IsInputCategory);
    public bool HasSelectedInputReports => SelectedInputReportsCount > 0;

    public int SelectedAggregateReportsCount => FilteredReports.Count(r => r.IsSelected && r.IsAggregateCategory);
    public bool HasSelectedAggregateReports => SelectedAggregateReportsCount > 0;

    public int SelectedSubmitReportsCount => FilteredReports.Count(r => r.IsSelected && r.IsSubmitCategory && (r.StateId == 2 || r.Status == "2" || r.Status == "2.0" || r.DisplayStatusName.Contains("trình")));
    public bool HasSelectedSubmitReports => SelectedSubmitReportsCount > 0;

    public int SelectedApprovalReportsCount => FilteredReports.Count(r => r.IsSelected && r.CanApprovalApprove);
    public bool HasSelectedApprovalReports => SelectedApprovalReportsCount > 0;

    public int SelectedCorrectionReportsCount => FilteredReports.Count(r => r.IsSelected && r.IsApprovalCategory && r.CorrectionReq > 0);
    public bool HasSelectedCorrectionReports => SelectedCorrectionReportsCount > 0;

    private bool _isUpdatingAllSelected = false;
    public bool? IsAllSelected
    {
        get
        {
            if (FilteredReports.Count == 0) return false;
            var selectedCount = FilteredReports.Count(r => r.IsSelected);
            if (selectedCount == 0) return false;
            if (selectedCount == FilteredReports.Count) return true;
            return null;
        }
        set
        {
            if (_isUpdatingAllSelected) return;
            _isUpdatingAllSelected = true;
            bool isChecked = value == true;
            foreach (var r in FilteredReports)
            {
                r.IsSelected = isChecked;
            }
            _isUpdatingAllSelected = false;
            NotifySelectionChanged();
        }
    }

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedInputReportsCount));
        OnPropertyChanged(nameof(HasSelectedInputReports));
        OnPropertyChanged(nameof(SelectedAggregateReportsCount));
        OnPropertyChanged(nameof(HasSelectedAggregateReports));
        OnPropertyChanged(nameof(SelectedSubmitReportsCount));
        OnPropertyChanged(nameof(HasSelectedSubmitReports));
        OnPropertyChanged(nameof(SelectedApprovalReportsCount));
        OnPropertyChanged(nameof(HasSelectedApprovalReports));
        OnPropertyChanged(nameof(SelectedCorrectionReportsCount));
        OnPropertyChanged(nameof(HasSelectedCorrectionReports));
        OnPropertyChanged(nameof(CanShowBatchCorrectionButton));
        OnPropertyChanged(nameof(IsAllSelected));
    }

    private void OnReportItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportItem.IsSelected))
        {
            NotifySelectionChanged();
        }
    }

    public int SendSelectedReports()
    {
        var selected = FilteredReports.Where(r => r.IsSelected && r.IsSubmitCategory).ToList();
        if (selected.Count == 0) return 0;

        foreach (var r in selected)
        {
            r.StateId = 3;
            r.Status = "3";
            r.StatusName = "Báo cáo đã được gửi";
            r.IsSelected = false;
        }

        StatusMessage = $"Đã duyệt và gửi thành công {selected.Count} báo cáo lên cấp trên!";
        ApplyFilter();
        return selected.Count;
    }

    public int ApproveSelectedReports()
    {
        var selected = FilteredReports.Where(r => r.IsSelected && r.IsApprovalCategory).ToList();
        if (selected.Count == 0) return 0;

        foreach (var r in selected)
        {
            r.StateId = 4;
            r.Status = "4";
            r.StatusName = "Báo cáo đã được duyệt";
            r.IsSelected = false;
        }

        StatusMessage = $"Đã phê duyệt thành công {selected.Count} báo cáo!";
        ApplyFilter();
        return selected.Count;
    }

    /// <summary>
    /// Xóa một báo cáo khỏi danh sách dữ liệu (ví dụ: báo cáo đã duyệt đính chính được trả lại cho đơn vị)
    /// </summary>
    public void RemoveReport(ReportItem report)
    {
        _allReports.Remove(report);
    }

    /// <summary>
    /// Gỡ một dòng khỏi danh sách đang hiển thị mà không gọi lại IOC hoặc dựng lại toàn bộ DataGrid.
    /// Dùng khi trạng thái vừa đổi tại màn hình chi tiết để giữ nguyên vị trí cuộn.
    /// </summary>
    public void RemoveReportFromCurrentList(ReportItem report)
    {
        FilteredReports.Remove(report);
        NotifyCountersChanged();
        OnPropertyChanged(nameof(FilteredCount));
    }

    /// <summary>
    /// Tổng hợp hàng loạt các báo cáo được chọn (hoặc tất cả các báo cáo đang lọc trong danh sách Tổng hợp)
    /// </summary>
    public async Task<(int SuccessCount, int FailCount, List<string> ErrorMessages)> BatchAggregateSelectedReportsAsync(bool onlyApproved = false)
    {
        if (Session == null || !Session.IsAuthenticated)
            return (0, 0, new List<string> { "Chưa xác thực phiên đăng nhập" });

        var selected = FilteredReports.Where(r => r.IsSelected && (r.IsAggregateCategory || r.IsInputCategory)).ToList();
        var targetReports = selected.Count > 0
            ? selected
            : FilteredReports.Where(r => r.IsAggregateCategory || r.IsInputCategory).ToList();

        if (targetReports.Count == 0)
            return (0, 0, new List<string> { "Không tìm thấy báo cáo nào phù hợp để tổng hợp" });

        IsBatchAggregating = true;
        BatchProgress = 0;
        var modeName = onlyApproved ? "đã duyệt" : "tất cả";
        BatchProgressText = $"Bắt đầu tổng hợp ({modeName}) 0/{targetReports.Count}...";

        try
        {
            var progress = new Progress<(int Current, int Total, string ReportName, bool Success, string Message)>(p =>
            {
                BatchProgress = (double)p.Current / p.Total * 100.0;
                BatchProgressText = $"Đang tổng hợp {p.Current}/{p.Total}: {p.ReportName} {(p.Success ? "✓" : "❌")}";
            });

            var result = await _reportService.BatchAggregateReportsAsync(Session, targetReports, ThreadCount, progress, onlyApproved: onlyApproved);
            BatchProgressText = $"Hoàn tất ({modeName}): Thành công {result.SuccessCount}/{targetReports.Count}, Thất bại {result.FailCount}";

            // Refresh report list
            await LoadReportsAsync();
            return result;
        }
        finally
        {
            IsBatchAggregating = false;
        }
    }

    public MainViewModel()
    {
        _authService = new AuthService();
        _reportService = new ReportService();

        LoginVM = new LoginViewModel(_authService);
        LoginVM.LoginSuccess += OnLoginSuccess;

        InitializePeriodFilters();
        UpdateStatusFilterOptions();
    }

    private void InitializePeriodFilters()
    {
        PeriodFilters.Clear();
        PeriodFilters.Add("Tất cả kỳ");
        PeriodFilters.Add("Tháng");
        PeriodFilters.Add("Quý");
        PeriodFilters.Add("Tuần");
        PeriodFilters.Add("6 Tháng");
        PeriodFilters.Add("Năm");
        PeriodFilters.Add("Ngày");
        SelectedPeriodFilter = "Tất cả kỳ";
    }

    private void UpdateStatusFilterOptions()
    {
        StatusFilters.Clear();

        if (SelectedNavIndex == 0)
        {
            // Tab 1: Nhập báo cáo số liệu
            StatusFilters.Add("Tất cả trạng thái");
            StatusFilters.Add("Đang nhập liệu, tổng hợp");
            StatusFilters.Add("Báo cáo bị từ chối cấp đơn vị");
            StatusFilters.Add("Báo cáo cần đính chính");
            StatusFilters.Add("Đã giao");
            SelectedStatusFilter = "Tất cả trạng thái";
        }
        else if (SelectedNavIndex == 1)
        {
            // Tab 2: Gửi báo cáo
            StatusFilters.Add("Tất cả trạng thái");
            StatusFilters.Add("Đã trình lãnh đạo");
            SelectedStatusFilter = "Tất cả trạng thái";
        }
        else if (SelectedNavIndex == 2)
        {
            // Tab 3: Trạng thái báo cáo đã gửi (Chỉ có Đã gửi và Đã duyệt)
            StatusFilters.Add("Tất cả trạng thái");
            StatusFilters.Add("Báo cáo đã được gửi");
            StatusFilters.Add("Báo cáo đã được duyệt");
            SelectedStatusFilter = "Tất cả trạng thái";
        }
        else if (SelectedNavIndex == 3)
        {
            // Tab 4: Duyệt báo cáo (Chỉ có 2 phần tử theo yêu cầu, Mặc định là Báo cáo đã gửi & Yêu cầu đính chính)
            StatusFilters.Add("Báo cáo đã gửi & Yêu cầu đính chính");
            StatusFilters.Add("Báo cáo đã được duyệt");
            SelectedStatusFilter = "Báo cáo đã gửi & Yêu cầu đính chính";
        }
        else
        {
            // Tab 5: Tổng hợp báo cáo
            StatusFilters.Add("Tất cả trạng thái");
            StatusFilters.Add("Đang nhập liệu, tổng hợp");
            StatusFilters.Add("Đã giao");
            StatusFilters.Add("Báo cáo bị từ chối");
            SelectedStatusFilter = "Tất cả trạng thái";
        }
    }

    public async Task InitializeAsync()
    {
        await LoginVM.InitializeAsync();
        await CheckForAppUpdatesAsync();
    }

    public async Task CheckForAppUpdatesAsync()
    {
        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync();
            if (updateInfo.HasUpdate)
            {
                HasUpdate = true;
                UpdateMessage = $"Đã có phiên bản mới v{updateInfo.LatestVersion}!";
                UpdateDownloadUrl = updateInfo.DownloadUrl;
                LoginVM.IsLoginBlocked = true;
            }
        }
        catch { }
    }

    public Task<(bool Success, string Message)> DownloadAndInstallUpdateAsync() =>
        _updateService.DownloadAndInstallAsync(UpdateDownloadUrl);

    private async void OnLoginSuccess(AuthSession session)
    {
        Session = session;
        IsLoggedIn = true;
        await LoadReportsAsync();
    }

    public async Task LoadReportsAsync()
    {
        if (Session == null || !Session.IsAuthenticated) return;

        IsLoadingReports = true;
        StatusMessage = "Đang tải danh sách báo cáo từ hệ thống IOC...";

        var (reports, error) = await _reportService.GetAssignedReportsAsync(Session);

        IsLoadingReports = false;

        if (string.IsNullOrEmpty(error))
        {
            _hasLoadedApprovalApprovedReports = false;
            _allReports = reports;
            StatusMessage = $"Đã tải thành công {reports.Count} báo cáo.";

            UpdateAvailableReportNames();
            UpdateAvailableOrgNames();
            ApplyFilter();
        }
        else
        {
            _allReports.Clear();
            FilteredReports.Clear();
            AvailableReportNames.Clear();
            StatusMessage = $"Lỗi: {error}";
            NotifyCountersChanged();
        }
    }

    /// <summary>
    /// Làm mới dữ liệu chỉ riêng cho mục (Tab) hiện tại
    /// Tiết kiệm tài nguyên và phản hồi nhanh chóng
    /// </summary>
    public async Task RefreshCurrentTabAsync()
    {
        if (Session == null || !Session.IsAuthenticated) return;

        IsLoadingReports = true;
        StatusMessage = $"Đang làm mới dữ liệu cho {CurrentTabTitle}...";

        bool includeApproved = SelectedNavIndex == 3 && SelectedStatusFilter == "Báo cáo đã được duyệt";
        var (reports, error) = await _reportService.GetReportsForTabAsync(Session, SelectedNavIndex, includeApproved);

        IsLoadingReports = false;

        if (string.IsNullOrEmpty(error))
        {
            // Xóa các báo cáo cũ thuộc Tab hiện tại khỏi danh sách tổng
            _allReports.RemoveAll(r =>
            {
                return SelectedNavIndex switch
                {
                    0 => r.IsInputCategory,
                    1 => r.IsSubmitCategory,
                    2 => r.IsTrackingCategory,
                    3 => r.IsApprovalCategory,
                    4 => r.IsAggregateCategory,
                    _ => false
                };
            });

            // Nạp danh sách mới vào
            _allReports.AddRange(reports);

            if (SelectedNavIndex == 3 && includeApproved)
            {
                _hasLoadedApprovalApprovedReports = true;
            }

            StatusMessage = $"Đã làm mới thành công {reports.Count} báo cáo cho {CurrentTabTitle}.";

            UpdateAvailableReportNames();
            UpdateAvailableOrgNames();
            ApplyFilter();
            NotifyCountersChanged();
        }
        else
        {
            StatusMessage = $"Lỗi làm mới dữ liệu: {error}";
        }
    }

    private void UpdateAvailableReportNames()
    {
        AvailableReportNames.Clear();
        AvailableReportNames.Add("Tất cả biểu mẫu báo cáo");

        var distinctNames = _allReports
            .Select(r => r.ObjName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(name => name);

        foreach (var name in distinctNames)
        {
            AvailableReportNames.Add(name);
        }

        _selectedReportFilter = "Tất cả biểu mẫu báo cáo";
        OnPropertyChanged(nameof(SelectedReportFilter));
        OnPropertyChanged(nameof(SelectedReportDisplay));
        UpdateFilteredReportNames();
    }

    public void UpdateAvailableOrgNames()
    {
        OrgFilters.Clear();
        OrgFilters.Add("Tất cả đơn vị báo cáo");

        var query = _allReports.AsEnumerable();
        if (SelectedNavIndex == 0) query = query.Where(r => r.IsInputCategory);
        else if (SelectedNavIndex == 1) query = query.Where(r => r.IsSubmitCategory);
        else if (SelectedNavIndex == 2) query = query.Where(r => r.IsTrackingCategory);
        else if (SelectedNavIndex == 3) query = query.Where(r => r.IsApprovalCategory);
        else query = query.Where(r => r.IsAggregateCategory);

        var distinctOrgs = query
            .Select(r => !string.IsNullOrWhiteSpace(r.ReportOrgName) ? r.ReportOrgName : r.OrgName)
            .Where(org => !string.IsNullOrWhiteSpace(org))
            .Distinct()
            .OrderBy(org => org);

        foreach (var org in distinctOrgs)
        {
            OrgFilters.Add(org);
        }

        _selectedOrgFilter = "Tất cả đơn vị báo cáo";
        OnPropertyChanged(nameof(SelectedOrgFilter));
    }

    public void UpdateFilteredReportNames()
    {
        FilteredReportNames.Clear();
        var term = _searchKeyword?.Trim().ToLower() ?? "";
        if (string.IsNullOrEmpty(term))
        {
            foreach (var name in AvailableReportNames)
            {
                FilteredReportNames.Add(name);
            }
        }
        else
        {
            foreach (var name in AvailableReportNames)
            {
                if (name.ToLower().Contains(term))
                {
                    FilteredReportNames.Add(name);
                }
            }
        }
    }

    public void SelectInputTab()
    {
        SelectedNavIndex = 0;
    }

    public void SelectSubmitTab()
    {
        SelectedNavIndex = 1;
    }

    public void SelectTrackingTab()
    {
        SelectedNavIndex = 2;
    }

    public void SelectApprovalTab()
    {
        SelectedNavIndex = 3;
    }

    public void SelectAggregateTab()
    {
        SelectedNavIndex = 4;
    }

    public void ResetFilters()
    {
        _searchKeyword = string.Empty;
        _selectedReportFilter = "Tất cả biểu mẫu báo cáo";
        _selectedOrgFilter = "Tất cả đơn vị báo cáo";
        _selectedPeriodFilter = "Tất cả kỳ";
        _selectedStatusFilter = SelectedNavIndex == 3 ? "Báo cáo đã gửi & Yêu cầu đính chính" : "Tất cả trạng thái";

        OnPropertyChanged(nameof(SearchKeyword));
        OnPropertyChanged(nameof(SelectedReportFilter));
        OnPropertyChanged(nameof(SelectedReportDisplay));
        OnPropertyChanged(nameof(SelectedOrgFilter));
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(SelectedPeriodFilter));
        OnPropertyChanged(nameof(SelectedStatusFilter));
        OnPropertyChanged(nameof(CanShowBatchApproveButton));
        OnPropertyChanged(nameof(CanShowBatchRejectButton));
        OnPropertyChanged(nameof(CanShowBatchCorrectionButton));

        UpdateFilteredReportNames();
        UpdateAvailableOrgNames();
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        var query = _allReports.AsEnumerable();

        // 1. Phân loại theo Menu bên trái (Nhập báo cáo số liệu, Gửi báo cáo, Trạng thái báo cáo đã gửi, Duyệt báo cáo, Tổng hợp báo cáo)
        if (SelectedNavIndex == 0)
        {
            query = query.Where(r => r.IsInputCategory);
        }
        else if (SelectedNavIndex == 1)
        {
            query = query.Where(r => r.IsSubmitCategory);
        }
        else if (SelectedNavIndex == 2)
        {
            // Trạng thái báo cáo đã gửi: Chỉ hiện báo cáo đã gửi và báo cáo đã duyệt
            query = query.Where(r => r.IsTrackingCategory && (r.StateId == 3 || r.StateId == 4 || r.Status == "3" || r.Status == "4" || r.DisplayStatusName.Contains("đã gửi") || r.DisplayStatusName.Contains("đã duyệt")));
        }
        else if (SelectedNavIndex == 3)
        {
            query = query.Where(r => r.IsApprovalCategory);
        }
        else
        {
            query = query.Where(r => r.IsAggregateCategory);
        }

        // 2. Lọc theo Biểu mẫu báo cáo được chọn (SelectedReportFilter)
        if (!string.IsNullOrWhiteSpace(SelectedReportFilter) &&
            SelectedReportFilter != "Tất cả biểu mẫu báo cáo" &&
            SelectedReportFilter != "Tất cả báo cáo")
        {
            query = query.Where(r => r.ObjName == SelectedReportFilter || (r.ObjName?.Contains(SelectedReportFilter) ?? false));
        }

        // 2.1. Lọc theo Đơn vị báo cáo (SelectedOrgFilter)
        if (!string.IsNullOrWhiteSpace(SelectedOrgFilter) &&
            SelectedOrgFilter != "Tất cả đơn vị báo cáo" &&
            SelectedOrgFilter != "Tất cả đơn vị")
        {
            query = query.Where(r => 
                (!string.IsNullOrWhiteSpace(r.ReportOrgName) && r.ReportOrgName == SelectedOrgFilter) ||
                (!string.IsNullOrWhiteSpace(r.OrgName) && r.OrgName == SelectedOrgFilter));
        }

        // 3. Lọc theo Từ khóa tìm kiếm nếu có gõ vào ô search (SearchKeyword)
        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            var term = SearchKeyword.Trim().ToLower();
            query = query.Where(r =>
                (r.ObjName?.ToLower().Contains(term) ?? false) ||
                (r.OrgName?.ToLower().Contains(term) ?? false) ||
                (r.TimeName?.ToLower().Contains(term) ?? false) ||
                (r.ObjId?.ToLower().Contains(term) ?? false) ||
                (r.InputGrantId?.ToLower().Contains(term) ?? false) ||
                (r.DisplayStatusName?.ToLower().Contains(term) ?? false) ||
                (r.Note?.ToLower().Contains(term) ?? false)
            );
        }

        // 4. Lọc theo Chu kỳ (Period Filter ComboBox)
        if (!string.IsNullOrWhiteSpace(SelectedPeriodFilter) && SelectedPeriodFilter != "Tất cả kỳ")
        {
            var pTerm = SelectedPeriodFilter.ToLower();
            query = query.Where(r => (r.PeriodTypeName?.ToLower().Contains(pTerm) ?? false) ||
                                     (r.TimeName?.ToLower().Contains(pTerm) ?? false) ||
                                     (r.ObjName?.ToLower().Contains(pTerm) ?? false));
        }

        // 5. Lọc theo Trạng thái (Status Filter ComboBox)
        if (!string.IsNullOrWhiteSpace(SelectedStatusFilter) && SelectedStatusFilter != "Tất cả trạng thái")
        {
            var sTerm = SelectedStatusFilter.ToLower();
            if (sTerm.Contains("nhập"))
                query = query.Where(r => r.DisplayStatusName.ToLower().Contains("nhập") || r.Status == "1" || r.Status == "7");
            else if (sTerm.Contains("giao"))
                query = query.Where(r => r.DisplayStatusName.ToLower().Contains("giao") || r.Status == "1");
            else if (sTerm.Contains("từ chối") || sTerm.Contains("trả lại"))
                query = query.Where(r => r.DisplayStatusName.ToLower().Contains("từ chối") ||
                                         r.DisplayStatusName.ToLower().Contains("trả lại") ||
                                         r.Status == "5" || r.Status == "8" || r.Status == "9");
            else if (sTerm == "báo cáo đã gửi & yêu cầu đính chính")
                query = query.Where(r => (r.StateId == 3 || r.Status == "3" || r.DisplayStatusName.ToLower().Contains("đã gửi")) ||
                                         (r.CorrectionReq > 0));
            else if (sTerm.Contains("đính chính") || sTerm.Contains("dinh chinh"))
                query = query.Where(r => r.CorrectionReq > 0);
            else if (sTerm.Contains("trình"))
                query = query.Where(r => r.DisplayStatusName.ToLower().Contains("trình") || r.Status == "2");
            else if (sTerm.Contains("gửi") || sTerm.Contains("gui"))
                query = query.Where(r => r.StateId == 3 || r.Status == "3" || r.DisplayStatusName.ToLower().Contains("đã gửi"));
            else if (sTerm.Contains("duyệt") || sTerm.Contains("duyet") || sTerm.Contains("phê duyệt"))
                query = query.Where(r => r.StateId == 4 || r.Status == "4" || r.DisplayStatusName.ToLower().Contains("đã duyệt"));
            else
                query = query.Where(r => r.DisplayStatusName.ToLower().Contains(sTerm));
        }

        var sortedQuery = query
            .OrderBy(r => r.ObjName)
            .ThenBy(r => r.PeriodSortKey)
            .ThenBy(r => r.ReportOrgName)
            .ThenBy(r => r.TargetOrgId);

        FilteredReports.Clear();
        var stt = 1;
        foreach (var r in sortedQuery)
        {
            r.Stt = stt++;
            r.PropertyChanged += OnReportItemPropertyChanged;
            FilteredReports.Add(r);
        }

        NotifyCountersChanged();
        NotifySelectionChanged();
    }

    public void NotifyCountersChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(InputReportsCount));
        OnPropertyChanged(nameof(SubmitReportsCount));
        OnPropertyChanged(nameof(TrackingReportsCount));
        OnPropertyChanged(nameof(ApprovalReportsCount));
        OnPropertyChanged(nameof(AggregateReportsCount));
        OnPropertyChanged(nameof(CurrentTabTotalCount));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
    }

    public void Logout()
    {
        var currentUsername = Session?.Username ?? LoginVM.Username;
        Session = null;
        IsLoggedIn = false;
        _allReports.Clear();
        FilteredReports.Clear();
        AvailableReportNames.Clear();
        StatusMessage = string.Empty;
        LoginVM.StatusMessage = string.Empty;
        LoginVM.HasError = false;

        // Tải lại danh sách tài khoản và tự động điền sẵn mật khẩu cho tài khoản hiện tại
        _ = LoginVM.ReloadSavedAccountsAsync(currentUsername);

        // Check for updates when returning to the login screen
        _ = CheckForAppUpdatesAsync();
    }
}
