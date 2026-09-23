using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VSR.Helpers;
using VSR.Models;
using VSR.ViewModels;

namespace VSR.Views;

public partial class ReportEditWindow : Window
{
    private ReportEditViewModel? ViewModel => DataContext as ReportEditViewModel;
    private string _activeFldCode = string.Empty;
    private readonly Dictionary<(IndicatorItem, string), TextBox> _activeCellBoxes = new();
    private readonly Dictionary<(IndicatorItem Item, string Field), string> _excelDifferences = new();
    private bool _isComparingExcel;
    // Avalonia DataGrid chỉ hỗ trợ chọn theo hàng. Các trường dưới đây bổ sung vùng
    // chọn theo ô, tương tự Excel: điểm bắt đầu + điểm cuối tạo thành hình chữ nhật.
    private (int Row, int Column)? _selectionAnchor;
    private (int Row, int Column)? _selectionEnd;
    private (int Row, int Column)? _pendingSelectionAnchor;
    private Point _pointerPressPosition;
    private bool _isSelectingCells;

    // ── Zoom state ──────────────────────────────────────────────────────────
    private const double ZoomMin  = 0.50;
    private const double ZoomMax  = 2.00;
    private const double ZoomStep = 0.10;
    private double _zoomLevel = 1.0;
    private bool _zoomSliderUpdating = false; // guard to avoid circular update
    private readonly ScaleTransform _gridScale = new ScaleTransform(1, 1);

