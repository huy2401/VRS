using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VSR.Models;
using VSR.ViewModels;

namespace VSR;

public partial class MainWindow : Window
{
    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private readonly Services.ReportService _reportService = new();
    private readonly Services.UpdateService _updateService = new();

    static MainWindow()
    {
        Control.LoadedEvent.AddClassHandler<DataGridColumnHeader>((header, e) =>
        {
            var text = header.Content?.ToString() ?? "";
            if (text is "STT" or "Trạng thái" or "Thao tác" || string.IsNullOrWhiteSpace(text))
            {
                header.Classes.Add("no-sort");
                header.Classes.Remove("sortable");
            }
            else
            {
                header.Classes.Add("sortable");
                header.Classes.Remove("no-sort");
            }
        });
    }

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsSelectionColumnVisible)
                or nameof(MainViewModel.IsSttColumnVisible)
                or nameof(MainViewModel.IsApprovalTabSelected)
                or nameof(MainViewModel.SelectedNavIndex))
            {
                UpdateColumnVisibility();
            }
        };

        // Bấm ra ngoài khoảng trống bất kỳ sẽ tự động tắt dấu nháy / hủy focus trong TextBox
        AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);

        // Tunnel PointerPressed trên bảng danh sách báo cáo — đảm bảo khi click vào bất kỳ icon nào
        // (như icon cây bút ✏️ ở cột thao tác) thì dòng đó lập tức được highlight (chọn)
        var reportsGrid = this.FindControl<DataGrid>("ReportsDataGrid");
        reportsGrid?.AddHandler(InputElement.PointerPressedEvent, OnReportsGridPointerPressed, RoutingStrategies.Tunnel);

        Loaded += OnWindowLoaded;
    }

    private DataGridColumn? _colSelection;
    private DataGridColumn? _colStt;
    private DataGridColumn? _colName;
    private DataGridColumn? _colOrg;
    private DataGridColumn? _colTime;
    private DataGridColumn? _colStatus;
    private DataGridColumn? _colEndDate;
    private DataGridColumn? _colActions;
    private bool _columnsInitialized;

    private void InitializeColumns()
    {
        if (_columnsInitialized) return;
        var grid = this.FindControl<DataGrid>("ReportsDataGrid");
        if (grid == null || grid.Columns.Count < 8) return;

        _colSelection = grid.Columns.FirstOrDefault(c => c is DataGridTemplateColumn tc && tc.Header is CheckBox || c.Header == null || string.IsNullOrEmpty(c.Header?.ToString())) ?? grid.Columns[0];
        _colStt = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "STT") ?? grid.Columns[1];
        _colName = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Tên biểu mẫu báo cáo") ?? grid.Columns[2];
        _colTime = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Kỳ dữ liệu") ?? grid.Columns[3];
        _colStatus = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Trạng thái") ?? grid.Columns[4];
        _colEndDate = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Ngày kết thúc") ?? grid.Columns[5];
        _colOrg = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Đơn vị báo cáo") ?? grid.Columns[6];
        _colActions = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Thao tác") ?? grid.Columns[7];

        _columnsInitialized = true;
    }

    private void UpdateColumnVisibility()
    {
        if (ViewModel == null) return;
        var grid = this.FindControl<DataGrid>("ReportsDataGrid");
        if (grid == null) return;

        InitializeColumns();
        if (!_columnsInitialized) return;

        grid.Columns.Clear();

        if (ViewModel.IsSelectionColumnVisible && _colSelection != null)
        {
            _colSelection.IsVisible = true;
            grid.Columns.Add(_colSelection);
        }

        if (ViewModel.IsSttColumnVisible && _colStt != null)
        {
            _colStt.IsVisible = true;
            grid.Columns.Add(_colStt);
        }

        if (_colName != null)
        {
            _colName.IsVisible = true;
            grid.Columns.Add(_colName);
        }

        if (_colTime != null)
        {
            _colTime.IsVisible = true;
            grid.Columns.Add(_colTime);
        }

        if (_colStatus != null)
        {
            _colStatus.IsVisible = true;
            grid.Columns.Add(_colStatus);
        }

        if (_colEndDate != null)
        {
            // Theo dõi trạng thái báo cáo (Tab 2) và Duyệt báo cáo (Tab 3): hiển thị "Ngày duyệt"
            // Các phân hệ khác (Nhập báo cáo, Gửi báo cáo, Tổng hợp báo cáo): hiển thị "Ngày kết thúc"
            _colEndDate.Header = (ViewModel.IsTrackingTabSelected || ViewModel.IsApprovalTabSelected) ? "Ngày duyệt" : "Ngày kết thúc";
            _colEndDate.IsVisible = true;
            grid.Columns.Add(_colEndDate);
        }

        if (ViewModel.IsApprovalTabSelected && _colOrg != null)
        {
            _colOrg.IsVisible = true;
            grid.Columns.Add(_colOrg);
        }

        // Cột Thao tác LUÔN LUÔN ở cuối cùng bên phải
        if (_colActions != null)
        {
            _colActions.IsVisible = true;
            grid.Columns.Add(_colActions);
        }

        // Đồng bộ lại DisplayIndex theo đúng thứ tự mảng đã Add
        // Tránh tình trạng Avalonia DataGrid giữ lại DisplayIndex cũ khiến cột Thao tác bị nhảy lên trước
        for (int i = 0; i < grid.Columns.Count; i++)
        {
            grid.Columns[i].DisplayIndex = i;
        }

        // Đảm bảo star column được tính toán lại kích thước chuẩn xác
        if (_colName != null)
        {
            _colName.Width = new DataGridLength(0);
            grid.UpdateLayout();
            _colName.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        }

        grid.InvalidateMeasure();
        grid.InvalidateArrange();
        grid.UpdateLayout();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var visual = e.Source as Visual;
        bool isInsideInputOrInteractive = false;

        while (visual != null)
        {
            if (visual is TextBox || visual is Button || visual is Popup || (visual is Control c && (c.Name == "AccountSuggestionPopup" || c.Name == "EyeBorder" || c.Name == "TxtEyeIcon")))
            {
                isInsideInputOrInteractive = true;
                break;
            }
            visual = visual.GetVisualParent();
        }

        if (!isInsideInputOrInteractive)
        {
            FocusManager?.Focus(null);
            if (ViewModel != null)
            {
                ViewModel.LoginVM.IsAccountDropdownOpen = false;
            }
        }
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateColumnVisibility();

        var dropDown = this.FindControl<DropDownButton>("ReportDropDownButton");
        if (dropDown != null)
        {
            dropDown.SizeChanged += (s, ev) =>
            {
                if (dropDown.Flyout is Flyout flyout && flyout.Content is Border border && ev.NewSize.Width > 0)
                {
                    border.Width = ev.NewSize.Width;
                    border.MinWidth = ev.NewSize.Width;
                }
            };
        }

        if (ViewModel != null)
        {
            await ViewModel.InitializeAsync();
            UpdateColumnVisibility();

            if (ViewModel.HasUpdate)
            {
                await ShowUpdateDialogAsync();
            }
        }
    }

    private async void OnUpdateBannerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        await ShowUpdateDialogAsync();
    }

    private async Task ShowUpdateDialogAsync()
    {
        if (ViewModel == null || !ViewModel.HasUpdate) return;
        var updateDialog = new Views.RequiredUpdateDialog(ViewModel.UpdateMessage, ViewModel.UpdateDownloadUrl, _updateService);
        await updateDialog.ShowDialog(this);
    }

    private async void OnLoginButtonClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.LoginVM.IsAccountDropdownOpen = false;
            await ViewModel.LoginVM.ExecuteLoginAsync();
        }
    }

    private void OnUsernamePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (ViewModel != null && ViewModel.LoginVM.SavedAccounts.Count > 0)
        {
            ViewModel.LoginVM.ShowAllSavedAccounts();
        }
    }

    private void OnUsernameGotFocus(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.LoginVM.SavedAccounts.Count > 0)
        {
            ViewModel.LoginVM.ShowAllSavedAccounts();
        }
    }

    private void OnUsernameLostFocus(object? sender, RoutedEventArgs e)
    {
        // Khi mất focus khỏi ô tài khoản (chuyển sang ô khác)
        // Popup sẽ tự đóng nhờ IsLightDismissEnabled
    }

    private void OnUsernameTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (ViewModel != null && sender is TextBox tb)
        {
            // So sánh và lọc danh sách gợi ý khi người dùng gõ
            if (tb.IsFocused)
            {
                ViewModel.LoginVM.FilterSavedAccounts(tb.Text ?? "");
            }
        }
    }

    private void OnSelectSavedAccountPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is AccountCredential acc && ViewModel != null)
        {
            ViewModel.LoginVM.SelectAccount(acc);
        }
    }

    private async void OnDeleteSavedAccountClick(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        var acc = btn?.CommandParameter as AccountCredential ?? btn?.DataContext as AccountCredential;
        if (acc != null && ViewModel != null)
        {
            ViewModel.LoginVM.IsAccountDropdownOpen = false;
            var dialog = new Views.ConfirmDeleteAccountDialog(acc);
            await dialog.ShowDialog(this);
            if (dialog.IsConfirmed)
            {
                await ViewModel.LoginVM.DeleteSavedAccountAsync(acc);
            }
        }
    }

    private void OnShowPasswordPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Control control)
        {
            e.Pointer.Capture(control);
        }
        var txtPassword = this.FindControl<TextBox>("TxtPassword");
        var txtEyeIcon = this.FindControl<TextBlock>("TxtEyeIcon");
        if (txtPassword != null)
        {
            txtPassword.PasswordChar = '\0'; // Hiện mật khẩu
        }
        if (txtEyeIcon != null)
        {
            txtEyeIcon.Opacity = 1.0;
        }
        if (ViewModel != null)
        {
            ViewModel.LoginVM.RevealPassword = true;
        }
    }

    private void OnShowPasswordPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (sender is Control control)
        {
            e.Pointer.Capture(null);
        }
        HidePasswordText();
    }

    private void OnShowPasswordPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        HidePasswordText();
    }

    private void HidePasswordText()
    {
        var txtPassword = this.FindControl<TextBox>("TxtPassword");
        var txtEyeIcon = this.FindControl<TextBlock>("TxtEyeIcon");
        if (txtPassword != null)
        {
            txtPassword.PasswordChar = '●'; // Ẩn lại mật khẩu
        }
        if (txtEyeIcon != null)
        {
            txtEyeIcon.Opacity = 0.6;
        }
        if (ViewModel != null)
        {
            ViewModel.LoginVM.RevealPassword = false;
        }
    }

    private async void OnRefreshReportsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            var scrollViewer = grid?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            var previousOffset = scrollViewer?.Offset;
            await ViewModel.RefreshCurrentTabAsync();

            // DataGrid được bind lại sau refresh; khôi phục offset ở lượt render kế tiếp.
            if (scrollViewer != null && previousOffset.HasValue)
            {
                Dispatcher.UIThread.Post(() => scrollViewer.Offset = previousOffset.Value, DispatcherPriority.Render);
            }
        }
    }

    private void OnSelectInputTabClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SelectInputTab();
    }

    private void OnSelectSubmitTabClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SelectSubmitTab();
    }

    private void OnSelectTrackingTabClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SelectTrackingTab();
    }

    private void OnSelectApprovalTabClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SelectApprovalTab();
    }

    private void OnSelectAggregateTabClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SelectAggregateTab();
    }

    private void OnResetFiltersClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResetFilters();
    }

    private void OnReportDropDownFlyoutOpened(object? sender, EventArgs e)
    {
        var dropDown = this.FindControl<DropDownButton>("ReportDropDownButton");
        var border = (sender as Flyout)?.Content as Border;
        if (dropDown != null && border != null && dropDown.Bounds.Width > 0)
        {
            border.Width = dropDown.Bounds.Width;
            border.MinWidth = dropDown.Bounds.Width;
            if (border.Parent is Control parentControl)
            {
                parentControl.Width = dropDown.Bounds.Width;
                parentControl.MaxWidth = 2500;
            }
        }
        var searchBox = border?.FindControl<TextBox>("ReportFilterSearchBox");
        searchBox?.Focus();
    }

    private void OnReportNameSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem != null)
        {
            var dropDown = this.FindControl<DropDownButton>("ReportDropDownButton");
            dropDown?.Flyout?.Hide();
        }
    }

    private void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.Logout();
    }

    /// <summary>
    /// Bắt sự kiện nhấn chuột (Tunnel) trên DataGrid danh sách báo cáo — chọn dòng ngay khi click vào bất kỳ nút nào trong dòng
    /// </summary>
    private void OnReportsGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Visual;
        var row = source?.FindAncestorOfType<DataGridRow>();
        if (row?.DataContext is ReportItem report)
        {
            var grid = sender as DataGrid ?? this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null)
            {
                grid.SelectedItem = report;
            }
        }
    }

    /// <summary>
    /// Mục 1: ✏️ Chỉnh sửa / Nhập số liệu báo cáo
    /// </summary>
    private void OnReportActionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Nút trong DataGridTemplateColumn không tự chọn dòng như khi bấm trực tiếp lên ô.
        // Chọn dòng ngay khi nhấn nút thao tác để giữ trạng thái highlight nhất quán.
        if (sender is not Control { DataContext: ReportItem report }) return;

        var grid = this.FindControl<DataGrid>("ReportsDataGrid");
        if (grid == null) return;

        grid.SelectedItem = report;
        grid.Focus();
    }

    private async void OnEditReportClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report != null && ViewModel?.Session != null)
        {
            var editWindow = new Views.ReportEditWindow(report, ViewModel.Session);
            await editWindow.ShowDialog(this);

            // Giữ nguyên mục hiện tại sau khi đóng màn hình nhập liệu, kể cả khi vừa trình lãnh đạo.
            // Người dùng chủ động chuyển sang "Gửi báo cáo" khi cần.
            ViewModel.NotifyCountersChanged();
            ViewModel.UpdateAvailableOrgNames();
            if (report.Category == ReportCategoryType.Submit && report.StateId == 2)
            {
                // Đã trình lãnh đạo: gỡ ngay khỏi danh sách Nhập liệu tại chỗ,
                // không gọi lại IOC và không tạo lại DataGrid nên không nhảy scrollbar.
                ViewModel.RemoveReportFromCurrentList(report);
            }
            // Không dựng lại FilteredReports khi đóng cửa sổ nhập liệu. Việc dựng lại danh sách
            // khiến DataGrid tạo lại và đưa thanh cuộn của danh sách báo cáo về đầu.
        }
    }

    /// <summary>
    /// Mục 2 & 3: 👁️ Xem thông tin báo cáo
    /// </summary>
    private async void OnViewReportClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report != null && ViewModel?.Session != null)
        {
            var editWindow = new Views.ReportEditWindow(report, ViewModel.Session);
            await editWindow.ShowDialog(this);
            // Giữ nguyên danh sách và vị trí cuộn sau khi đóng cửa sổ xem báo cáo.
        }
    }

    /// <summary>
    /// Mục 2: ✈️ Gửi báo cáo lên cấp trên / Tỉnh
    /// </summary>
    private async void OnSendReportClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report != null && ViewModel != null)
        {
            if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
            {
                var (success, msg) = await _reportService.SendReportsAsync(ViewModel.Session, new List<ReportItem> { report });
                if (!success)
                {
                    var errDialog = new Views.RejectionReasonDialog(
                        report,
                        title: "Lỗi gửi báo cáo",
                        subTitle: "Không thể hoàn tất gửi báo cáo lên hệ thống IOC",
                        reasonLabel: "Chi tiết lỗi:",
                        content: msg,
                        icon: "⚠️",
                        headerBg: "#FEF2F2",
                        headerBorder: "#FECACA",
                        headerFg: "#DC2626"
                    );
                    await errDialog.ShowDialog(this);
                    return;
                }
            }

            // Cập nhật trạng thái thành Đã gửi (STATE_ID = 3)
            report.StateId = 3;
            report.Status = "3";
            report.StatusName = "Báo cáo đã được gửi";
            ViewModel.StatusMessage = $"Đã duyệt và gửi báo cáo \"{report.ObjName}\" lên cấp trên thành công!";
            ViewModel.ApplyFilter();

            var dialog = new Views.RejectionReasonDialog(
                report,
                title: "Gửi báo cáo thành công",
                subTitle: "Báo cáo đã được chuyển sang trạng thái \"Báo cáo đã được gửi\"",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Báo cáo \"{report.ObjName}\" kỳ {report.TimeName} đã được gửi lên hệ thống IOC Tỉnh thành công. Bạn có thể theo dõi tiến độ phê duyệt tại mục \"Theo dõi trạng thái báo cáo\".",
                icon: "✈️",
                headerBg: "#DCFCE7",
                headerBorder: "#86EFAC",
                headerFg: "#15803D"
            );
            await dialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 5: ⚡ Tổng hợp hàng loạt báo cáo đã chọn (hoặc tất cả các báo cáo đang hiển thị trong danh sách)
    /// </summary>
    private async Task ExecuteBatchAggregateAsync(bool onlyApproved)
    {
        if (ViewModel == null) return;

        var selected = ViewModel.FilteredReports.Where(r => r.IsSelected && (r.IsAggregateCategory || r.IsInputCategory)).ToList();
        var targetReports = selected.Count > 0
            ? selected
            : ViewModel.FilteredReports.Where(r => r.IsAggregateCategory || r.IsInputCategory).ToList();

        if (targetReports.Count == 0)
        {
            var warningDialog = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = "Tổng hợp hàng loạt" },
                title: "Không có báo cáo",
                subTitle: "Không tìm thấy biểu mẫu báo cáo nào trong danh sách để tổng hợp",
                reasonLabel: "Hướng dẫn thao tác:",
                content: "Danh sách hiện tại không có báo cáo nào thuộc mục Tổng hợp hoặc Nhập liệu. Vui lòng kiểm tra lại bộ lọc.",
                icon: "⚠️",
                headerBg: "#FEF3C7",
                headerBorder: "#FDE68A",
                headerFg: "#B45309"
            );
            await warningDialog.ShowDialog(this);
            return;
        }

        var modeTitle = onlyApproved ? "đã duyệt" : "tất cả";
        var (successCount, failCount, errorMessages) = await ViewModel.BatchAggregateSelectedReportsAsync(onlyApproved: onlyApproved);

        var resultDialog = new Views.RejectionReasonDialog(
            new ReportItem { ObjName = $"Hoàn tất tổng hợp hàng loạt {modeTitle}: {successCount}/{targetReports.Count} báo cáo" },
            title: failCount == 0 ? $"Tổng hợp hàng loạt ({modeTitle}) thành công" : $"Hoàn tất tổng hợp ({modeTitle}) có cảnh báo",
            subTitle: $"Đã xử lý xong {targetReports.Count} báo cáo (Thành công: {successCount}, Thất bại: {failCount})",
            reasonLabel: "Chi tiết kết quả:",
            content: failCount == 0
                ? $"Đã tổng hợp số liệu trực tiếp từ API IOC và lưu thành công cho toàn bộ {successCount} biểu mẫu báo cáo!"
                : $"Thành công: {successCount} báo cáo.\nThất bại ({failCount} báo cáo):\n" + string.Join("\n", errorMessages.Take(10)),
            icon: failCount == 0 ? "⚡" : "⚠️",
            headerBg: failCount == 0 ? (onlyApproved ? "#F0FDF4" : "#F5F3FF") : "#FEF2F2",
            headerBorder: failCount == 0 ? (onlyApproved ? "#BBF7D0" : "#DDD6FE") : "#FECACA",
            headerFg: failCount == 0 ? (onlyApproved ? "#16A34A" : "#7C3AED") : "#DC2626"
        );
        await resultDialog.ShowDialog(this);
    }

    private void OnBatchAggregateApprovedClick(object? sender, RoutedEventArgs e)
    {
        _ = ExecuteBatchAggregateAsync(onlyApproved: true);
    }

    private void OnBatchAggregateAllClick(object? sender, RoutedEventArgs e)
    {
        _ = ExecuteBatchAggregateAsync(onlyApproved: false);
    }

    /// <summary>
    /// Mục 1: 🚀 Trình lãnh đạo nhiều báo cáo bằng một lệnh IOC duy nhất.
    /// </summary>
    private async void OnSubmitMultipleReportsToLeaderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Session == null || !ViewModel.Session.IsAuthenticated) return;

        var selected = ViewModel.FilteredReports.Where(r => r.IsSelected && r.IsInputCategory).ToList();
        if (selected.Count == 0)
        {
            var warning = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = "Trình lãnh đạo hàng loạt" },
                title: "Chưa chọn báo cáo",
                subTitle: "Vui lòng tích chọn ít nhất một báo cáo cần trình lãnh đạo",
                reasonLabel: "Hướng dẫn:",
                content: "Tích chọn các báo cáo tại cột đầu tiên, sau đó bấm nút \"Trình lãnh đạo hàng loạt\".",
                icon: "⚠️", headerBg: "#FEF3C7", headerBorder: "#FDE68A", headerFg: "#B45309");
            await warning.ShowDialog(this);
            return;
        }

        var summary = new ReportItem { ObjName = $"{selected.Count} báo cáo đã chọn", TimeName = "Trình cùng ý kiến" };
        var dialog = new Views.SubmitToLeaderDialog(summary, 0, 0);
        await dialog.ShowDialog(this);
        if (!dialog.IsConfirmed) return;

        var (success, message) = await _reportService.SubmitReportsToLeaderAsync(ViewModel.Session, selected, dialog.OpinionText);
        if (!success)
        {
            var error = new Views.RejectionReasonDialog(selected[0], "Lỗi trình lãnh đạo hàng loạt", "Không thể trình danh sách báo cáo lên IOC", "Chi tiết lỗi:", message, "⚠️", "#FEF2F2", "#FECACA", "#DC2626");
            await error.ShowDialog(this);
            return;
        }

        foreach (var report in selected)
        {
            report.SubmitOpinion = dialog.OpinionText;
            report.Note = dialog.OpinionText;
            report.StateId = 2;
            report.Status = "2";
            report.StatusName = "Đã trình lãnh đạo";
            report.Category = ReportCategoryType.Submit;
            report.IsSelected = false;
            report.NotifyStatusChanged();
        }

        ViewModel.NotifyCountersChanged();
        ViewModel.ApplyFilter();
        var notice = new Views.RejectionReasonDialog(selected[0], "Trình lãnh đạo hàng loạt thành công", $"Đã trình {selected.Count} báo cáo", "Thông báo:", message, "🚀", "#F5F3FF", "#DDD6FE", "#6D28D9");
        await notice.ShowDialog(this);
    }

    /// <summary>
    /// Mục 2: 🚫 Từ chối nhiều báo cáo đã trình lãnh đạo, gọi một API FNC010_P19.
    /// </summary>
    private async void OnRejectMultipleSubmitReportsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Session == null || !ViewModel.Session.IsAuthenticated) return;

        var selected = ViewModel.FilteredReports.Where(r => r.IsSelected && r.IsSubmitCategory).ToList();
        if (selected.Count == 0)
        {
            var warning = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = "Từ chối nhiều báo cáo" },
                "Chưa chọn báo cáo", "Vui lòng tích chọn ít nhất một báo cáo để từ chối", "Hướng dẫn:",
                "Tích chọn các báo cáo tại cột đầu tiên rồi bấm \"Từ chối nhiều báo cáo\".", "⚠️", "#FEF3C7", "#FDE68A", "#B45309");
            await warning.ShowDialog(this);
            return;
        }

        var dialog = new Views.RejectReportDialog(selected.Count);
        await dialog.ShowDialog(this);
        if (!dialog.IsConfirmed) return;

        var (success, message) = await _reportService.RejectReportsAsync(ViewModel.Session, selected, dialog.RejectionReason);
        if (!success)
        {
            var error = new Views.RejectionReasonDialog(selected[0], "Lỗi từ chối nhiều báo cáo", "Không thể hoàn tất thao tác trên IOC", "Chi tiết lỗi:", message, "⚠️", "#FEF2F2", "#FECACA", "#DC2626");
            await error.ShowDialog(this);
            return;
        }

        foreach (var report in selected)
        {
            report.StateId = 8;
            report.Status = "8";
            report.StatusName = "Báo cáo bị từ chối cấp đơn vị";
            report.Note = dialog.RejectionReason;
            report.Category = ReportCategoryType.Input;
            report.IsSelected = false;
            report.NotifyStatusChanged();
            ViewModel.RemoveReportFromCurrentList(report);
        }

        ViewModel.StatusMessage = $"Đã từ chối {selected.Count} báo cáo và trả về mục Nhập liệu.";
        var notice = new Views.RejectionReasonDialog(selected[0], "Từ chối nhiều báo cáo thành công", $"Đã trả lại {selected.Count} báo cáo", "Thông báo:", message, "🚫", "#FEF2F2", "#FECACA", "#DC2626");
        await notice.ShowDialog(this);
    }

    /// <summary>
    /// Mục 2: ✈️ Gửi nhiều báo cáo đã được chọn cùng lúc
    /// </summary>
    private async void OnSendMultipleReportsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var selected = ViewModel.FilteredReports.Where(r => r.IsSelected && r.IsSubmitCategory).ToList();
        if (selected.Count == 0)
        {
            var warningDialog = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = "Gửi nhiều báo cáo" },
                title: "Chưa chọn báo cáo",
                subTitle: "Vui lòng chọn ít nhất một báo cáo để thực hiện gửi",
                reasonLabel: "Hướng dẫn thao tác:",
                content: "Bạn chưa tích chọn báo cáo nào trong danh sách. Hãy tích chọn vào ô vuông ở đầu các dòng báo cáo cần gửi (hoặc tích ô trên thanh tiêu đề để chọn tất cả), sau đó bấm \"Gửi nhiều báo cáo\".",
                icon: "⚠️",
                headerBg: "#FEF3C7",
                headerBorder: "#FDE68A",
                headerFg: "#B45309"
            );
            await warningDialog.ShowDialog(this);
            return;
        }

        if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
        {
            var (success, msg) = await _reportService.SendReportsAsync(ViewModel.Session, selected);
            if (!success)
            {
                var errDialog = new Views.RejectionReasonDialog(
                    new ReportItem { ObjName = "Gửi nhiều báo cáo" },
                    title: "Lỗi gửi báo cáo",
                    subTitle: "Không thể hoàn tất gửi danh sách báo cáo lên hệ thống IOC",
                    reasonLabel: "Chi tiết lỗi:",
                    content: msg,
                    icon: "⚠️",
                    headerBg: "#FEF2F2",
                    headerBorder: "#FECACA",
                    headerFg: "#DC2626"
                );
                await errDialog.ShowDialog(this);
                return;
            }
        }

        int count = ViewModel.SendSelectedReports();
        if (count > 0)
        {
            var successDialog = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = $"Đã gửi thành công {count} báo cáo" },
                title: "Gửi nhiều báo cáo thành công",
                subTitle: $"Đã chuyển {count} báo cáo sang trạng thái \"Báo cáo đã được gửi\"",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Đã gửi thành công tổng cộng {count} biểu mẫu báo cáo lên hệ thống IOC Tỉnh. Bạn có thể theo dõi tiến độ phê duyệt tại mục \"Theo dõi trạng thái báo cáo\".",
                icon: "✈️",
                headerBg: "#DCFCE7",
                headerBorder: "#86EFAC",
                headerFg: "#15803D"
            );
            await successDialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 4: ✅ Phê duyệt nhiều báo cáo cùng lúc
    /// </summary>
    private async void OnApproveMultipleReportsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var allSelected = ViewModel.FilteredReports.Where(r => r.IsSelected && r.IsApprovalCategory).ToList();
        if (allSelected.Count == 0)
        {
            var warningDialog = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = "Duyệt nhiều báo cáo" },
                title: "Chưa chọn báo cáo",
                subTitle: "Vui lòng tích chọn ít nhất 1 biểu mẫu báo cáo để thực hiện phê duyệt",
                reasonLabel: "Hướng dẫn thao tác:",
                content: "Bạn có thể tích chọn vào các ô vuông ở đầu mỗi dòng hoặc tích ô chọn ở tiêu đề bảng để chọn tất cả báo cáo cần duyệt.",
                icon: "⚠️",
                headerBg: "#FEF3C7",
                headerBorder: "#FDE68A",
                headerFg: "#B45309"
            );
            await warningDialog.ShowDialog(this);
            return;
        }

        // Lọc ra những báo cáo thực sự ở trạng thái "Đã gửi" (State 3) để duyệt
        var selected = allSelected.Where(r => r.StateId == 3 || r.Status is "3" or "3.0" || r.DisplayStatusName.Contains("đã gửi")).ToList();

        // Những báo cáo đã ở trạng thái "Đã duyệt" (State 4)
        var alreadyApproved = allSelected.Where(r => r.StateId == 4 || r.Status is "4" or "4.0" || r.DisplayStatusName.Contains("đã duyệt") || r.DisplayStatusName.Contains("phê duyệt")).ToList();

        if (selected.Count == 0 && alreadyApproved.Count > 0)
        {
            var infoDialog = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = "Duyệt nhiều báo cáo" },
                title: "Báo cáo đã được duyệt",
                subTitle: "Tất cả các báo cáo bạn chọn đều đã được phê duyệt trước đó.",
                reasonLabel: "Thông báo:",
                content: $"Bạn đã chọn {alreadyApproved.Count} báo cáo, nhưng tất cả đều ở trạng thái 'Báo cáo đã được duyệt'. Hệ thống sẽ không thực hiện duyệt lại.",
                icon: "ℹ️",
                headerBg: "#DBEAFE",
                headerBorder: "#BFDBFE",
                headerFg: "#1E3A8A"
            );
            await infoDialog.ShowDialog(this);
            return;
        }

        if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
        {
            var (success, msg) = await _reportService.ApproveReportsAsync(ViewModel.Session, selected);
            if (!success)
            {
                var errDialog = new Views.RejectionReasonDialog(
                    new ReportItem { ObjName = "Duyệt nhiều báo cáo" },
                    title: "Lỗi phê duyệt báo cáo",
                    subTitle: "Không thể hoàn tất phê duyệt danh sách báo cáo lên hệ thống IOC",
                    reasonLabel: "Chi tiết lỗi:",
                    content: msg,
                    icon: "⚠️",
                    headerBg: "#FEF2F2",
                    headerBorder: "#FECACA",
                    headerFg: "#DC2626"
                );
                await errDialog.ShowDialog(this);
                return;
            }
        }

        int count = ViewModel.ApproveSelectedReports();
        if (count > 0)
        {
            var successDialog = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = $"Đã duyệt thành công {count} báo cáo" },
                title: "Phê duyệt nhiều báo cáo thành công",
                subTitle: $"Đã chuyển {count} báo cáo sang trạng thái \"Báo cáo đã được duyệt\"",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Đã phê duyệt thành công tổng cộng {count} biểu mẫu báo cáo trên hệ thống IOC. Các đơn vị cấp dưới đã có thể xem trạng thái báo cáo đã được duyệt.",
                icon: "✅",
                headerBg: "#DCFCE7",
                headerBorder: "#86EFAC",
                headerFg: "#15803D"
            );
            await successDialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 4: 📝 Xử lý nhiều yêu cầu đính chính cùng lúc
    /// </summary>
    private async void OnBatchCorrectionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var selected = ViewModel.FilteredReports.Where(r => r.IsSelected && r.IsApprovalCategory && r.CorrectionReq > 0).ToList();
        if (selected.Count == 0)
        {
            var warningDialog = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = "Duyệt nhiều yêu cầu đính chính" },
                title: "Chưa chọn báo cáo có yêu cầu đính chính",
                subTitle: "Vui lòng tích chọn các báo cáo đang có yêu cầu xin đính chính số liệu (có icon 📝)",
                reasonLabel: "Hướng dẫn:",
                content: "Bạn chưa tích chọn báo cáo nào có cờ xin đính chính số liệu. Hãy tích chọn vào ô vuông ở đầu các dòng báo cáo có icon 📝, sau đó bấm \"Duyệt nhiều yêu cầu đính chính\".",
                icon: "⚠️",
                headerBg: "#FEF3C7",
                headerBorder: "#FDE68A",
                headerFg: "#B45309"
            );
            await warningDialog.ShowDialog(this);
            return;
        }

        var dialog = new Views.ApproveCorrectionDialog(selected);
        await dialog.ShowDialog(this);

        if (dialog.Decision == Views.CorrectionDecision.Cancel)
        {
            return;
        }

        bool agree = dialog.Decision == Views.CorrectionDecision.Agree;

        if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
        {
            var (successCount, failCount, msg) = await _reportService.BatchProcessCorrectionRequestsAsync(
                ViewModel.Session, selected, agree, dialog.ResponseNote);

            if (failCount > 0 && successCount == 0)
            {
                var errDialog = new Views.RejectionReasonDialog(
                    selected[0],
                    title: "Lỗi xử lý yêu cầu đính chính",
                    subTitle: "Không thể hoàn tất xử lý danh sách yêu cầu đính chính",
                    reasonLabel: "Chi tiết lỗi:",
                    content: msg,
                    icon: "⚠️",
                    headerBg: "#FEF2F2",
                    headerBorder: "#FECACA",
                    headerFg: "#DC2626"
                );
                await errDialog.ShowDialog(this);
                return;
            }
        }

        if (agree)
        {
            // Đồng ý: Đổi trạng thái sang State 6 và xóa khỏi danh sách duyệt
            foreach (var r in selected)
            {
                r.StateId = 6;
                r.Status = "6";
                r.StatusName = "Báo cáo cần đính chính";
                r.CorrectionReq = 0;
                r.IsSelected = false;
                r.NotifyStatusChanged();
                ViewModel.RemoveReport(r);
            }

            ViewModel.StatusMessage = $"Đã duyệt đính chính và trả lại {selected.Count} báo cáo cho các đơn vị thành công!";
            ViewModel.ApplyFilter();

            var successDialog = new Views.RejectionReasonDialog(
                selected[0],
                title: "Duyệt yêu cầu đính chính thành công",
                subTitle: $"Đã chuyển {selected.Count} báo cáo sang trạng thái \"Báo cáo cần đính chính\"",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Đã duyệt yêu cầu xin đính chính số liệu cho toàn bộ {selected.Count} biểu mẫu báo cáo đã chọn.\nCác báo cáo này đã được trả lại cho các đơn vị cấp dưới để mở lại quyền nhập số liệu và không còn nằm trong danh sách cần duyệt.",
                icon: "🔄",
                headerBg: "#FFF7ED",
                headerBorder: "#FED7AA",
                headerFg: "#EA580C"
            );
            await successDialog.ShowDialog(this);
        }
        else
        {
            // Không đồng ý: Giữ nguyên State 4, xóa cờ CorrectionReq = 0
            foreach (var r in selected)
            {
                r.StateId = 4;
                r.Status = "4";
                r.StatusName = "Báo cáo đã được duyệt";
                r.CorrectionReq = 0;
                r.IsSelected = false;
                r.NotifyStatusChanged();
            }

            ViewModel.StatusMessage = $"Đã từ chối yêu cầu đính chính cho {selected.Count} báo cáo. Các báo cáo giữ nguyên trạng thái đã duyệt.";
            ViewModel.ApplyFilter();

            var noticeDialog = new Views.RejectionReasonDialog(
                selected[0],
                title: "Từ chối yêu cầu đính chính",
                subTitle: $"Đã từ chối yêu cầu đính chính của {selected.Count} báo cáo",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Đã từ chối yêu cầu xin đính chính số liệu cho {selected.Count} biểu mẫu báo cáo đã chọn.\nCác báo cáo này tiếp tục giữ nguyên trạng thái \"Báo cáo đã được duyệt\" và cờ yêu cầu đính chính đã được gỡ bỏ.",
                icon: "ℹ️",
                headerBg: "#EFF6FF",
                headerBorder: "#BFDBFE",
                headerFg: "#1E40AF"
            );
            await noticeDialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 4: 🚫 Từ chối nhiều báo cáo cùng lúc (khi chọn "Báo cáo đã được duyệt")
    /// Gọi API FNC010_P19 đúng 1 lần duy nhất
    /// </summary>
    private async void OnRejectMultipleReportsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var selected = ViewModel.FilteredReports.Where(r => r.IsSelected && r.IsApprovalCategory).ToList();
        if (selected.Count == 0)
        {
            var alert = new Views.RejectionReasonDialog(
                new ReportItem { ObjName = "Chưa chọn báo cáo" },
                title: "Chưa chọn báo cáo",
                subTitle: "Vui lòng tích chọn các báo cáo cần từ chối",
                reasonLabel: "Hướng dẫn:",
                content: "Vui lòng tích vào ô chọn ở đầu mỗi dòng báo cáo để thực hiện từ chối hàng loạt.",
                icon: "⚠️",
                headerBg: "#FEF2F2",
                headerBorder: "#FECACA",
                headerFg: "#DC2626"
            );
            await alert.ShowDialog(this);
            return;
        }

        var rejectDialog = new Views.RejectReportDialog(selected.Count);
        await rejectDialog.ShowDialog(this);

        if (rejectDialog.IsConfirmed)
        {
            if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
            {
                var (success, msg) = await _reportService.RejectReportsAsync(ViewModel.Session, selected, rejectDialog.RejectionReason);
                if (!success)
                {
                    var errDialog = new Views.RejectionReasonDialog(
                        selected[0],
                        title: "Lỗi từ chối nhiều báo cáo",
                        subTitle: "Không thể hoàn tất từ chối các báo cáo trên hệ thống IOC",
                        reasonLabel: "Chi tiết lỗi:",
                        content: msg,
                        icon: "⚠️",
                        headerBg: "#FEF2F2",
                        headerBorder: "#FECACA",
                        headerFg: "#DC2626"
                    );
                    await errDialog.ShowDialog(this);
                    return;
                }
            }

            foreach (var r in selected)
            {
                r.StateId = 8;
                r.Status = "8";
                r.StatusName = "Báo cáo bị từ chối cấp đơn vị";
                r.IsSelected = false;
                r.NotifyStatusChanged();
            }

            ViewModel.StatusMessage = $"Đã từ chối thành công {selected.Count} báo cáo!";
            ViewModel.ApplyFilter();

            var successDialog = new Views.RejectionReasonDialog(
                selected[0],
                title: "Từ chối báo cáo thành công",
                subTitle: $"Đã từ chối {selected.Count} báo cáo",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Đã hoàn tất từ chối {selected.Count} báo cáo đã chọn trên hệ thống IOC trong 1 lần gọi API duy nhất.\nCác báo cáo này đã được chuyển về trạng thái bị từ chối.",
                icon: "🚫",
                headerBg: "#FEF2F2",
                headerBorder: "#FECACA",
                headerFg: "#DC2626"
            );
            await successDialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 2: 🚫 Từ chối báo cáo kèm lý do (chuyển về mục Nhập báo cáo số liệu)
    /// </summary>
    private async void OnRejectReportClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report != null && ViewModel != null)
        {
            var rejectDialog = new Views.RejectReportDialog(report);
            await rejectDialog.ShowDialog(this);

            if (rejectDialog.IsConfirmed)
            {
                if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
                {
                    var (success, msg) = await _reportService.RejectApprovalReportAsync(ViewModel.Session, report, rejectDialog.RejectionReason);
                    if (!success)
                    {
                        var errDialog = new Views.RejectionReasonDialog(
                            report,
                            title: "Lỗi từ chối báo cáo",
                            subTitle: "Không thể hoàn tất thao tác từ chối trên hệ thống IOC",
                            reasonLabel: "Chi tiết lỗi:",
                            content: msg,
                            icon: "⚠️",
                            headerBg: "#FEF2F2",
                            headerBorder: "#FECACA",
                            headerFg: "#DC2626"
                        );
                        await errDialog.ShowDialog(this);
                        return;
                    }
                }

                // Cập nhật trạng thái thành Bị từ chối và chuyển phân loại về mục Nhập báo cáo số liệu
                report.StateId = 8;
                report.Status = "8";
                report.StatusName = "Báo cáo bị từ chối cấp đơn vị";
                report.Note = rejectDialog.RejectionReason;
                report.Category = ReportCategoryType.Input;

                ViewModel.StatusMessage = $"Đã từ chối báo cáo \"{report.ObjName}\". Biểu mẫu đã chuyển về mục \"Nhập báo cáo số liệu\" kèm lý do từ chối.";
                ViewModel.ApplyFilter();

                var successDialog = new Views.RejectionReasonDialog(
                    report,
                    title: "Từ chối báo cáo thành công",
                    subTitle: "Báo cáo đã chuyển về mục \"Nhập báo cáo số liệu\"",
                    reasonLabel: "Lý do từ chối đã gửi:",
                    content: !string.IsNullOrWhiteSpace(rejectDialog.RejectionReason) ? rejectDialog.RejectionReason : "Báo cáo đã bị từ chối và yêu cầu nhập lại số liệu.",
                    icon: "🚫",
                    headerBg: "#FEF2F2",
                    headerBorder: "#FECACA",
                    headerFg: "#DC2626"
                );
                await successDialog.ShowDialog(this);
            }
        }
    }

    /// <summary>
    /// Mục 1: 💬 Xem chi tiết lý do báo cáo bị từ chối
    /// </summary>
    private async void OnShowRejectionReasonClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
            var dialog = new Views.RejectionReasonDialog(report);
            await dialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 2: 💬 Xem ý kiến trình lãnh đạo đã nhập ở mục 1
    /// </summary>
    private async void OnShowOpinionClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report != null)
        {
            var opinion = !string.IsNullOrWhiteSpace(report.SubmitOpinion) ? report.SubmitOpinion : report.Note;
            var dialog = new Views.RejectionReasonDialog(
                report,
                title: "Ý kiến khi trình lãnh đạo",
                subTitle: "Nội dung ghi chú do người lập biểu mẫu nhập khi bấm Trình lãnh đạo",
                reasonLabel: "Nội dung ý kiến:",
                content: opinion,
                icon: "💬",
                headerBg: "#F5F3FF",
                headerBorder: "#DDD6FE",
                headerFg: "#6D28D9"
            );
            await dialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 3: 🔄 Yêu cầu đính chính
    /// </summary>
    private async void OnRequestCorrectionClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report != null && ViewModel != null)
        {
            if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
            {
                var (success, msg) = await _reportService.RequestCorrectionAsync(ViewModel.Session, report, "");
                if (!success)
                {
                    ViewModel.StatusMessage = $"Lỗi gửi yêu cầu đính chính: {msg}";
                    var errorDialog = new Views.RejectionReasonDialog(
                        report,
                        title: "Lỗi gửi yêu cầu đính chính",
                        subTitle: "Không thể hoàn tất gửi yêu cầu lên hệ thống IOC",
                        reasonLabel: "Chi tiết lỗi:",
                        content: msg,
                        icon: "⚠️",
                        headerBg: "#FEF2F2",
                        headerBorder: "#FECACA",
                        headerFg: "#DC2626"
                    );
                    await errorDialog.ShowDialog(this);
                    return;
                }
            }

            report.CorrectionReq = 1;
            report.StateId = 6;
            report.Status = "6";
            report.StatusName = "Báo cáo cần đính chính";

            ViewModel.StatusMessage = $"Đã gửi yêu cầu đính chính cho biểu mẫu \"{report.ObjName}\" thành công!";
            ViewModel.ApplyFilter();

            var dialog = new Views.RejectionReasonDialog(
                report,
                title: "Yêu cầu đính chính thành công",
                subTitle: "Gửi yêu cầu xin đính chính số liệu lên đơn vị quản lý",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Đã gửi thành công yêu cầu đính chính số liệu cho biểu mẫu \"{report.ObjName}\" ({report.TimeName}) lên hệ thống IOC Tỉnh.\nĐơn vị quản lý cấp trên sẽ xem xét và mở lại quyền sửa đổi số liệu cho biểu mẫu.",
                icon: "🔄",
                headerBg: "#FFF7ED",
                headerBorder: "#FED7AA",
                headerFg: "#EA580C"
            );
            await dialog.ShowDialog(this);
        }
    }

    private async void OnEditSingleReportClick(object? sender, RoutedEventArgs e)
    {
        OnEditReportClick(sender, e);
    }

    /// <summary>
    /// Mục 4: ✅ Phê duyệt báo cáo (chuyển sang Báo cáo đã được duyệt, State = 4)
    /// </summary>
    private async void OnApproveReportClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report != null && ViewModel != null)
        {
            if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
            {
                var (success, msg) = await _reportService.ApproveReportAsync(ViewModel.Session, report);
                if (!success)
                {
                    var errDialog = new Views.RejectionReasonDialog(
                        report,
                        title: "Lỗi phê duyệt báo cáo",
                        subTitle: "Không thể hoàn tất phê duyệt trên hệ thống IOC",
                        reasonLabel: "Chi tiết lỗi:",
                        content: msg,
                        icon: "⚠️",
                        headerBg: "#FEF2F2",
                        headerBorder: "#FECACA",
                        headerFg: "#DC2626"
                    );
                    await errDialog.ShowDialog(this);
                    return;
                }
            }

            // Cập nhật trạng thái thành Đã duyệt (STATE_ID = 4)
            report.StateId = 4;
            report.Status = "4";
            report.StatusName = "Báo cáo đã được duyệt";
            ViewModel.StatusMessage = $"Đã phê duyệt báo cáo \"{report.ObjName}\" thành công!";
            ViewModel.ApplyFilter();

            var dialog = new Views.RejectionReasonDialog(
                report,
                title: "Phê duyệt báo cáo thành công",
                subTitle: "Báo cáo đã được chuyển sang trạng thái \"Báo cáo đã được duyệt\"",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Biểu mẫu \"{report.ObjName}\" kỳ {report.TimeName} của đơn vị {report.OrgName} đã được phê duyệt thành công trên hệ thống IOC.",
                icon: "✅",
                headerBg: "#DCFCE7",
                headerBorder: "#86EFAC",
                headerFg: "#15803D"
            );
            await dialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 4: 📝 Duyệt yêu cầu đính chính
    /// Mở hộp thoại với 2 phương án:
    /// 1. Đồng ý: Trả lại cho đơn vị giao (State = 6), báo cáo biến mất khỏi danh sách ngay lập tức.
    /// 2. Không đồng ý: Giữ nguyên trạng thái (State = 4), xóa cờ yêu cầu đính chính, nút yêu cầu đính chính không còn nữa.
    /// </summary>
    private async void OnApproveCorrectionClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ??
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report == null || ViewModel == null) return;

        var dialog = new Views.ApproveCorrectionDialog(report);
        await dialog.ShowDialog(this);

        if (dialog.Decision == Views.CorrectionDecision.Cancel)
        {
            return;
        }

        if (dialog.Decision == Views.CorrectionDecision.Agree)
        {
            // PHƯƠNG ÁN 1: ĐỒNG Ý -> Trả lại cho đơn vị giao
            if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
            {
                var (success, msg) = await _reportService.ApproveCorrectionRequestAsync(
                    ViewModel.Session, report, dialog.ResponseNote);
                if (!success)
                {
                    var errDialog = new Views.RejectionReasonDialog(
                        report,
                        title: "Lỗi duyệt đính chính",
                        subTitle: "Không thể hoàn tất duyệt yêu cầu đính chính",
                        reasonLabel: "Chi tiết lỗi:",
                        content: msg,
                        icon: "⚠️",
                        headerBg: "#FEF2F2",
                        headerBorder: "#FECACA",
                        headerFg: "#DC2626"
                    );
                    await errDialog.ShowDialog(this);
                    return;
                }
            }

            report.StateId = 6;
            report.Status = "6";
            report.StatusName = "Báo cáo cần đính chính";
            report.CorrectionReq = 0;
            report.NotifyStatusChanged();

            // Xóa báo cáo khỏi danh sách duyệt và áp dụng bộ lọc -> Biến mất khỏi danh sách ngay lập tức
            ViewModel.RemoveReport(report);
            ViewModel.ApplyFilter();

            ViewModel.StatusMessage = $"Đã duyệt đính chính và trả lại báo cáo \"{report.ObjName}\" cho đơn vị thành công!";

            var successDialog = new Views.RejectionReasonDialog(
                report,
                title: "Duyệt yêu cầu đính chính thành công",
                subTitle: "Báo cáo đã chuyển sang trạng thái \"Báo cáo cần đính chính\"",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Đã duyệt yêu cầu xin đính chính số liệu cho biểu mẫu \"{report.ObjName}\" kỳ {report.TimeName} của đơn vị {report.OrgName}.\nBáo cáo đã được trả lại cho đơn vị để chỉnh sửa số liệu và không còn nằm trong danh sách cần duyệt.",
                icon: "🔄",
                headerBg: "#FFF7ED",
                headerBorder: "#FED7AA",
                headerFg: "#EA580C"
            );
            await successDialog.ShowDialog(this);
        }
        else if (dialog.Decision == Views.CorrectionDecision.Disagree)
        {
            // PHƯƠNG ÁN 2: KHÔNG ĐỒNG Ý -> Báo cáo vẫn giữ nguyên trạng thái, nút yêu cầu đính chính không còn nữa
            if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
            {
                var (success, msg) = await _reportService.RejectCorrectionRequestAsync(
                    ViewModel.Session, report, dialog.ResponseNote);
                if (!success)
                {
                    var errDialog = new Views.RejectionReasonDialog(
                        report,
                        title: "Lỗi từ chối đính chính",
                        subTitle: "Không thể hoàn tất từ chối yêu cầu đính chính trên hệ thống",
                        reasonLabel: "Chi tiết lỗi:",
                        content: msg,
                        icon: "⚠️",
                        headerBg: "#FEF2F2",
                        headerBorder: "#FECACA",
                        headerFg: "#DC2626"
                    );
                    await errDialog.ShowDialog(this);
                    return;
                }
            }

            // Báo cáo vẫn giữ nguyên trạng thái (StateId = 4: Báo cáo đã được duyệt)
            report.StateId = 4;
            report.Status = "4";
            report.StatusName = "Báo cáo đã được duyệt";
            // Xóa cờ yêu cầu đính chính -> nút yêu cầu đính chính lập tức biến mất
            report.CorrectionReq = 0;
            report.NotifyStatusChanged();

            // Cập nhật số lượng thông báo trên badge
            ViewModel.NotifyCountersChanged();
            ViewModel.StatusMessage = $"Đã từ chối yêu cầu đính chính. Báo cáo \"{report.ObjName}\" giữ nguyên trạng thái đã duyệt.";

            var noticeDialog = new Views.RejectionReasonDialog(
                report,
                title: "Từ chối yêu cầu đính chính",
                subTitle: "Báo cáo vẫn giữ nguyên trạng thái đã duyệt",
                reasonLabel: "Thông báo chi tiết:",
                content: $"Đã từ chối yêu cầu xin đính chính số liệu cho biểu mẫu \"{report.ObjName}\" kỳ {report.TimeName}.\nBáo cáo tiếp tục giữ nguyên trạng thái \"Báo cáo đã được duyệt\" và nút yêu cầu đính chính đã được gỡ bỏ.",
                icon: "ℹ️",
                headerBg: "#EFF6FF",
                headerBorder: "#BFDBFE",
                headerFg: "#1E40AF"
            );
            await noticeDialog.ShowDialog(this);
        }
    }

    /// <summary>
    /// Mục 4: 🚫 Từ chối báo cáo trong màn hình Duyệt báo cáo
    /// </summary>
    private async void OnApprovalRejectClick(object? sender, RoutedEventArgs e)
    {
        var report = (sender as Button)?.CommandParameter as ReportItem ?? 
                     (sender as Button)?.DataContext as ReportItem;

        if (report != null)
        {
            var grid = this.FindControl<DataGrid>("ReportsDataGrid");
            if (grid != null) grid.SelectedItem = report;
        }

        if (report != null && ViewModel != null)
        {
            var rejectDialog = new Views.RejectReportDialog(report);
            await rejectDialog.ShowDialog(this);

            if (rejectDialog.IsConfirmed)
            {
                if (ViewModel.Session != null && ViewModel.Session.IsAuthenticated)
                {
                    var (success, msg) = await _reportService.RejectApprovalReportAsync(ViewModel.Session, report, rejectDialog.RejectionReason);
                    if (!success)
                    {
                        var errDialog = new Views.RejectionReasonDialog(
                            report,
                            title: "Lỗi từ chối báo cáo",
                            subTitle: "Không thể hoàn tất thao tác từ chối trên hệ thống IOC",
                            reasonLabel: "Chi tiết lỗi:",
                            content: msg,
                            icon: "⚠️",
                            headerBg: "#FEF2F2",
                            headerBorder: "#FECACA",
                            headerFg: "#DC2626"
                        );
                        await errDialog.ShowDialog(this);
                        return;
                    }
                }

                report.StateId = 8;
                report.Status = "8";
                report.StatusName = "Báo cáo bị từ chối cấp đơn vị";
                report.Note = rejectDialog.RejectionReason;

                ViewModel.StatusMessage = $"Đã từ chối báo cáo \"{report.ObjName}\" thành công!";
                ViewModel.ApplyFilter();

                var successDialog = new Views.RejectionReasonDialog(
                    report,
                    title: "Từ chối báo cáo thành công",
                    subTitle: "Báo cáo đã chuyển sang trạng thái bị từ chối và trả lại cho đơn vị",
                    reasonLabel: "Lý do từ chối đã gửi:",
                    content: !string.IsNullOrWhiteSpace(rejectDialog.RejectionReason) ? rejectDialog.RejectionReason : "Báo cáo đã bị từ chối và yêu cầu đơn vị chỉnh sửa lại số liệu.",
                    icon: "🚫",
                    headerBg: "#FEF2F2",
                    headerBorder: "#FECACA",
                    headerFg: "#DC2626"
                );
                await successDialog.ShowDialog(this);
            }
        }
    }
}