    public void FocusCell(IndicatorItem targetItem, string colKey)
    {
        if (targetItem == null || ViewModel == null) return;
        _activeFldCode = colKey;
        IndicatorsGrid.SelectedItem = targetItem;
        IndicatorsGrid.ScrollIntoView(targetItem, null);

        Dispatcher.UIThread.Post(() =>
        {
            if (_activeCellBoxes.TryGetValue((targetItem, colKey), out var tb))
            {
                tb.Focus();
                tb.SelectAll();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_activeCellBoxes.TryGetValue((targetItem, colKey), out var tb2))
                    {
                        tb2.Focus();
                        tb2.SelectAll();
                    }
                    else
                    {
                        var foundTb = IndicatorsGrid.GetVisualDescendants()
                            .OfType<TextBox>()
                            .FirstOrDefault(t => t.DataContext == targetItem);
                        if (foundTb != null)
                        {
                            foundTb.Focus();
                            foundTb.SelectAll();
                        }
                    }
                }, DispatcherPriority.Loaded);
            }
        }, DispatcherPriority.Render);
    }

    public void NavigateCell(IndicatorItem currentItem, string currentColKey, int rowDelta, int colDelta)
    {
        if (ViewModel == null) return;
        var list = ViewModel.FilteredIndicators;
        int currentIndex = list.IndexOf(currentItem);
        if (currentIndex < 0) return;

        var headerList = ViewModel.DynamicHeaders;
        int colIndex = 0;
        for (int i = 0; i < headerList.Count; i++)
        {
            if (headerList[i].FldCode == currentColKey)
            {
                colIndex = i;
                break;
            }
        }

        int newColIndex = colIndex + colDelta;
        int targetRowIndex = currentIndex;

        if (rowDelta != 0)
        {
            int step = rowDelta > 0 ? 1 : -1;
            int checkIndex = currentIndex + step;
            while (checkIndex >= 0 && checkIndex < list.Count)
            {
                if (list[checkIndex].IsEditable)
                {
                    targetRowIndex = checkIndex;
                    break;
                }
                checkIndex += step;
            }
        }
        else if (colDelta != 0)
        {
            if (newColIndex >= 0 && newColIndex < headerList.Count)
            {
                // Di chuyển trong cùng một hàng
            }
            else if (newColIndex >= headerList.Count)
            {
                // Xuống hàng kế tiếp, cột đầu tiên
                newColIndex = 0;
                int checkIndex = currentIndex + 1;
                while (checkIndex >= 0 && checkIndex < list.Count)
                {
                    if (list[checkIndex].IsEditable)
                    {
                        targetRowIndex = checkIndex;
                        break;
                    }
                    checkIndex++;
                }
            }
            else if (newColIndex < 0)
            {
                // Lên hàng trước, cột cuối cùng
                newColIndex = Math.Max(0, headerList.Count - 1);
                int checkIndex = currentIndex - 1;
                while (checkIndex >= 0 && checkIndex < list.Count)
                {
                    if (list[checkIndex].IsEditable)
                    {
                        targetRowIndex = checkIndex;
                        break;
                    }
                    checkIndex--;
                }
            }
        }

        newColIndex = Math.Clamp(newColIndex, 0, Math.Max(0, headerList.Count - 1));
        var targetColKey = headerList.Count > newColIndex ? headerList[newColIndex].FldCode : currentColKey;
        var targetItem = list[targetRowIndex];

        FocusCell(targetItem, targetColKey);
    }

    public ReportEditWindow()
    {
        InitializeComponent();
        AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        // Bắt ở cửa sổ theo Tunnel để nhận cả thao tác bắt đầu trên TextBox ô số liệu.
        AddHandler(InputElement.PointerPressedEvent, OnCellRangePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerMovedEvent, OnIndicatorsGridPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerReleasedEvent, OnIndicatorsGridPointerReleased, RoutingStrategies.Tunnel);
        // Hook Ctrl+MouseWheel for zoom on the ScrollViewer
        GridScrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnGridPointerWheelChanged,
            RoutingStrategies.Tunnel);
        // Apply ScaleTransform to LayoutTransformControl wrapping the DataGrid
        GridZoomContainer.LayoutTransform = _gridScale;
    }

    private void OnCellRangePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var hit = this.InputHitTest(e.GetPosition(this)) as Visual;
        if (!TryGetCellCoordinates(hit, out var coordinates)) return;

        // Chỉ ghi nhận điểm bắt đầu. Click đơn thuần vẫn là thao tác DataGrid bình thường
        // và tuyệt đối không được xem là một vùng để copy.
        _pendingSelectionAnchor = coordinates;
        _pointerPressPosition = e.GetPosition(this);
        _selectionAnchor = null;
        _selectionEnd = null;
        _isSelectingCells = false;
        ApplyCellRangeVisuals();

        // Không cho DataGrid biến thao tác kéo thành chọn nhiều hàng. Với ô nhập,
        // chủ động giữ focus để người dùng vẫn có thể gõ sau khi click đơn.
        var textBox = hit as TextBox ?? hit?.FindAncestorOfType<TextBox>();
        if (textBox != null) textBox.Focus();
        e.Handled = true;
    }

    private void OnIndicatorsGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingSelectionAnchor is not { } pending || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!_isSelectingCells && Math.Abs(position.X - _pointerPressPosition.X) < 4 && Math.Abs(position.Y - _pointerPressPosition.Y) < 4)
            return;

        var hit = this.InputHitTest(position) as Visual;
        if (!TryGetCellCoordinates(hit, out var coordinates)) return;

        if (!_isSelectingCells)
        {
            _selectionAnchor = pending;
            _isSelectingCells = true;
        }
        if (coordinates == _selectionEnd) return;

        _selectionEnd = coordinates;
        ApplyCellRangeVisuals();
        // Khi đã kéo chọn vùng, không để TextBox chuyển thao tác này thành bôi chữ.
        e.Handled = true;
    }

    private void OnIndicatorsGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            _isSelectingCells = false;
            _pendingSelectionAnchor = null;
        }
    }

    private bool TryGetCellCoordinates(Visual? source, out (int Row, int Column) coordinates)
    {
        coordinates = default;
        var cell = source as DataGridCell ?? source?.FindAncestorOfType<DataGridCell>();
        if (ViewModel == null || cell == null ||
            cell.DataContext is not IndicatorItem item)
        {
            return false;
        }

        return TryGetCellCoordinates(item, GetColumnFromCell(cell), out coordinates);
    }

    private bool TryGetCellCoordinates(IndicatorItem item, DataGridColumn? dataGridColumn, out (int Row, int Column) coordinates)
    {
        coordinates = default;
        if (ViewModel == null) return false;

        var columnKey = GetColumnKeyFromColumn(dataGridColumn);
        var columns = GetSelectableColumnKeys();
        var row = ViewModel.FilteredIndicators.IndexOf(item);
        var column = columns.IndexOf(columnKey);
        if (row < 0 || column < 0)
        {
            return false; // Không chọn cột thao tác hoặc ô không phải dữ liệu.
        }

        coordinates = (row, column);
        return true;
    }

    private List<string> GetSelectableColumnKeys() => IndicatorsGrid.Columns
        .Where(column => column.IsVisible)
        .OrderBy(column => column.DisplayIndex)
        .Select(GetColumnKeyFromColumn)
        .Where(key => !string.IsNullOrEmpty(key))
        .ToList();

    /// <summary>
    /// DataGridCell của Avalonia 12 không công khai thuộc tính Column. Các cell trong
    /// DataGridCellsPresenter luôn được sắp theo DisplayIndex, nên ánh xạ bằng vị trí
    /// hiển thị để vẫn đúng cả khi người dùng đổi thứ tự cột.
    /// </summary>
    private DataGridColumn? GetColumnFromCell(DataGridCell cell)
    {
        // Thứ tự VisualChildren của Avalonia không ổn định khi hàng có TextBox và khi
        // DataGrid virtualize. Xác định theo vị trí màn hình thực tế của cell trước.
        var pointInGrid = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), IndicatorsGrid);
        if (pointInGrid is { } point)
        {
            double rightEdge = 0;
            foreach (var column in IndicatorsGrid.Columns.Where(column => column.IsVisible).OrderBy(column => column.DisplayIndex))
            {
                rightEdge += column.ActualWidth;
                if (point.X <= rightEdge)
                    return column;
            }
        }

        // Fallback cho cell chưa được bố trí xong trong lượt render đầu tiên.
        var presenter = cell.FindAncestorOfType<DataGridCellsPresenter>();
        if (presenter == null) return null;

        var cellIndex = presenter.GetVisualChildren().OfType<DataGridCell>().ToList().IndexOf(cell);
        if (cellIndex < 0) return null;

        return IndicatorsGrid.Columns
            .Where(column => column.IsVisible)
            .OrderBy(column => column.DisplayIndex)
            .ElementAtOrDefault(cellIndex);
    }

    private bool IsInSelectedCellRange(IndicatorItem item, DataGridColumn? column)
    {
        if (ViewModel == null || _selectionAnchor is not { } anchor || _selectionEnd is not { } end)
        {
            return false;
        }

        var row = ViewModel.FilteredIndicators.IndexOf(item);
        var col = GetSelectableColumnKeys().IndexOf(GetColumnKeyFromColumn(column));
        return row >= Math.Min(anchor.Row, end.Row) && row <= Math.Max(anchor.Row, end.Row) &&
               col >= Math.Min(anchor.Column, end.Column) && col <= Math.Max(anchor.Column, end.Column);
    }

    private void ApplyCellRangeVisuals()
    {
        foreach (var cell in IndicatorsGrid.GetVisualDescendants().OfType<DataGridCell>())
        {
            cell.Classes.Set("range-selected", cell.DataContext is IndicatorItem item && IsInSelectedCellRange(item, GetColumnFromCell(cell)));
        }
    }

    public ReportEditWindow(ReportItem report, AuthSession session) : this()
    {
        var vm = new ReportEditViewModel(report, session);
        DataContext = vm;
        vm.HeadersLoaded += OnHeadersLoaded;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ReportEditViewModel.IsActionColumnVisible))
            {
                if (IndicatorsGrid.Columns.Count > 0 && IndicatorsGrid.Columns[0] is DataGridColumn colAction)
                {
                    colAction.IsVisible = vm.IsActionColumnVisible;
                }
            }
            else if (e.PropertyName == nameof(ReportEditViewModel.IsSttColumnVisible))
            {
                if (IndicatorsGrid.Columns.Count > 1 && IndicatorsGrid.Columns[1] is DataGridColumn colStt)
                {
                    colStt.IsVisible = vm.IsSttColumnVisible;
                }
            }
            else if (e.PropertyName == nameof(ReportEditViewModel.IsCodeColumnVisible))
            {
                if (IndicatorsGrid.Columns.Count > 2 && IndicatorsGrid.Columns[2] is DataGridColumn colCode)
                {
                    colCode.IsVisible = vm.IsCodeColumnVisible;
                }
            }
            else if (e.PropertyName == nameof(ReportEditViewModel.IsUnitColumnVisible))
            {
                if (IndicatorsGrid.Columns.Count > 4 && IndicatorsGrid.Columns[4] is DataGridColumn colUnit)
                {
                    colUnit.IsVisible = vm.IsUnitColumnVisible;
                }
            }
        };

        Loaded += OnWindowLoaded;
    }

    // ── Zoom Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Áp dụng mức zoom mới vào DataGrid và đồng bộ UI slider / label.
    /// </summary>
    private void ApplyZoom(double level)
    {
        _zoomLevel = Math.Clamp(Math.Round(level, 2), ZoomMin, ZoomMax);
        _gridScale.ScaleX = _zoomLevel;
        _gridScale.ScaleY = _zoomLevel;

        int pct = (int)Math.Round(_zoomLevel * 100);
        ZoomLabel.Text = $"{pct}%";

        _zoomSliderUpdating = true;
        ZoomSlider.Value = pct;
        _zoomSliderUpdating = false;
    }

    private void OnGridPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Only intercept when Ctrl is held; otherwise let normal scroll happen
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        e.Handled = true;
        double delta = e.Delta.Y > 0 ? ZoomStep : -ZoomStep;
        ApplyZoom(_zoomLevel + delta);
    }

    private void OnZoomSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_zoomSliderUpdating) return;
        ApplyZoom(e.NewValue / 100.0);
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
        => ApplyZoom(_zoomLevel - ZoomStep);

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
        => ApplyZoom(_zoomLevel + ZoomStep);

    private void OnZoomResetClick(object? sender, RoutedEventArgs e)
        => ApplyZoom(1.0);

    private void OnHeadersLoaded()
    {
        Dispatcher.UIThread.Post(BuildDynamicColumns);
    }

    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.InitializeAsync();
        }
    }

    /// <summary>
    /// Tạo các cột số liệu động dựa trên cấu trúc API trả về từ FNC003_S315 / S312
    /// </summary>
    private void BuildDynamicColumns()
    {
        if (ViewModel == null) return;
        _activeCellBoxes.Clear();

        // Cập nhật ẩn/hiện và độ rộng các cột cơ bản theo cấu hình API
        // Cột 0: Thao tác | Cột 1: STT | Cột 2: Mã chỉ tiêu | Cột 3: Tên chỉ tiêu | Cột 4: Đơn vị tính
        if (IndicatorsGrid.Columns.Count > 0 && IndicatorsGrid.Columns[0] is DataGridColumn colAction)
        {
            colAction.IsVisible = ViewModel.IsActionColumnVisible;
            colAction.Width = new DataGridLength(76);
        }
        if (IndicatorsGrid.Columns.Count > 1 && IndicatorsGrid.Columns[1] is DataGridColumn colStt)
        {
            colStt.IsVisible = ViewModel.IsSttColumnVisible;
            colStt.Width = new DataGridLength(ViewModel.SttColumnWidth > 30 ? ViewModel.SttColumnWidth : 65);
        }
        if (IndicatorsGrid.Columns.Count > 2 && IndicatorsGrid.Columns[2] is DataGridColumn colCode)
        {
            colCode.IsVisible = ViewModel.IsCodeColumnVisible;
            colCode.Width = new DataGridLength(ViewModel.CodeColumnWidth > 30 ? ViewModel.CodeColumnWidth : 180);
        }
        if (IndicatorsGrid.Columns.Count > 3 && IndicatorsGrid.Columns[3] is DataGridColumn colIndName)
        {
            // Độ rộng cột fix theo đúng kích thước lấy ra từ API, không tự co giãn
            colIndName.Width = new DataGridLength(ViewModel.NameColumnWidth > 50 ? ViewModel.NameColumnWidth : 400);
        }
        if (IndicatorsGrid.Columns.Count > 4 && IndicatorsGrid.Columns[4] is DataGridColumn colUnit)
        {
            colUnit.IsVisible = ViewModel.IsUnitColumnVisible;
            colUnit.Width = new DataGridLength(ViewModel.UnitColumnWidth > 30 ? ViewModel.UnitColumnWidth : 110);
        }

        // Giữ lại 5 cột cơ bản cố định (Thao tác, STT, Mã chỉ tiêu, Tên chỉ tiêu, Đơn vị tính)
        while (IndicatorsGrid.Columns.Count > 5)
        {
            IndicatorsGrid.Columns.RemoveAt(IndicatorsGrid.Columns.Count - 1);
        }

        foreach (var col in ViewModel.DynamicHeaders)
        {
            var colKey = col.FldCode;
            var colName = col.HeaderName;

            var headerText = new TextBlock
            {
                Text = colName,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12
            };

            var column = new DataGridTemplateColumn
            {
                Header = headerText,
                Width = new DataGridLength(col.ColWidth > 60 ? col.ColWidth : (col.IsStringColumn ? 220 : 160)),
                MinWidth = 100,
                CanUserResize = true,
                CanUserSort = false,
                CellTemplate = new FuncDataTemplate<IndicatorItem>((_, namescope) =>
                {
                    var container = new Grid
                    {
                        Margin = new Thickness(2),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    var dashText = new TextBlock
                    {
                        Text = "-",
                        Foreground = new SolidColorBrush(Color.Parse("#64748B")),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeight.Normal,
                        IsVisible = false
                    };

                    var tb = new TextBox
                    {
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#1E40AF")),
                        Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
                        BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 4),
                        MinHeight = 30,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        TextWrapping = TextWrapping.Wrap,
                        AcceptsReturn = false,
                        Focusable = true,
                        IsHitTestVisible = true,
                        IsVisible = false,
                        Tag = colKey
                    };
                    ScrollViewer.SetVerticalScrollBarVisibility(tb, ScrollBarVisibility.Disabled);
                    ScrollViewer.SetHorizontalScrollBarVisibility(tb, ScrollBarVisibility.Disabled);

                    var characterCounter = new TextBlock
                    {
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 5, 3),
                        FontSize = 10,
                        FontWeight = FontWeight.SemiBold,
                        Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
                        Foreground = new SolidColorBrush(Color.Parse("#64748B")),
                        IsHitTestVisible = false,
                        IsVisible = false
                    };

                    void UpdateCharacterCounter()
                    {
                        if (!col.IsStringColumn || !tb.IsVisible)
                        {
                            characterCounter.IsVisible = false;
                            return;
                        }

                        var valueLength = (tb.Text ?? string.Empty).Length;
                        var isOverLimit = valueLength > col.MaxLength;
                        characterCounter.Text = $"{valueLength}/{col.MaxLength}";
                        characterCounter.Foreground = new SolidColorBrush(Color.Parse(isOverLimit ? "#DC2626" : "#64748B"));
                        tb.Foreground = new SolidColorBrush(Color.Parse(isOverLimit ? "#DC2626" : "#1E40AF"));
                        characterCounter.IsVisible = true;
                    }

                    container.Children.Add(dashText);
                    container.Children.Add(tb);
                    container.Children.Add(characterCounter);

                    bool isUpdatingFromDataContext = false;
                    IndicatorItem? currentItem = null;
                    string initialValueOnFocus = string.Empty;
                    bool isCellInEditMode = false;

                    void RegisterActiveBox(IndicatorItem? item)
                    {
                        if (item != null && item.IsEditable)
                        {
                            _activeCellBoxes[(item, colKey)] = tb;
                        }
                    }

                    void UnregisterActiveBox(IndicatorItem? item)
                    {
                        if (item != null)
                        {
                            _activeCellBoxes.Remove((item, colKey));
                        }
                    }

                    void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName == $"Col_{colKey}" || e.PropertyName == $"Conflict_{colKey}" || e.PropertyName == nameof(IndicatorItem.Value) || string.IsNullOrEmpty(e.PropertyName))
                        {
                            UpdateCell(currentItem);
                        }
                    }

                    void UpdateCell(IndicatorItem? item)
                    {
                        if (item == null)
                        {
                            dashText.IsVisible = false;
                            tb.IsVisible = false;
                            characterCounter.IsVisible = false;
                            if (!tb.IsFocused) initialValueOnFocus = string.Empty;
                            return;
                        }

                        var val = item.GetColumnValue(colKey) ?? string.Empty;
                        bool isNum = VietnameseNumberHelper.IsNumericValue(val);
                        var hasConflict = item.HasConflict(colKey);

                        if (!item.IsEditable)
                        {
                            dashText.IsVisible = true;
                            tb.IsVisible = false;
                            characterCounter.IsVisible = false;

                            if (item.IsHeader)
                            {
                                dashText.Text = "-";
                                dashText.FontWeight = FontWeight.Normal;
                                dashText.Foreground = new SolidColorBrush(Color.Parse("#64748B"));
                                dashText.HorizontalAlignment = HorizontalAlignment.Center;
                                dashText.Margin = new Thickness(0);
                            }
                            else if (!string.IsNullOrWhiteSpace(val) && val != "-")
                            {
                                dashText.Text = val;
                                dashText.FontWeight = FontWeight.SemiBold;
                                dashText.Foreground = new SolidColorBrush(Color.Parse("#1E40AF"));
                                dashText.HorizontalAlignment = isNum ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                                dashText.Margin = isNum ? new Thickness(0, 0, 8, 0) : new Thickness(8, 0, 0, 0);
                            }
                            else
                            {
                                dashText.Text = "";
                                dashText.IsVisible = false;
                            }
                            if (!tb.IsFocused) initialValueOnFocus = string.Empty;
                        }
                        else
                        {
                            dashText.IsVisible = false;
                            tb.IsVisible = true;
                            tb.HorizontalContentAlignment = isNum ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                            tb.BorderBrush = new SolidColorBrush(Color.Parse(hasConflict ? "#DC2626" : "#CBD5E1"));
                            tb.Background = new SolidColorBrush(Color.Parse(hasConflict ? "#FEF2F2" : "#FFFFFF"));
                            ToolTip.SetTip(tb, hasConflict
                                ? $"Xung đột dữ liệu. Dữ liệu hiện tại trên máy chủ: '{item.GetConflictingServerValue(colKey)}'. Nhấn vào ô để chọn giá trị cần giữ."
                                : null);

                            if (!tb.IsFocused)
                            {
                                isUpdatingFromDataContext = true;
                                tb.Text = val;
                                isUpdatingFromDataContext = false;
                                initialValueOnFocus = val;
                            }
                            else if (tb.Text != val)
                            {
                                // Cập nhật từ Undo/Redo khi ô đang focus
                                isUpdatingFromDataContext = true;
                                tb.Text = val;
                                isUpdatingFromDataContext = false;
                                initialValueOnFocus = val;
                                tb.SelectAll();
                            }
                            RegisterActiveBox(item);
                            UpdateCharacterCounter();
                        }
                    }

                    container.DataContextChanged += (s, e) =>
                    {
                        if (currentItem != null)
                        {
                            currentItem.PropertyChanged -= OnItemPropertyChanged;
                            UnregisterActiveBox(currentItem);
                        }
                        currentItem = container.DataContext as IndicatorItem;
                        if (currentItem != null)
                        {
                            currentItem.PropertyChanged += OnItemPropertyChanged;
                        }
                        UpdateCell(currentItem);
                    };

                    tb.GotFocus += (s, e) =>
                    {
                        _activeFldCode = colKey;
                        // Viền xanh đậm rõ nét khi đang chọn ô, giữ nguyên độ dày 1px để không làm dịch chuyển chữ
                        tb.BorderBrush = new SolidColorBrush(Color.Parse("#2563EB"));
                        tb.BorderThickness = new Thickness(1);
                        isCellInEditMode = false;

                        if (container.DataContext is IndicatorItem item)
                        {
                            if (item.HasConflict(colKey))
                            {
                                ShowConflictChoice(tb, item, colKey);
                                return;
                            }
                            initialValueOnFocus = item.GetColumnValue(colKey) ?? string.Empty;
                            RegisterActiveBox(item);

                            // Khi người dùng nhấn chuột vào để nhập: Dấu "." phân cách phần nghìn tự động biến mất (Tương tự Excel)
                            if (col.IsNumericColumn && !string.IsNullOrEmpty(initialValueOnFocus))
                            {
                                int maxDec = col.DecimalDigits >= 0 ? col.DecimalDigits : 4;
                                var unformatted = VietnameseNumberHelper.ToEditNumberString(initialValueOnFocus, maxDec);
                                if (tb.Text != unformatted)
                                {
                                    isUpdatingFromDataContext = true;
                                    tb.Text = unformatted;
                                    isUpdatingFromDataContext = false;
                                }
                            }
                        }
                        Dispatcher.UIThread.Post(() => tb.SelectAll());
                    };

                    void CommitValue()
                    {
                        if (container.DataContext is IndicatorItem item && item.IsEditable)
                        {
                            var oldVal = initialValueOnFocus;
                            var rawInput = tb.Text ?? "";
                            var formatted = string.IsNullOrWhiteSpace(rawInput) ? "" : VietnameseNumberHelper.FormatVietnameseNumber(rawInput);
                            item.SetColumnValue(colKey, formatted);
                            isUpdatingFromDataContext = true;
                            tb.Text = formatted;
                            isUpdatingFromDataContext = false;

                            if (oldVal != formatted)
                            {
                                ViewModel?.RecordSingleEdit(item, colKey, oldVal, formatted);
                                initialValueOnFocus = formatted;
                                ViewModel?.RecalculateRowHeight(item);
                            }

                            ViewModel?.RecalculateFormulas();
                        }
                    }

                    tb.LostFocus += (s, e) =>
                    {
                        // Khôi phục viền xám mặc định khi rời ô và định dạng lại số phân cách hàng nghìn
                        tb.BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1"));
                        tb.BorderThickness = new Thickness(1);
                        isCellInEditMode = false;

                        CommitValue();
                    };

                    tb.PropertyChanged += (s, e) =>
                    {
                        if (e.Property.Name == nameof(TextBox.Text))
                        {
                            UpdateCharacterCounter();
                            if (isUpdatingFromDataContext) return;

                            isCellInEditMode = true;
                            if (!col.IsStringColumn)
                            {
                                var raw = tb.Text ?? "";
                                string sanitized;
                                if (col.IsIntegerColumn)
                                {
                                    sanitized = VietnameseNumberHelper.SanitizeIntegerString(raw, maxDigits: 15);
                                }
                                else
                                {
                                    int maxDec = col.DecimalDigits >= 0 ? col.DecimalDigits : 4;
                                    sanitized = VietnameseNumberHelper.SanitizeRealString(raw, maxIntegerDigits: 15, maxDecimalDigits: maxDec);
                                }

                                if (sanitized != raw)
                                {
                                    isUpdatingFromDataContext = true;
                                    int oldCaret = tb.CaretIndex;
                                    tb.Text = sanitized;
                                    tb.CaretIndex = Math.Min(oldCaret, sanitized.Length);
                                    isUpdatingFromDataContext = false;
                                }
                            }
                        }
                    };

                    // Chặn triệt để ký tự chữ và ký tự không hợp lệ ở mức Tunneling event trước khi TextBox xử lý
                    tb.AddHandler(InputElement.TextInputEvent, (s, e) =>
                    {
                        isCellInEditMode = true;
                        if (col.IsStringColumn || string.IsNullOrEmpty(e.Text)) return;

                        var currentText = tb.Text ?? "";
                        int selStart = tb.SelectionStart;
                        int selEnd = tb.SelectionEnd;
                        int selMin = Math.Min(selStart, selEnd);
                        int selLen = Math.Abs(selEnd - selStart);

                        string newText;
                        if (selLen > 0 && selMin <= currentText.Length)
                        {
                            newText = currentText.Remove(selMin, Math.Min(selLen, currentText.Length - selMin)).Insert(selMin, e.Text);
                        }
                        else
                        {
                            int caret = Math.Clamp(tb.CaretIndex, 0, currentText.Length);
                            newText = currentText.Insert(caret, e.Text);
                        }

                        if (col.IsIntegerColumn)
                        {
                            // Số nguyên: Chỉ cho nhập số (Tối đa 15 ký tự)
                            if (!VietnameseNumberHelper.IsValidIntegerInput(newText, maxDigits: 15))
                            {
                                e.Handled = true;
                            }
                        }
                        else // Số thực
                        {
                            // Số thực: Chỉ cho nhập số và dấu "," (Tối đa 15 ký tự phần nguyên, phần thập phân tối đa = Cấu hình số thập phân)
                            int maxDec = col.DecimalDigits >= 0 ? col.DecimalDigits : 4;
                            if (!VietnameseNumberHelper.IsValidRealInput(newText, maxIntegerDigits: 15, maxDecimalDigits: maxDec))
                            {
                                e.Handled = true;
                            }
                        }
                    }, RoutingStrategies.Tunnel);

                    tb.AddHandler(InputElement.KeyDownEvent, (s, e) =>
                    {
                        if (col.IsNumericColumn && e.Key == Key.Space)
                        {
                            e.Handled = true;
                            return;
                        }

                        if (e.Key == Key.Enter)
                        {
                            e.Handled = true;
                            CommitValue();
                            isCellInEditMode = false;
                            if (container.DataContext is IndicatorItem item)
                            {
                                var delta = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1;
                                NavigateCell(item, colKey, delta, 0);
                            }
                        }
                        else if (e.Key == Key.Tab)
                        {
                            e.Handled = true;
                            CommitValue();
                            isCellInEditMode = false;
                            if (container.DataContext is IndicatorItem item)
                            {
                                var delta = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1;
                                NavigateCell(item, colKey, 0, delta);
                            }
                        }
                        else if (e.Key == Key.Up)
                        {
                            e.Handled = true;
                            CommitValue();
                            isCellInEditMode = false;
                            if (container.DataContext is IndicatorItem item)
                            {
                                NavigateCell(item, colKey, -1, 0);
                            }
                        }
                        else if (e.Key == Key.Down)
                        {
                            e.Handled = true;
                            CommitValue();
                            isCellInEditMode = false;
                            if (container.DataContext is IndicatorItem item)
                            {
                                NavigateCell(item, colKey, 1, 0);
                            }
                        }
                        else if (e.Key == Key.Left)
                        {
                            if (!isCellInEditMode || (tb.SelectionStart == 0 && tb.SelectionEnd == (tb.Text?.Length ?? 0)) || (tb.CaretIndex == 0 && tb.SelectionStart == tb.SelectionEnd))
                            {
                                e.Handled = true;
                                CommitValue();
                                isCellInEditMode = false;
                                if (container.DataContext is IndicatorItem item)
                                {
                                    NavigateCell(item, colKey, 0, -1);
                                }
                            }
                        }
                        else if (e.Key == Key.Right)
                        {
                            if (!isCellInEditMode || (tb.SelectionStart == 0 && tb.SelectionEnd == (tb.Text?.Length ?? 0)) || (tb.CaretIndex == (tb.Text?.Length ?? 0) && tb.SelectionStart == tb.SelectionEnd))
                            {
                                e.Handled = true;
                                CommitValue();
                                isCellInEditMode = false;
                                if (container.DataContext is IndicatorItem item)
                                {
                                    NavigateCell(item, colKey, 0, 1);
                                }
                            }
                        }
                        else if (e.Key == Key.Escape)
                        {
                            e.Handled = true;
                            isUpdatingFromDataContext = true;
                            tb.Text = initialValueOnFocus;
                            if (container.DataContext is IndicatorItem item)
                            {
                                item.SetColumnValue(colKey, initialValueOnFocus);
                            }
                            isUpdatingFromDataContext = false;
                            ViewModel?.RecalculateFormulas();
                            isCellInEditMode = false;
                            tb.SelectAll();
                        }
                        else if (e.Key == Key.F2)
                        {
                            e.Handled = true;
                            isCellInEditMode = true;
                            tb.SelectionStart = tb.Text?.Length ?? 0;
                            tb.SelectionEnd = tb.Text?.Length ?? 0;
                            tb.CaretIndex = tb.Text?.Length ?? 0;
                        }
                        else if (e.Key == Key.Delete || (e.Key == Key.Back && !isCellInEditMode && (tb.SelectionEnd - tb.SelectionStart >= (tb.Text?.Length ?? 0))))
                        {
                            e.Handled = true;
                            if (container.DataContext is IndicatorItem item && item.IsEditable)
                            {
                                var oldVal = initialValueOnFocus;
                                isUpdatingFromDataContext = true;
                                tb.Text = "";
                                item.SetColumnValue(colKey, "");
                                isUpdatingFromDataContext = false;

                                if (!string.IsNullOrEmpty(oldVal))
                                {
                                    ViewModel?.RecordSingleEdit(item, colKey, oldVal, "");
                                    initialValueOnFocus = "";
                                }
                                ViewModel?.RecalculateFormulas();
                                isCellInEditMode = false;
                                tb.SelectAll();
                            }
                        }
                        else if (e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) != 0)
                        {
                            e.Handled = true;
                            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                            {
                                if (ViewModel != null && ViewModel.Redo(out var affItem, out var affCol))
                                {
                                    if (affItem != null && !string.IsNullOrEmpty(affCol))
                                    {
                                        FocusCell(affItem, affCol);
                                    }
                                }
                            }
                            else
                            {
                                if (isCellInEditMode && tb.Text != initialValueOnFocus)
                                {
                                    isUpdatingFromDataContext = true;
                                    tb.Text = initialValueOnFocus;
                                    if (container.DataContext is IndicatorItem item)
                                    {
                                        item.SetColumnValue(colKey, initialValueOnFocus);
                                    }
                                    isUpdatingFromDataContext = false;
                                    ViewModel?.RecalculateFormulas();
                                    isCellInEditMode = false;
                                    tb.SelectAll();
                                }
                                else
                                {
                                    if (ViewModel != null && ViewModel.Undo(out var affItem, out var affCol))
                                    {
                                        if (affItem != null && !string.IsNullOrEmpty(affCol))
                                        {
                                            FocusCell(affItem, affCol);
                                        }
                                    }
                                }
                            }
                        }
                        else if (e.Key == Key.Y && (e.KeyModifiers & KeyModifiers.Control) != 0)
                        {
                            e.Handled = true;
                            if (ViewModel != null && ViewModel.Redo(out var affItem, out var affCol))
                            {
                                if (affItem != null && !string.IsNullOrEmpty(affCol))
                                {
                                    FocusCell(affItem, affCol);
                                }
                            }
                        }
                        else
                        {
                            if (!isCellInEditMode && e.Key != Key.LeftShift && e.Key != Key.RightShift && e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl && e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
                            {
                                isCellInEditMode = true;
                            }
                        }
                    }, RoutingStrategies.Tunnel);

                    tb.PointerPressed += (s, e) =>
                    {
                        _activeFldCode = colKey;
                        if (tb.IsFocused)
                        {
                            isCellInEditMode = true;
                        }
                        tb.Focus();
                    };

                    // Cập nhật giá trị ban đầu nếu DataContext đã có sẵn
                    currentItem = container.DataContext as IndicatorItem;
                    if (currentItem != null)
                    {
                        currentItem.PropertyChanged += OnItemPropertyChanged;
                    }
                    UpdateCell(currentItem);

                    return container;
                }, true)
            };

            IndicatorsGrid.Columns.Add(column);
        }
    }

    private DispatcherTimer? _toastTimer;

    public void ShowToast(string message, bool isError = false, string icon = "✓")
    {
        _toastTimer?.Stop();

        TxtToastMessage.Text = message;
        TxtToastIcon.Text = icon;
        ToastNotification.Background = isError
            ? new SolidColorBrush(Color.Parse("#DC2626"))
            : new SolidColorBrush(Color.Parse("#52C41A"));

        ToastNotification.IsVisible = true;

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.8)
        };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            ToastNotification.IsVisible = false;
        };
        _toastTimer.Start();
    }

    private async void OnGetPrePeriodDataClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.GetPrePeriodDataAsync();
            ShowToast("Đã lấy dữ liệu kỳ trước!", false, "✓");
        }
    }

    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var cleanName = SanitizeFileName(ViewModel.ObjName);
            var cleanPeriod = SanitizeFileName(ViewModel.TimeName);
            var defaultFileName = $"BaoCao_{cleanName}_{cleanPeriod}.xlsx";

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Xuất dữ liệu báo cáo ra file Excel",
                DefaultExtension = "xlsx",
                SuggestedFileName = defaultFileName,
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new("Excel Workbook (*.xlsx)")
                    {
                        Patterns = new[] { "*.xlsx" }
                    }
                }
            });

            if (file != null)
            {
                var filePath = file.Path.LocalPath;
                var (success, msg) = await ViewModel.ExportToExcelAsync(filePath);
                if (success)
                {
                    ShowToast("Xuất Excel thành công!", false, "📤");
                }
                else
                {
                    ShowToast(msg, true, "✕");
                }
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Lỗi xuất Excel: {ex.Message}", true, "✕");
        }
    }

    private async void OnImportExcelClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Chọn file Excel để nhập dữ liệu vào bảng",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Excel Files (*.xlsx, *.xls)")
                    {
                        Patterns = new[] { "*.xlsx", "*.xls" }
                    }
                }
            });

            if (files != null && files.Count > 0)
            {
                var filePath = files[0].Path.LocalPath;
                var (success, msg, matched) = await ViewModel.ImportFromExcelAsync(filePath);
                if (success)
                {
                    ShowToast($"Đã nhập {matched} chỉ tiêu từ Excel!", false, "📂");
                }
                else
                {
                    ShowToast(msg, true, "✕");
                }
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Lỗi nhập Excel: {ex.Message}", true, "✕");
        }
    }

    /// <summary>Chọn Excel để kiểm tra, tuyệt đối không gọi SetColumnValue hay thay đổi dữ liệu hiện tại.</summary>
    private async void OnCompareExcelClick(object? sender, RoutedEventArgs e)
    {
        if (_isComparingExcel)
        {
            ExitExcelComparison();
            return;
        }
        if (ViewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn file Excel để so sánh với báo cáo",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> { new("Excel Files (*.xlsx, *.xls)") { Patterns = new[] { "*.xlsx", "*.xls" } } }
        });
        if (files.Count == 0) return;

        try
        {
            // ViewModel và ObservableCollection thuộc UI thread. Không đưa phép so sánh
            // sang Task.Run vì ClosedXML có thể đọc được ở nền, nhưng việc duyệt dữ liệu
            // bảng sẽ gây InvalidOperationException do khác thread sở hữu.
            var differences = ReadExcelDifferencesInOrder(files[0].Path.LocalPath);
            _excelDifferences.Clear();
            foreach (var difference in differences)
                _excelDifferences[(difference.Item, difference.Field)] = difference.ExcelValue;

            _isComparingExcel = true;
            CompareExcelLabel.Text = "Tắt so sánh";
            CompareExcelIcon.Text = "✕";
            GetPrePeriodButton.IsEnabled = false;
            ImportExcelButton.IsEnabled = false;
            ExportExcelButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            ApplyExcelComparisonVisuals();
            ShowToast($"Đã so sánh Excel: phát hiện {_excelDifferences.Count} ô sai khác.", false, "🔎");
        }
        catch (Exception ex)
        {
            ShowToast($"Không thể đọc file Excel: {ex.Message}", true, "✕");
        }
    }

    private sealed record ExcelComparisonRow(string Stt, string Code, string Name, Dictionary<string, string> Values);
    private sealed record ExcelDifference(IndicatorItem Item, string Field, string ExcelValue);

    /// <summary>
    /// So sánh theo đúng thứ tự hiển thị từ trên xuống. File Excel có thể không có mã
    /// chỉ tiêu hoặc tiêu đề cột không hoàn toàn giống IOC, nên không ghép theo mã/tên.
    /// Cột số liệu đầu tiên ngay sau STT/Tên/ĐVT là cột D (hoặc E nếu có Mã), rồi tiếp
    /// tục tuần tự sang E/F...; ví dụ chỉ tiêu STT 1 được so với D3.
    /// </summary>
    private List<ExcelDifference> ReadExcelDifferencesInOrder(string filePath)
    {
        if (ViewModel == null || !System.IO.File.Exists(filePath))
            throw new InvalidOperationException("File Excel không tồn tại.");

        // Excel thường giữ khóa ghi khi người dùng đang mở file. Đọc bản đã lưu qua
        // FileShare.ReadWrite rồi nạp từ memory để vẫn so sánh được trong trường hợp đó.
        using var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        using var memoryStream = new System.IO.MemoryStream();
        fileStream.CopyTo(memoryStream);
        memoryStream.Position = 0;
        using var workbook = new XLWorkbook(memoryStream);
        var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidOperationException("File Excel rỗng.");
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0 || lastColumn == 0) throw new InvalidOperationException("File Excel rỗng.");

        var headerRow = 1;
        var sttColumn = 1;
        var nameColumn = 2;
        var unitColumn = 3;
        var codeColumn = 0;
        for (var row = 1; row <= Math.Min(25, lastRow); row++)
        {
            var foundStt = 0; var foundName = 0; var foundUnit = 0; var foundCode = 0;
            for (var column = 1; column <= lastColumn; column++)
            {
                var header = VietnameseNumberHelper.CleanSearchKey(sheet.Cell(row, column).GetString());
                if (header == "stt" || header == "tt" || header == "sothutu") foundStt = column;
                else if (header.Contains("tenchitieu") || header == "chitieu" || header == "ten" || header == "name") foundName = column;
                else if (header.Contains("donvitinh") || header == "dvt" || header == "donvi" || header == "unit") foundUnit = column;
                else if (header.Contains("machitieu") || header == "ma" || header == "code" || header == "indcode") foundCode = column;
            }
            if (foundName > 0 || foundStt > 0)
            {
                headerRow = row;
                sttColumn = foundStt > 0 ? foundStt : sttColumn;
                nameColumn = foundName > 0 ? foundName : nameColumn;
                unitColumn = foundUnit > 0 ? foundUnit : unitColumn;
                codeColumn = foundCode;
                break;
            }
        }

        var firstValueColumn = new[] { sttColumn, nameColumn, unitColumn, codeColumn }.Max() + 1;
        var fields = ViewModel.DynamicHeaders.Select(header => header.FldCode).ToList();
        if (fields.Count == 0) fields.Add("FN01");

        // Bỏ dòng nhóm/chú thích ở Excel: chúng không có số liệu ở bất kỳ cột giá trị nào.
        var excelDataRows = Enumerable.Range(headerRow + 1, Math.Max(0, lastRow - headerRow))
            .Where(row => Enumerable.Range(firstValueColumn, Math.Min(fields.Count, Math.Max(0, lastColumn - firstValueColumn + 1)))
                .Any(column => !string.IsNullOrWhiteSpace(ExcelCellValue(sheet.Cell(row, column)))))
            .ToList();

        // Hàng nhóm trong bảng IOC không có dữ liệu để nhập nên không tiêu tốn một dòng Excel.
        // Hàng có giá trị tự tính vẫn được đối chiếu để người dùng nhìn thấy mọi sai khác số liệu.
        var reportRows = ViewModel.AllIndicators
            .Where(item => !item.IsHeader && (item.IsEditable || fields.Any(field => !string.IsNullOrWhiteSpace(item.GetColumnValue(field)))))
            .ToList();

        var differences = new List<ExcelDifference>();
        for (var index = 0; index < Math.Min(reportRows.Count, excelDataRows.Count); index++)
        {
            var item = reportRows[index];
            var excelRow = excelDataRows[index];
            for (var fieldIndex = 0; fieldIndex < fields.Count && firstValueColumn + fieldIndex <= lastColumn; fieldIndex++)
            {
                var field = fields[fieldIndex];
                var excelValue = ExcelCellValue(sheet.Cell(excelRow, firstValueColumn + fieldIndex));
                if (!AreExcelValuesEqual(item.GetColumnValue(field), excelValue))
                    differences.Add(new ExcelDifference(item, field, excelValue));
            }
        }
        return differences;
    }

    private List<ExcelDifference> ReadExcelDifferences(string filePath)
    {
        if (ViewModel == null || !System.IO.File.Exists(filePath))
            throw new InvalidOperationException("File Excel không tồn tại.");

        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidOperationException("File Excel rỗng.");
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0 || lastColumn == 0) throw new InvalidOperationException("File Excel rỗng.");

        int headerRow = 1, sttColumn = 1, codeColumn = 2, nameColumn = 3, unitColumn = 4;
        for (var row = 1; row <= Math.Min(25, lastRow); row++)
        {
            var foundName = -1; var foundCode = -1; var foundStt = -1; var foundUnit = -1;
            for (var column = 1; column <= lastColumn; column++)
            {
                var header = VietnameseNumberHelper.CleanSearchKey(sheet.Cell(row, column).GetString());
                if (header.Contains("tenchitieu") || header == "chitieu" || header == "ten" || header == "name") foundName = column;
                else if (header.Contains("machitieu") || header == "ma" || header == "code" || header == "indcode") foundCode = column;
                else if (header == "stt" || header == "tt" || header == "sothutu") foundStt = column;
                else if (header.Contains("donvitinh") || header == "dvt" || header == "donvi" || header == "unit") foundUnit = column;
            }
            if (foundName >= 0 || foundCode >= 0)
            {
                headerRow = row; nameColumn = foundName; codeColumn = foundCode; sttColumn = foundStt; unitColumn = foundUnit;
                break;
            }
        }

        var valueColumns = new Dictionary<int, string>();
        var usedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= lastColumn; column++)
        {
            if (column == sttColumn || column == codeColumn || column == nameColumn || column == unitColumn) continue;
            var header = VietnameseNumberHelper.CleanSearchKey(sheet.Cell(headerRow, column).GetString());
            var field = ViewModel.DynamicHeaders.FirstOrDefault(h =>
                VietnameseNumberHelper.CleanSearchKey(h.HeaderName) == header ||
                VietnameseNumberHelper.CleanSearchKey(h.FldCode) == header)?.FldCode;
            if (!string.IsNullOrEmpty(field)) { valueColumns[column] = field; usedFields.Add(field); }
        }
        // File xuất bởi ứng dụng luôn đặt cột số liệu sau STT/Mã/Tên/ĐVT; khớp tuần tự nếu tiêu đề khác nhau.
        foreach (var field in ViewModel.DynamicHeaders.Select(h => h.FldCode).Where(f => !usedFields.Contains(f)))
        {
            var freeColumn = Enumerable.Range(1, lastColumn).FirstOrDefault(c => c != sttColumn && c != codeColumn && c != nameColumn && c != unitColumn && !valueColumns.ContainsKey(c));
            if (freeColumn > 0) valueColumns[freeColumn] = field;
        }

        var excelRows = new List<ExcelComparisonRow>();
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var values = valueColumns.ToDictionary(pair => pair.Value, pair => ExcelCellValue(sheet.Cell(row, pair.Key)), StringComparer.OrdinalIgnoreCase);
            var stt = sttColumn > 0 ? sheet.Cell(row, sttColumn).GetString().Trim() : string.Empty;
            var code = codeColumn > 0 ? sheet.Cell(row, codeColumn).GetString().Trim() : string.Empty;
            var name = nameColumn > 0 ? sheet.Cell(row, nameColumn).GetString().Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(stt) || !string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(name) || values.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                excelRows.Add(new ExcelComparisonRow(stt, code, name, values));
        }

        var result = new List<ExcelDifference>();
        foreach (var item in ViewModel.AllIndicators.Where(i => i.IsEditable))
        {
            var excel = excelRows.FirstOrDefault(row => !string.IsNullOrWhiteSpace(item.IndCode) && VietnameseNumberHelper.CleanCode(row.Code) == VietnameseNumberHelper.CleanCode(item.IndCode))
                ?? excelRows.FirstOrDefault(row => VietnameseNumberHelper.CleanSearchKey(row.Name) == VietnameseNumberHelper.CleanSearchKey(item.IndName))
                ?? excelRows.FirstOrDefault(row => VietnameseNumberHelper.CleanCode(row.Stt) == VietnameseNumberHelper.CleanCode(item.SttDisplay));
            if (excel == null) continue;
            foreach (var (field, excelValue) in excel.Values)
                if (!AreExcelValuesEqual(item.GetColumnValue(field), excelValue)) result.Add(new ExcelDifference(item, field, excelValue));
        }
        return result;
    }

    private static string ExcelCellValue(IXLCell cell) => cell.DataType == XLDataType.Number
        ? cell.GetDouble().ToString("0.###############", System.Globalization.CultureInfo.InvariantCulture)
        : cell.GetString().Trim();

    private static bool AreExcelValuesEqual(string? current, string? excel)
    {
        var left = VietnameseNumberHelper.ToStandardDecimalString(current ?? string.Empty);
        var right = VietnameseNumberHelper.ToStandardDecimalString(excel ?? string.Empty);
        if (decimal.TryParse(left, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var leftNumber) &&
            decimal.TryParse(right, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rightNumber)) return leftNumber == rightNumber;
        return string.Equals((current ?? string.Empty).Trim(), (excel ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyExcelComparisonVisuals()
    {
        // Chỉ TextBox của các cột số liệu động là ô được điền. Không tô DataGridCell
        // vì các cell đó cũng bao quanh STT, Tên chỉ tiêu và Đơn vị tính (chỉ để xem).
        foreach (var textBox in IndicatorsGrid.GetVisualDescendants().OfType<TextBox>())
        {
            if (textBox.DataContext is not IndicatorItem item || textBox.Tag is not string field) continue;
            if (_excelDifferences.TryGetValue((item, field), out var excelValue))
            {
                textBox.Background = new SolidColorBrush(Color.Parse("#FEE2E2"));
                textBox.BorderBrush = new SolidColorBrush(Color.Parse("#DC2626"));
                ToolTip.SetTip(textBox, $"Giá trị trong Excel: {excelValue}");
            }
        }
    }

    private void ExitExcelComparison()
    {
        _isComparingExcel = false;
        _excelDifferences.Clear();
        CompareExcelLabel.Text = "So sánh Excel";
        CompareExcelIcon.Text = "🔎";
        GetPrePeriodButton.IsEnabled = true;
        ImportExcelButton.IsEnabled = true;
        ExportExcelButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
        foreach (var textBox in IndicatorsGrid.GetVisualDescendants().OfType<TextBox>())
        {
            textBox.ClearValue(TextBox.BackgroundProperty);
            textBox.ClearValue(TextBox.BorderBrushProperty);
            ToolTip.SetTip(textBox, null);
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "BaoCao";
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return clean.Replace(" ", "_");
    }

    private async void OnCheckUnitsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        try
        {
            var units = await ViewModel.GetAssignedUnitsStatusAsync();
            var dialog = new CheckAssignedUnitsDialog(ViewModel.ObjName, ViewModel.TimeName, units);
            await dialog.ShowDialog(this);

            if (dialog.TriggerAggregation)
            {
                if (dialog.TriggerAggregationOnlyApproved)
                {
                    OnAggregateApprovedClick(sender, e);
                }
                else
                {
                    OnAggregateAllClick(sender, e);
                }
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Lỗi kiểm tra đơn vị: {ex.Message}", true, "✕");
        }
    }

    private async void OnAggregateApprovedClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var (success, msg) = await ViewModel.AggregateReportAsync(onlyApproved: true);
        if (success)
        {
            ShowToast("Tổng hợp báo cáo đã duyệt thành công!", false, "⚡");
        }
        else
        {
            var cleanMsg = !string.IsNullOrWhiteSpace(msg) ? msg.Replace("❌ ", "").Replace("Lỗi: ", "") : "Tổng hợp dữ liệu thất bại!";
            ShowToast(cleanMsg, true, "✕");
        }
    }

    private async void OnAggregateAllClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var (success, msg) = await ViewModel.AggregateReportAsync(onlyApproved: false);
        if (success)
        {
            ShowToast("Tổng hợp tất cả báo cáo thành công!", false, "⚡");
        }
        else
        {
            var cleanMsg = !string.IsNullOrWhiteSpace(msg) ? msg.Replace("❌ ", "").Replace("Lỗi: ", "") : "Tổng hợp dữ liệu thất bại!";
            ShowToast(cleanMsg, true, "✕");
        }
    }

    private async void OnSaveToServerClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            var success = await ViewModel.SaveToServerAsync();
            if (success)
            {
                ShowToast("Cập nhật thành công!", false, "✓");
            }
            else
            {
                var msg = !string.IsNullOrWhiteSpace(ViewModel.StatusMessage)
                    ? ViewModel.StatusMessage.Replace("❌ ", "").Replace("Lỗi: ", "")
                    : "Lưu dữ liệu thất bại!";
                ShowToast(msg, true, "✕");
            }
        }
    }

    private void ShowConflictChoice(Control target, IndicatorItem item, string colKey)
    {
        var serverValue = item.GetConflictingServerValue(colKey);
        var localValue = item.GetColumnValue(colKey) ?? string.Empty;
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12), Width = 310 };
        panel.Children.Add(new TextBlock
        {
            Text = "Dữ liệu đã được người khác thay đổi.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold
        });
        panel.Children.Add(new TextBlock { Text = $"Dữ liệu người khác: {serverValue}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"Dữ liệu của bạn: {localValue}", TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var keepServer = new Button { Content = "Lấy dữ liệu người khác", Classes = { "secondary" } };
        var keepLocal = new Button { Content = "Lấy dữ liệu của tôi", Classes = { "primary" } };
        actions.Children.Add(keepServer);
        actions.Children.Add(keepLocal);
        panel.Children.Add(actions);

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        keepServer.Click += (_, _) =>
        {
            item.ResolveConflictKeepingServer(colKey);
            flyout.Hide();
        };
        keepLocal.Click += (_, _) =>
        {
            item.ResolveConflictKeepingLocal(colKey);
            flyout.Hide();
        };
        flyout.ShowAt(target);
    }

    private async void OnSubmitToLeaderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var (filled, unfilled, total) = ViewModel.GetCellCounts();
        var dialog = new SubmitToLeaderDialog(ViewModel.Report, filled, unfilled);
        await dialog.ShowDialog(this);

        if (dialog.IsConfirmed)
        {
            // 1. Tự động lưu số liệu mới nhất lên máy chủ trước khi trình
            await ViewModel.SaveToServerAsync();

            // 2. Gửi lệnh Trình lãnh đạo lên API IOC (FNC010_P23, State = 2)
            var (success, msg) = await ViewModel.SubmitReportToLeaderAsync(dialog.OpinionText);

            if (success)
            {
                ViewModel.Report.SubmitOpinion = dialog.OpinionText;
                ViewModel.Report.Note = dialog.OpinionText;
                ViewModel.Report.StateId = 2;
                ViewModel.Report.Status = "2";
                ViewModel.Report.StatusName = "Đã trình lãnh đạo";
                ViewModel.Report.Category = ReportCategoryType.Submit;
                ViewModel.Report.NotifyStatusChanged();

                ShowToast("Trình lãnh đạo thành công!", false, "🚀");
                await Task.Delay(600);
                Close();
            }
            else
            {
                ShowToast($"Lỗi trình lãnh đạo: {msg}", true, "✕");
            }
        }
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        // Tải lại luôn trả bảng về trạng thái dữ liệu gốc, đồng thời thoát so sánh.
        if (_isComparingExcel) ExitExcelComparison();
        if (ViewModel != null)
        {
            await ViewModel.LoadIndicatorsAsync();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Xử lý phím tắt Ctrl+C (Sao chép sang Excel), Ctrl+V (Dán từ Excel), Ctrl+Z (Undo), Ctrl+Y (Redo)
    /// </summary>
    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                if (ViewModel != null && ViewModel.Redo(out var affItem, out var affCol))
                {
                    e.Handled = true;
                    if (affItem != null && !string.IsNullOrEmpty(affCol))
                    {
                        FocusCell(affItem, affCol);
                    }
                    return;
                }
            }
            else
            {
                if (ViewModel != null && ViewModel.Undo(out var affItem, out var affCol))
                {
                    e.Handled = true;
                    if (affItem != null && !string.IsNullOrEmpty(affCol))
                    {
                        FocusCell(affItem, affCol);
                    }
                    return;
                }
            }
        }
        else if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ViewModel != null && ViewModel.Redo(out var affItem, out var affCol))
            {
                e.Handled = true;
                if (affItem != null && !string.IsNullOrEmpty(affCol))
                {
                    FocusCell(affItem, affCol);
                }
                return;
            }
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var topLevel = GetTopLevel(this);
            var focused = topLevel?.FocusManager?.GetFocusedElement();

            // Nếu người dùng đang bôi đen một đoạn văn bản trong TextBox của 1 ô cụ thể: để TextBox tự copy text bình thường
            if (IndicatorsGrid.SelectedItems.Count <= 1 && focused is TextBox tb && !string.IsNullOrEmpty(tb.SelectedText) && tb.SelectedText.Length < (tb.Text?.Length ?? 0))
            {
                return;
            }

            // Ngược lại (chọn nhiều dòng hoặc toàn bộ ô/dòng): Copy toàn bộ dữ liệu các ô của các dòng được chọn để dán sang Excel
            e.Handled = true;
            await CopySelectedRowsToClipboardAsync();
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            try
            {
                var text = await TextCopy.ClipboardService.GetTextAsync();
                if (string.IsNullOrWhiteSpace(text)) return;

                IndicatorItem? targetItem = null;
                var topLevel = GetTopLevel(this);
                var focused = topLevel?.FocusManager?.GetFocusedElement();

                if (focused is TextBox tb && tb.DataContext is IndicatorItem indFromTb)
                {
                    targetItem = indFromTb;
                    if (tb.Tag is string tag && !string.IsNullOrEmpty(tag))
                    {
                        _activeFldCode = tag;
                    }
                }
                else if (IndicatorsGrid.SelectedItem is IndicatorItem selItem)
                {
                    targetItem = selItem;
                }

                if (string.IsNullOrEmpty(_activeFldCode) && IndicatorsGrid.CurrentColumn != null)
                {
                    _activeFldCode = GetColumnKeyFromColumn(IndicatorsGrid.CurrentColumn);
                }

                if (ViewModel != null)
                {
                    e.Handled = true;
                    ViewModel.PasteFromClipboard(text, targetItem, _activeFldCode);
                    ShowToast("📥", "Đã dán dữ liệu từ Clipboard vào bảng!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lỗi dán clipboard: {ex.Message}");
            }
        }
    }

    public void OnCellTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            if (tb.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                _activeFldCode = tag;
            }
            if (tb.DataContext is IndicatorItem item)
            {
                IndicatorsGrid.SelectedItem = item;
            }
            Dispatcher.UIThread.Post(() => tb.SelectAll());
        }
    }

    private string GetColumnKeyFromColumn(DataGridColumn? col)
    {
        if (col == null || ViewModel == null) return string.Empty;
        int idx = IndicatorsGrid.Columns.IndexOf(col);
        if (idx == 1) return "STT";
        if (idx == 2) return "IndCode";
        if (idx == 3) return "IndName";
        if (idx == 4) return "IndUnit";
        if (idx >= 5)
        {
            int dynIdx = idx - 5;
            if (dynIdx >= 0 && dynIdx < ViewModel.DynamicHeaders.Count)
            {
                return ViewModel.DynamicHeaders[dynIdx].FldCode;
            }
        }
        return string.Empty;
    }

    private async void OnCopySelectedRowsClick(object? sender, RoutedEventArgs e)
    {
        await CopySelectedRowsToClipboardAsync();
    }

    private async void OnPasteClipboardClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var text = await TextCopy.ClipboardService.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;

            IndicatorItem? targetItem = null;
            var topLevel = GetTopLevel(this);
            var focused = topLevel?.FocusManager?.GetFocusedElement();

            if (focused is TextBox tb && tb.DataContext is IndicatorItem indFromTb)
            {
                targetItem = indFromTb;
                if (tb.Tag is string tag && !string.IsNullOrEmpty(tag))
                {
                    _activeFldCode = tag;
                }
            }
            else if (IndicatorsGrid.SelectedItem is IndicatorItem selItem)
            {
                targetItem = selItem;
            }

            if (string.IsNullOrEmpty(_activeFldCode) && IndicatorsGrid.CurrentColumn != null)
            {
                _activeFldCode = GetColumnKeyFromColumn(IndicatorsGrid.CurrentColumn);
            }

            if (ViewModel != null)
            {
                ViewModel.PasteFromClipboard(text, targetItem, _activeFldCode);
                ShowToast("📥", "Đã dán dữ liệu từ Clipboard vào bảng!");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi dán clipboard: {ex.Message}");
        }
    }

    /// <summary>
    /// Sao chép toàn bộ dữ liệu các dòng được chọn ra Clipboard theo chuẩn TSV (Tab-separated) để dán trực tiếp vào Excel
    /// </summary>
    private async Task CopySelectedRowsToClipboardAsync()
    {
        if (ViewModel == null) return;

        // Ưu tiên vùng ô được kéo chọn. Giữ đúng hình chữ nhật để khi dán vào Excel
        // số hàng/cột và vị trí các ô trống không bị thay đổi.
        if (_selectionAnchor is { } anchor && _selectionEnd is { } end)
        {
            var columns = GetSelectableColumnKeys();
            var firstRow = Math.Min(anchor.Row, end.Row);
            var lastRow = Math.Max(anchor.Row, end.Row);
            var firstColumn = Math.Min(anchor.Column, end.Column);
            var lastColumn = Math.Max(anchor.Column, end.Column);

            if (firstRow >= 0 && lastRow < ViewModel.FilteredIndicators.Count &&
                firstColumn >= 0 && lastColumn < columns.Count)
            {
                var selectedText = new System.Text.StringBuilder();
                for (var rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
                {
                    var row = ViewModel.FilteredIndicators[rowIndex];
                    var values = new List<string>();
                    for (var columnIndex = firstColumn; columnIndex <= lastColumn; columnIndex++)
                    {
                        var value = row.GetColumnValue(columns[columnIndex]) ?? string.Empty;
                        values.Add(value.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " "));
                    }
                    selectedText.AppendLine(string.Join("\t", values));
                }

                await TextCopy.ClipboardService.SetTextAsync(selectedText.ToString());
                var cellCount = (lastRow - firstRow + 1) * (lastColumn - firstColumn + 1);
                ViewModel.StatusMessage = $"Đã sao chép {cellCount} ô dữ liệu vào Clipboard.";
                ShowToast("✓", $"Đã sao chép {cellCount} ô vào Clipboard!");
                return;
            }
        }

        var selectedRows = new List<IndicatorItem>();
        if (IndicatorsGrid.SelectedItems.Count > 0)
        {
            selectedRows = ViewModel.FilteredIndicators
                .Where(i => IndicatorsGrid.SelectedItems.Contains(i))
                .ToList();
        }

        if (selectedRows.Count == 0)
        {
            if (IndicatorsGrid.SelectedItem is IndicatorItem sel)
            {
                selectedRows.Add(sel);
            }
            else
            {
                var topLevel = GetTopLevel(this);
                var focused = topLevel?.FocusManager?.GetFocusedElement();
                if (focused is TextBox tb && tb.DataContext is IndicatorItem tbItem)
                {
                    selectedRows.Add(tbItem);
                }
            }
        }

        if (selectedRows.Count == 0) return;

        var visibleKeys = ViewModel.GetVisibleColumnKeys();
        var sb = new System.Text.StringBuilder();

        for (int r = 0; r < selectedRows.Count; r++)
        {
            var row = selectedRows[r];
            var rowValues = new List<string>();
            foreach (var key in visibleKeys)
            {
                var val = row.GetColumnValue(key);
                // Giữ text liền mạch khi dán vào Excel
                val = val?.Replace("\r\n", " ")?.Replace("\n", " ")?.Replace("\r", " ") ?? string.Empty;
                rowValues.Add(val);
            }
            sb.AppendLine(string.Join("\t", rowValues));
        }

        var tsv = sb.ToString();
        if (!string.IsNullOrEmpty(tsv))
        {
            await TextCopy.ClipboardService.SetTextAsync(tsv);
            ViewModel.StatusMessage = $"Đã sao chép {selectedRows.Count} dòng dữ liệu vào Clipboard (sẵn sàng dán vào Excel).";
            ShowToast("✓", $"Đã sao chép {selectedRows.Count} dòng vào Clipboard!");
        }
    }

    private void ShowToast(string icon, string message)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            TxtToastIcon.Text = icon;
            TxtToastMessage.Text = message;
            ToastNotification.IsVisible = true;
            await Task.Delay(2500);
            ToastNotification.IsVisible = false;
        });
    }

    private void OnAddSubIndicatorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is IndicatorItem parentItem && ViewModel != null)
        {
            ViewModel.AddSubIndicator(parentItem);
        }
    }

    private void OnDeleteSubIndicatorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is IndicatorItem subItem && ViewModel != null)
        {
            ViewModel.DeleteSubIndicator(subItem);
        }
    }
}
