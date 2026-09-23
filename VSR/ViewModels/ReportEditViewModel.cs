using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VSR.Helpers;
using VSR.Models;
using VSR.Services;

namespace VSR.ViewModels;

public class ReportEditViewModel : ViewModelBase
{
    private readonly ReportService _reportService;

    private bool _isLoading;
    private bool _isSaving;
    private string _statusMessage = string.Empty;
    private bool _hasError;
    private string _filterText = string.Empty;
    private string _topIndId = "5924953";
    private string _attrId = "953157";
    private string _submitType = "2";
    private readonly List<IndicatorItem> _deletedSubIndicators = new();

    public ReportItem Report { get; }
    public AuthSession Session { get; }

    public string WindowTitle => Report.IsAggregateCategory
        ? $"Tổng Hợp Báo Cáo - {Report.ObjName} ({Report.TimeName})"
        : $"Nhập Liệu Báo Cáo - {Report.ObjName} ({Report.TimeName})";

    public bool IsAggregateCategory => Report.IsAggregateCategory;
    public bool IsInputCategory => !Report.IsAggregateCategory;

    public string ReportTitle => $"{Report.ObjName} - {Report.TimeName}";
    public string ObjName => Report.ObjName;
    public string TimeName => Report.TimeName;
    public string TimeId => Report.TimeId;
    public string OrgName => !string.IsNullOrWhiteSpace(Report.ReportOrgName) ? Report.ReportOrgName : (!string.IsNullOrWhiteSpace(Report.OrgName) ? Report.OrgName : (!string.IsNullOrWhiteSpace(Session.UnitName) ? Session.UnitName : string.Empty));
    public string ReportOrgName => !string.IsNullOrWhiteSpace(Report.ReportOrgName) ? Report.ReportOrgName : OrgName;
    public string SenderOrgName => !string.IsNullOrWhiteSpace(Report.SenderOrgName) ? Report.SenderOrgName : (!string.IsNullOrWhiteSpace(Session.UnitName) ? Session.UnitName : string.Empty);
    public string PeriodTypeName => Report.PeriodTypeName;
    public string StatusName => Report.DisplayStatusName;
    public string StatusBadgeBackground => Report.StatusBadgeBackground;
    public string StatusBadgeForeground => Report.StatusBadgeForeground;

    public async Task<List<AssignedUnitStatus>> GetAssignedUnitsStatusAsync()
    {
        return await _reportService.GetAssignedUnitsStatusAsync(Session, Report);
    }

    public ObservableCollection<IndicatorItem> AllIndicators { get; } = new();
    public ObservableCollection<IndicatorItem> FilteredIndicators { get; } = new();
    public ObservableCollection<ColumnHeaderInfo> DynamicHeaders { get; } = new();

    public event Action? HeadersLoaded;

    private bool _isActionColumnVisible;
    public bool IsActionColumnVisible
    {
        get => _isActionColumnVisible;
        set => SetProperty(ref _isActionColumnVisible, value);
    }

    private bool _isUnitColumnVisible = true;
    public bool IsUnitColumnVisible
    {
        get => _isUnitColumnVisible;
        set => SetProperty(ref _isUnitColumnVisible, value);
    }

    private bool _isSttColumnVisible = true;
    public bool IsSttColumnVisible
    {
        get => _isSttColumnVisible;
        set => SetProperty(ref _isSttColumnVisible, value);
    }

    private bool _isCodeColumnVisible;
    public bool IsCodeColumnVisible
    {
        get => _isCodeColumnVisible;
        set => SetProperty(ref _isCodeColumnVisible, value);
    }

    private double _nameColumnWidth = 400;
    public double NameColumnWidth
    {
        get => _nameColumnWidth;
        set => SetProperty(ref _nameColumnWidth, value);
    }

    private double _sttColumnWidth = 65;
    public double SttColumnWidth
    {
        get => _sttColumnWidth;
        set => SetProperty(ref _sttColumnWidth, value);
    }

    private double _codeColumnWidth = 180;
    public double CodeColumnWidth
    {
        get => _codeColumnWidth;
        set => SetProperty(ref _codeColumnWidth, value);
    }

    private double _unitColumnWidth = 110;
    public double UnitColumnWidth
    {
        get => _unitColumnWidth;
        set => SetProperty(ref _unitColumnWidth, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    private bool _isAggregating;
    public bool IsAggregating
    {
        get => _isAggregating;
        set => SetProperty(ref _isAggregating, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ApplyFilter();
            }
        }
    }

    public int TotalCount => AllIndicators.Count;
    public int EditableCount => AllIndicators.Count(i => i.IsEditable);
    public int FilledCount => AllIndicators.Count(i => i.IsEditable && !string.IsNullOrWhiteSpace(i.Value));

    public class CellEditRecord
    {
        public IndicatorItem Item { get; set; } = null!;
        public string ColKey { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }

    public class UndoBatch
    {
        public List<CellEditRecord> Records { get; } = new();
    }

    private readonly Stack<UndoBatch> _undoHistory = new();
    private readonly Stack<UndoBatch> _redoHistory = new();

    public void RecordSingleEdit(IndicatorItem item, string colKey, string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        var batch = new UndoBatch();
        batch.Records.Add(new CellEditRecord
        {
            Item = item,
            ColKey = colKey,
            OldValue = oldValue ?? string.Empty,
            NewValue = newValue ?? string.Empty
        });
        _undoHistory.Push(batch);
        _redoHistory.Clear();
        NotifyStatsChanged();
    }

    public void RecordBatchEdit(UndoBatch batch)
    {
        if (batch.Records.Count == 0) return;
        _undoHistory.Push(batch);
        _redoHistory.Clear();
        NotifyStatsChanged();
    }

    public bool Undo(out IndicatorItem? affectedItem, out string? affectedColKey)
    {
        affectedItem = null;
        affectedColKey = null;
        if (_undoHistory.Count == 0) return false;
        var batch = _undoHistory.Pop();
        _redoHistory.Push(batch);

        foreach (var record in batch.Records)
        {
            record.Item.SetColumnValue(record.ColKey, record.OldValue);
            affectedItem = record.Item;
            affectedColKey = record.ColKey;
        }
        RecalculateFormulas();
        NotifyStatsChanged();
        return true;
    }

    public bool Undo() => Undo(out _, out _);

    public bool Redo(out IndicatorItem? affectedItem, out string? affectedColKey)
    {
        affectedItem = null;
        affectedColKey = null;
        if (_redoHistory.Count == 0) return false;
        var batch = _redoHistory.Pop();
        _undoHistory.Push(batch);

        foreach (var record in batch.Records)
        {
            record.Item.SetColumnValue(record.ColKey, record.NewValue);
            affectedItem = record.Item;
            affectedColKey = record.ColKey;
        }
        RecalculateFormulas();
        NotifyStatsChanged();
        return true;
    }

    public bool Redo() => Redo(out _, out _);

    public (int Filled, int Unfilled, int Total) GetCellCounts()
    {
        var editableIndicators = AllIndicators.Where(i => i.IsEditable).ToList();
        int colsCount = Math.Max(1, DynamicHeaders.Count);
        int totalCells = editableIndicators.Count * colsCount;
        int filledCells = 0;

        foreach (var ind in editableIndicators)
        {
            if (DynamicHeaders.Count > 0)
            {
                foreach (var h in DynamicHeaders)
                {
                    var val = ind.GetColumnValue(h.FldCode);
                    if (!string.IsNullOrWhiteSpace(val) && val != "-")
                    {
                        filledCells++;
                    }
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(ind.Value) && ind.Value != "-")
                {
                    filledCells++;
                }
            }
        }

        int unfilledCells = Math.Max(0, totalCells - filledCells);
        return (filledCells, unfilledCells, totalCells);
    }

    /// <summary>
    /// Trả về danh sách các key cột hiện đang hiển thị trên bảng (theo thứ tự: STT, Mã, Tên, ĐVT, cột động).
    /// Chỉ các cột không bị ẩn mới được đưa vào — đảm bảo Ctrl+C không sao chép dữ liệu cột ẩn.
    /// </summary>
    public List<string> GetVisibleColumnKeys()
    {
        var keys = new List<string>();

        // Cột tĩnh – thêm đúng thứ tự hiển thị, chỉ khi đang hiển thị
        if (IsSttColumnVisible)   keys.Add("STT");
        if (IsCodeColumnVisible)  keys.Add("IndCode");
        // Cột Tên chỉ tiêu luôn hiển thị (không có toggle ẩn)
        keys.Add("IndName");
        if (IsUnitColumnVisible)  keys.Add("IndUnit");

        // Các cột động: bỏ qua những cột bị ẩn theo cấu hình từ server
        foreach (var h in DynamicHeaders)
        {
            if (!h.IsHidden && h.IsVisible)
                keys.Add(h.FldCode);
        }

        return keys;
    }

    public ReportEditViewModel(ReportItem report, AuthSession session)
    {
        Report = report;
        Session = session;
        _reportService = new ReportService();
    }

    public async Task InitializeAsync()
    {
        await LoadIndicatorsAsync();
    }

    public async Task LoadIndicatorsAsync()
    {
        IsLoading = true;
        HasError = false;
        StatusMessage = "Đang tải cấu trúc cột và danh sách chỉ tiêu từ IOC Hà Tĩnh (FNC003_S315 / P105)...";

        var (indicators, headers, attrId, topIndId, submitType, isUnitVisible, isSttVisible, isCodeVisible, nameWidth, sttWidth, codeWidth, unitWidth, error) = await _reportService.GetReportIndicatorsAsync(Session, Report);

        IsLoading = false;

        if (string.IsNullOrEmpty(error))
        {
            _attrId = attrId;
            _topIndId = topIndId;
            _submitType = submitType;
            IsUnitColumnVisible = isUnitVisible;
            IsSttColumnVisible = isSttVisible;
            IsCodeColumnVisible = isCodeVisible;
            NameColumnWidth = nameWidth;
            SttColumnWidth = sttWidth;
            CodeColumnWidth = codeWidth;
            UnitColumnWidth = unitWidth;

            DynamicHeaders.Clear();
            foreach (var h in headers)
            {
                DynamicHeaders.Add(h);
            }

            AllIndicators.Clear();
            _deletedSubIndicators.Clear();
            foreach (var item in indicators)
            {
                item.CaptureServerSnapshot(headers.Select(header => header.FldCode));
                AllIndicators.Add(item);
            }

            RecalculateAllRowHeights();
            IsActionColumnVisible = AllIndicators.Any(i => i.AllowAddRow || i.IsSubInd);

            StatusMessage = string.Empty;
            RecalculateFormulas();
            ApplyFilter();
            NotifyStatsChanged();
            HeadersLoaded?.Invoke();
        }
        else
        {
            HasError = true;
            StatusMessage = $"Lỗi: {error}";
        }
    }

    /// <summary>
    /// Lấy chỉ tiêu và dữ liệu từ kỳ trước (FNC003_P105 của pre_time_id)
    /// </summary>
    public async Task GetPrePeriodDataAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        HasError = false;
        StatusMessage = "Đang lấy dữ liệu và chỉ tiêu từ kỳ trước...";

        var (preIndicators, preHeaders, preTimeId, error) = await _reportService.GetPrePeriodIndicatorsAsync(Session, Report, _attrId, _topIndId, _submitType);

        IsLoading = false;

        if (string.IsNullOrEmpty(error) && preIndicators.Count > 0)
        {
            int matched = 0;

            // Match by IndCode first, fallback to IndId, then IndName + IndUnit
            foreach (var ind in AllIndicators.Where(i => i.IsEditable))
            {
                var pre = preIndicators.FirstOrDefault(p => p.IndCode == ind.IndCode && !string.IsNullOrWhiteSpace(p.IndCode)) ??
                          preIndicators.FirstOrDefault(p => p.IndId == ind.IndId && !string.IsNullOrWhiteSpace(p.IndId)) ??
                          preIndicators.FirstOrDefault(p => CleanString(p.IndName) == CleanString(ind.IndName) &&
                                                           CleanString(p.IndUnit) == CleanString(ind.IndUnit)) ??
                          preIndicators.FirstOrDefault(p => CleanString(p.IndName) == CleanString(ind.IndName));

                if (pre != null)
                {
                    bool indMatched = false;
                    foreach (var col in DynamicHeaders)
                    {
                        var v = pre.GetColumnValue(col.FldCode);
                        if (!string.IsNullOrWhiteSpace(v) && v != "-")
                        {
                            ind.SetColumnValue(col.FldCode, VietnameseNumberHelper.FormatVietnameseNumber(v));
                            indMatched = true;
                        }
                    }
                    if (indMatched) matched++;
                }
            }

            RecalculateAllRowHeights();
            RecalculateFormulas();
            HasError = false;
            StatusMessage = $"Đã lấy dữ liệu từ kỳ trước ({preTimeId}): Điền {matched}/{EditableCount} chỉ tiêu.";
            NotifyStatsChanged();
            ApplyFilter();
        }
        else
        {
            HasError = true;
            StatusMessage = !string.IsNullOrEmpty(error)
                ? $"Không lấy được dữ liệu kỳ trước ({preTimeId}): {error}"
                : $"Không tìm thấy dữ liệu của kỳ trước ({preTimeId}) trên hệ thống IOC.";
        }
    }

    /// <summary>
    /// Xử lý dán nhiều ô dữ liệu theo chiều dọc và chiều ngang như Excel (hỗ trợ cả cột Tên chỉ tiêu, STT, Mã, ĐVT và các cột số liệu)
    /// </summary>
    public void PasteFromClipboard(string clipboardText, IndicatorItem? startIndicator, string targetFldCode = "")
    {
        if (string.IsNullOrWhiteSpace(clipboardText)) return;

        var lines = clipboardText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (lines.Length > 1 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines = lines[..^1];
        }

        if (lines.Length == 0) return;

        int startIndex = 0;
        if (startIndicator != null)
        {
            var idx = FilteredIndicators.IndexOf(startIndicator);
            if (idx >= 0) startIndex = idx;
        }

        var visibleCols = GetVisibleColumnKeys();
        int startColIdx = 0;
        if (!string.IsNullOrWhiteSpace(targetFldCode))
        {
            int foundIdx = visibleCols.FindIndex(k => k.Equals(targetFldCode, StringComparison.OrdinalIgnoreCase));
            if (foundIdx >= 0)
            {
                startColIdx = foundIdx;
            }
            else
            {
                int dynIdx = DynamicHeaders.ToList().FindIndex(h => h.FldCode.Equals(targetFldCode, StringComparison.OrdinalIgnoreCase));
                if (dynIdx >= 0)
                {
                    int baseFixedCount = (IsSttColumnVisible ? 1 : 0) + (IsCodeColumnVisible ? 1 : 0) + 1 + (IsUnitColumnVisible ? 1 : 0);
                    startColIdx = baseFixedCount + dynIdx;
                }
            }
        }
        startColIdx = Math.Clamp(startColIdx, 0, Math.Max(0, visibleCols.Count - 1));

        int pastedCount = 0;
        var batch = new UndoBatch();

        for (int i = 0; i < lines.Length && (startIndex + i) < FilteredIndicators.Count; i++)
        {
            var rawLine = lines[i];
            var targetInd = FilteredIndicators[startIndex + i];

            if (rawLine.Contains('\t'))
            {
                var cols = rawLine.Split('\t');
                for (int c = 0; c < cols.Length && (startColIdx + c) < visibleCols.Count; c++)
                {
                    var colVal = cols[c];
                    var colKey = visibleCols[startColIdx + c];

                    bool isColEditable = false;
                    if (colKey is "STT" or "IndCode" or "IndName" or "IndUnit")
                    {
                        isColEditable = targetInd.IsSubInd;
                    }
                    else
                    {
                        isColEditable = targetInd.IsEditable;
                    }

                    if (isColEditable)
                    {
                        var oldVal = targetInd.GetColumnValue(colKey) ?? string.Empty;
                        string newVal;
                        if (colKey is "STT" or "IndCode" or "IndName" or "IndUnit")
                        {
                            newVal = colVal?.Trim() ?? string.Empty;
                        }
                        else
                        {
                            var header = DynamicHeaders.FirstOrDefault(h => h.FldCode.Equals(colKey, StringComparison.OrdinalIgnoreCase));
                            if (header != null && header.IsStringColumn)
                            {
                                newVal = colVal?.Trim() ?? string.Empty;
                            }
                            else
                            {
                                newVal = VietnameseNumberHelper.FormatVietnameseNumber(colVal);
                            }
                        }

                        if (oldVal != newVal)
                        {
                            targetInd.SetColumnValue(colKey, newVal);
                            batch.Records.Add(new CellEditRecord
                            {
                                Item = targetInd,
                                ColKey = colKey,
                                OldValue = oldVal,
                                NewValue = newVal
                            });
                        }
                        pastedCount++;
                    }
                }
            }
            else
            {
                var colKey = visibleCols.Count > startColIdx ? visibleCols[startColIdx] : (DynamicHeaders.FirstOrDefault()?.FldCode ?? "FN01");

                bool isColEditable = false;
                if (colKey is "STT" or "IndCode" or "IndName" or "IndUnit")
                {
                    isColEditable = targetInd.IsSubInd;
                }
                else
                {
                    isColEditable = targetInd.IsEditable;
                }

                if (isColEditable)
                {
                    var oldVal = targetInd.GetColumnValue(colKey) ?? string.Empty;
                    string newVal;
                    if (colKey is "STT" or "IndCode" or "IndName" or "IndUnit")
                    {
                        newVal = rawLine?.Trim() ?? string.Empty;
                    }
                    else
                    {
                        var header = DynamicHeaders.FirstOrDefault(h => h.FldCode.Equals(colKey, StringComparison.OrdinalIgnoreCase));
                        if (header != null && header.IsStringColumn)
                        {
                            newVal = rawLine?.Trim() ?? string.Empty;
                        }
                        else
                        {
                            newVal = VietnameseNumberHelper.FormatVietnameseNumber(rawLine);
                        }
                    }

                    if (oldVal != newVal)
                    {
                        targetInd.SetColumnValue(colKey, newVal);
                        batch.Records.Add(new CellEditRecord
                        {
                            Item = targetInd,
                            ColKey = colKey,
                            OldValue = oldVal,
                            NewValue = newVal
                        });
                    }
                    pastedCount++;
                }
            }
            // Dòng không cho phép nhập (nhóm chỉ tiêu / cha) vẫn tiêu thụ 1 dòng của Excel mà không ghi dữ liệu, đảm bảo kết quả 1-1
        }

        if (batch.Records.Count > 0)
        {
            RecordBatchEdit(batch);
        }

        RecalculateAllRowHeights();
        RecalculateFormulas();
        StatusMessage = $"Đã dán thành công {pastedCount} ô dữ liệu từ Excel.";
        NotifyStatsChanged();
    }

    /// <summary>
    /// Tính toán các chỉ tiêu CÓ CÔNG THỨC (FORMULA từ server) hoặc chỉ tiêu có loại tính toán đặc thù (IND_TYPE 3: SUM, 4: AVG, 5: MAX, 6: MIN)
    /// Tuyệt đối KHÔNG tự động cộng dồn/tính toán lên các chỉ tiêu/nhóm chỉ tiêu nếu server không khai báo FORMULA.
    /// </summary>
    public void RecalculateFormulas()
    {
        if (AllIndicators.Count == 0 || DynamicHeaders.Count == 0) return;

        var codeMap = new Dictionary<string, IndicatorItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var ind in AllIndicators)
        {
            if (!string.IsNullOrWhiteSpace(ind.IndCode))
                codeMap[ind.IndCode] = ind;
            if (!string.IsNullOrWhiteSpace(ind.IndId))
                codeMap[ind.IndId] = ind;
            if (!string.IsNullOrWhiteSpace(ind.FieldId))
                codeMap[ind.FieldId] = ind;
        }

        var childrenByParent = AllIndicators
            .Where(i => !string.IsNullOrWhiteSpace(i.ParentId))
            .GroupBy(i => i.ParentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Duyệt từ cấp sâu nhất lên (Level giảm dần) để chỉ tiêu con được tính trước chỉ tiêu cha
        var orderedIndicators = AllIndicators
            .OrderByDescending(i => i.Level)
            .ThenByDescending(i => i.Stt)
            .ToList();

        foreach (var col in DynamicHeaders)
        {
            var colKey = col.FldCode;

            foreach (var ind in orderedIndicators)
            {
                // Nhóm chỉ tiêu (Header category như "NHÓM THU NGÂN SÁCH"): Luôn để trống, không tính toán
                if (ind.IsHeader)
                {
                    ind.SetColumnValue(colKey, "");
                    continue;
                }

                // Trường hợp 1: Chỉ tiêu có công thức rõ ràng từ server (FORMULA, ví dụ {CTDB_IV_1_1_1}+{CTDB_IV_1_1_2})
                if (ind.HasFormula)
                {
                    var (calculatedVal, hasAnyInput) = EvaluateFormula(ind.Formula, codeMap, colKey);
                    if (hasAnyInput && calculatedVal.HasValue)
                    {
                        var formatted = VietnameseNumberHelper.FormatVietnameseDouble(calculatedVal.Value);
                        ind.SetColumnValue(colKey, formatted);
                    }
                    else
                    {
                        ind.SetColumnValue(colKey, "");
                    }
                    continue;
                }

                // Trường hợp 2: Chỉ khi IND_TYPE được server khai báo là loại tính toán (3: SUM, 4: AVG, 5: MAX, 6: MIN)
                if (ind.IndType == "3" || ind.IndType == "4" || ind.IndType == "5" || ind.IndType == "6")
                {
                    if (childrenByParent.TryGetValue(ind.IndId, out var children) && children.Count > 0)
                    {
                        var childValues = new List<double>();
                        int childWithValueCount = 0;

                        foreach (var child in children)
                        {
                            var raw = child.GetColumnValue(colKey);
                            if (!string.IsNullOrWhiteSpace(raw) && raw != "-")
                            {
                                var clean = VietnameseNumberHelper.ToStandardDecimalString(raw);
                                if (double.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                                {
                                    childValues.Add(d);
                                    childWithValueCount++;
                                }
                            }
                        }

                        if (childWithValueCount == 0)
                        {
                            ind.SetColumnValue(colKey, "");
                            continue;
                        }

                        double result = 0;
                        if (ind.IndType == "4") // AVERAGE
                        {
                            result = childValues.Average();
                        }
                        else if (ind.IndType == "5") // MAX
                        {
                            result = childValues.Max();
                        }
                        else if (ind.IndType == "6") // MIN
                        {
                            result = childValues.Min();
                        }
                        else if (ind.IndType == "3") // SUM
                        {
                            result = childValues.Sum();
                        }

                        var formatted = VietnameseNumberHelper.FormatVietnameseDouble(result);
                        ind.SetColumnValue(colKey, formatted);
                    }
                }
            }
        }

        NotifyStatsChanged();
    }

    private static (double? Value, bool HasAnyInput) EvaluateFormula(string formula, Dictionary<string, IndicatorItem> codeMap, string colKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(formula)) return (null, false);

            var pattern = @"[\{\[]([^{}\[\]]+)[\}\]]";
            var matches = System.Text.RegularExpressions.Regex.Matches(formula, pattern);
            if (matches.Count == 0) return (null, false);

            bool hasAnyInput = false;
            string expr = formula;

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var varCode = match.Groups[1].Value.Trim();
                double val = 0;

                if (codeMap.TryGetValue(varCode, out var targetInd))
                {
                    var raw = targetInd.GetColumnValue(colKey);
                    if (!string.IsNullOrWhiteSpace(raw) && raw != "-")
                    {
                        hasAnyInput = true;
                        var clean = VietnameseNumberHelper.ToStandardDecimalString(raw);
                        double.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val);
                    }
                }

                expr = expr.Replace(match.Value, val.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (!hasAnyInput) return (null, false);

            var dt = new System.Data.DataTable();
            var computed = dt.Compute(expr, "");
            if (computed != null)
            {
                double result;
                try
                {
                    // Dùng Convert.ToDouble với InvariantCulture để tránh lỗi khi locale máy dùng ',' làm dấu thập phân
                    result = System.Convert.ToDouble(computed, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    // Fallback: chuyển sang chuỗi rồi parse với InvariantCulture
                    if (!double.TryParse(computed.ToString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out result))
                        return (null, false);
                }

                if (double.IsNaN(result) || double.IsInfinity(result))
                    return (null, false);

                return (result, true);
            }
        }
        catch { }

        return (null, false);
    }

    public async Task<bool> SaveToServerAsync()
    {
        if (IsSaving) return false;

        var invalidStringValues = AllIndicators
            .Where(indicator => indicator.IsEditable)
            .SelectMany(indicator => DynamicHeaders
                .Where(header => header.IsStringColumn)
                .Select(header => new
                {
                    Indicator = indicator,
                    Header = header,
                    Value = indicator.GetColumnValue(header.FldCode) ?? string.Empty
                }))
            .Where(value => value.Value.Length > value.Header.MaxLength)
            .Take(3)
            .ToList();

        if (invalidStringValues.Count > 0)
        {
            HasError = true;
            var details = string.Join("; ", invalidStringValues.Select(value =>
                $"{value.Indicator.IndName} – {value.Header.HeaderName}: {value.Value.Length}/{value.Header.MaxLength}"));
            StatusMessage = $"❌ Không thể lưu: dữ liệu chuỗi vượt số ký tự tối đa ({details}).";
            return false;
        }

        IsSaving = true;
        HasError = false;

        // Nếu có attrId thì thực hiện Fetch-before-Save Merge để tránh ghi đè dữ liệu người khác
        if (!string.IsNullOrWhiteSpace(_attrId))
            StatusMessage = "Đang kiểm tra dữ liệu mới nhất từ server trước khi lưu...";
        else
            StatusMessage = "Đang lưu dữ liệu báo cáo lên hệ thống IOC (FNC003_P220)...";

        var (success, msg) = await _reportService.SaveReportDataAsync(Session, Report, AllIndicators.ToList(), DynamicHeaders.ToList(), _topIndId, _attrId, _deletedSubIndicators.ToList());

        IsSaving = false;

        if (success)
        {
            _deletedSubIndicators.Clear();
            HasError = false;
            StatusMessage = $"✅ {msg}";

            // ── Bảo toàn baseline + phát hiện ô do người khác cập nhật ───────────────────────
            // Snapshot giá trị TẤT CẢ ô trước reload để:
            //   1. Khôi phục baseline cho ô A vừa lưu (tránh conflict bị bỏ qua).
            //   2. Phát hiện ô không được A sửa nhưng có giá trị mới sau reload
            //      (tức là người dùng B vừa lưu) → đánh dấu viền xanh thông báo.
            var preSaveSnapshot = AllIndicators.ToDictionary(
                ind => string.IsNullOrWhiteSpace(ind.IndId) ? $"code:{ind.IndCode}" : ind.IndId,
                ind => DynamicHeaders.ToDictionary(
                    h => h.FldCode,
                    h => ind.GetColumnValue(h.FldCode),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            // Ô A vừa sửa và lưu (sẽ được restore baseline sau reload)
            var justSavedByIndId = AllIndicators
                .Where(ind => DynamicHeaders.Any(h => ind.IsCellModified(h.FldCode)))
                .ToDictionary(
                    ind => ind.IndId,
                    ind => DynamicHeaders
                        .Where(h => ind.IsCellModified(h.FldCode))
                        .ToDictionary(h => h.FldCode, h => ind.GetColumnValue(h.FldCode)),
                    StringComparer.OrdinalIgnoreCase);
            var justSavedByIndCode = AllIndicators
                .Where(ind => !string.IsNullOrWhiteSpace(ind.IndCode)
                           && DynamicHeaders.Any(h => ind.IsCellModified(h.FldCode)))
                .ToDictionary(
                    ind => ind.IndCode,
                    ind => DynamicHeaders
                        .Where(h => ind.IsCellModified(h.FldCode))
                        .ToDictionary(h => h.FldCode, h => ind.GetColumnValue(h.FldCode)),
                    StringComparer.OrdinalIgnoreCase);
            // ────────────────────────────────────────────────────────────────────────────

            // Reload để nhận cấu trúc mới (bao gồm IndId được IOC cấp cho chỉ tiêu con mới)
            await LoadIndicatorsAsync();

            // ── Khôi phục baseline + đánh dấu ô do người khác cập nhật ─────────────────────
            foreach (var ind in AllIndicators)
            {
                var snapshotKey = string.IsNullOrWhiteSpace(ind.IndId) ? $"code:{ind.IndCode}" : ind.IndId;
                preSaveSnapshot.TryGetValue(snapshotKey, out var preValues);

                Dictionary<string, string>? savedCells = null;
                if (!string.IsNullOrWhiteSpace(ind.IndId))
                    justSavedByIndId.TryGetValue(ind.IndId, out savedCells);
                if (savedCells == null && !string.IsNullOrWhiteSpace(ind.IndCode))
                    justSavedByIndCode.TryGetValue(ind.IndCode, out savedCells);

                foreach (var h in DynamicHeaders)
                {
                    var fldCode = h.FldCode;
                    if (savedCells != null && savedCells.TryGetValue(fldCode, out var savedValue))
                    {
                        // Ô A đã lưu: khôi phục giá trị và baseline về đúng giá trị A lưu.
                        ind.RestoreSavedCellValue(fldCode, savedValue);
                    }
                    else if (ind.IsEditable && preValues != null)
                    {
                        // Ô A không sửa: nếu reload mang lại giá trị khác → người khác đã cập nhật.
                        var preSaveVal = preValues.TryGetValue(fldCode, out var pv) ? pv : string.Empty;
                        var postReloadVal = ind.GetColumnValue(fldCode);
                        var preNorm = VietnameseNumberHelper.ToStandardDecimalString(preSaveVal);
                        var postNorm = VietnameseNumberHelper.ToStandardDecimalString(postReloadVal);
                        if (!string.Equals(preNorm, postNorm, StringComparison.Ordinal)
                            && !string.IsNullOrWhiteSpace(postReloadVal))
                        {
                            ind.MarkExternallyUpdated(fldCode);
                        }
                    }
                }
            }
            // ────────────────────────────────────────────────────────────────────────────

            NotifyStatsChanged();
            return true;
        }
        else
        {
            HasError = true;
            StatusMessage = $"❌ {msg}";
            return false;
        }
    }

    public async Task<(bool Success, string Message)> SubmitReportToLeaderAsync(string opinion)
    {
        if (IsSaving) return (false, "Đang lưu dữ liệu");
        return await _reportService.SubmitReportToLeaderAsync(Session, Report, opinion);
    }

    /// <summary>
    /// Tổng hợp số liệu trực tiếp từ các Stored Procedure của IOC (FNC006_S200 + FNC003_P105)
    /// </summary>
    /// <param name="onlyApproved">True: chỉ lấy các báo cáo đã duyệt (Status 4); False: lấy tất cả báo cáo hợp lệ (khác Status 1 'Đã giao')</param>
    public async Task<(bool Success, string Message)> AggregateReportAsync(bool onlyApproved = false)
    {
        if (IsSaving || IsLoading || IsAggregating)
            return (false, "Hệ thống đang bận xử lý tác vụ khác");

        IsAggregating = true;
        IsLoading = true;
        HasError = false;
        var modeText = onlyApproved ? "báo cáo đã duyệt" : "tất cả báo cáo";
        StatusMessage = $"⚡ Đang lấy dữ liệu và tổng hợp {modeText} từ API IOC...";

        try
        {
            var (success, msg, updatedInds, updatedHeaders, updatedTop) = await _reportService.AggregateReportAsync(
                Session, Report, AllIndicators.ToList(), DynamicHeaders.ToList(), _topIndId, onlyApproved);

            if (success)
            {
                RecalculateFormulas();
                NotifyStatsChanged();
                ApplyFilter();
                HasError = false;
                StatusMessage = $"✅ {msg}";
                return (true, msg);
            }
            else
            {
                HasError = true;
                StatusMessage = $"❌ {msg}";
                return (false, msg);
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"❌ Lỗi tổng hợp: {ex.Message}";
            return (false, ex.Message);
        }
        finally
        {
            IsAggregating = false;
            IsLoading = false;
        }
    }

    /// <summary>
    /// Thêm một chỉ tiêu con động dưới nhóm chỉ tiêu cha (ALLOW_ADD_ROW)
    /// </summary>
    public void AddSubIndicator(IndicatorItem currentItem)
    {
        if (currentItem == null) return;

        // Xác định thông tin nhóm cha gốc
        string parentId = currentItem.IsSubInd ? currentItem.ParentId : currentItem.IndId;
        var parentItem = AllIndicators.FirstOrDefault(i => i.IndId == parentId && !i.IsSubInd);

        string rootId = parentItem != null && !string.IsNullOrWhiteSpace(parentItem.RootId)
            ? parentItem.RootId
            : (!string.IsNullOrWhiteSpace(currentItem.RootId) ? currentItem.RootId : parentId);

        int level = parentItem != null ? parentItem.Level + 1 : (currentItem.IsSubInd ? currentItem.Level : currentItem.Level + 1);
        string unit = !string.IsNullOrWhiteSpace(currentItem.IndUnit) ? currentItem.IndUnit : (parentItem?.IndUnit ?? string.Empty);
        string parentCode = parentItem != null && !string.IsNullOrWhiteSpace(parentItem.IndCode)
            ? parentItem.IndCode
            : (currentItem.IsSubInd ? currentItem.IndCode : currentItem.IndCode);

        // Nếu parentCode có đuôi _số (trường hợp fallback lấy từ dòng con), tách lấy mã gốc
        if (currentItem.IsSubInd && !string.IsNullOrWhiteSpace(parentCode))
        {
            int lastUnder = parentCode.LastIndexOf('_');
            if (lastUnder > 0 && int.TryParse(parentCode.Substring(lastUnder + 1), out _))
            {
                parentCode = parentCode.Substring(0, lastUnder);
            }
        }

        // Chèn vào vị trí ngay dưới dòng hiện tại. IOC đánh số các dòng con theo IND_INDEX,
        // không theo mã chỉ tiêu đang hiển thị (cột này có thể bị ẩn).
        int insertPos = AllIndicators.IndexOf(currentItem);
        insertPos = Math.Max(0, insertPos + 1);
        var siblings = AllIndicators.Where(i => i.ParentId == parentId && i.IsSubInd).ToList();
        int nextIndex = siblings
            .Select(i => int.TryParse(i.SttDisplay, out var index) ? index : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        int nextCodeIndex = AllIndicators
            .Select(i => i.IndCode.Split('_', 2)[0])
            .Select(code => int.TryParse(code, out var index) ? index : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var orgId = !string.IsNullOrWhiteSpace(Report.TargetOrgId) ? Report.TargetOrgId : Session.OrgId;
        var generatedCode = $"{nextCodeIndex}_{Report.ObjId}_{orgId}{DateTimeOffset.Now.ToUnixTimeMilliseconds()}";

        var newSub = new IndicatorItem
        {
            Stt = AllIndicators.Count + 1,
            SttDisplay = nextIndex.ToString(),
            // Giá trị -1 báo IOC tạo IND_ID mới khi FNC003_P220 lưu thành công.
            IndId = "-1",
            IndCode = generatedCode,
            IndName = string.Empty,
            IndUnit = unit,
            IndType = "1",
            GroupId = "1",
            ParentId = parentId,
            RootId = rootId,
            OrgId = orgId,
            Level = level,
            AllowAddRow = false,
            IsSubInd = true,
            IsNewSubIndicator = true,
            IsModified = true
        };

        AllIndicators.Insert(insertPos, newSub);

        RecalculateRowHeight(newSub);
        IsActionColumnVisible = AllIndicators.Any(i => i.AllowAddRow || i.IsSubInd);
        ApplyFilter();
        NotifyStatsChanged();
        StatusMessage = $"Đã thêm một dòng chỉ tiêu mới bên dưới.";
    }

    /// <summary>
    /// Tính toán chiều cao chuẩn xác cho tất cả các dòng dựa trên toàn bộ các cột dữ liệu của dòng đó.
    /// Giúp chiều cao dòng vừa vặn 100% với nội dung và KHÔNG BAO GIỜ bị thay đổi/co dãn khi kéo thanh cuộn ngang.
    /// </summary>
    public void RecalculateAllRowHeights()
    {
        double nameColWidth = NameColumnWidth > 50 ? NameColumnWidth : 400;

        foreach (var ind in AllIndicators)
        {
            RecalculateRowHeight(ind, nameColWidth);
        }
    }

    public void RecalculateRowHeight(IndicatorItem ind, double? overrideNameColWidth = null)
    {
        double nameColWidth = overrideNameColWidth ?? (NameColumnWidth > 50 ? NameColumnWidth : 400);
        double maxH = 38;

        // 1. Đo độ cao của cột Tên chỉ tiêu / Nhiệm vụ
        if (!string.IsNullOrWhiteSpace(ind.IndName))
        {
            double indentOffset = ind.Level * 16 + 28;
            double availWidth = Math.Max(50, nameColWidth - indentOffset);
            double h = EstimateTextHeight(ind.IndName, availWidth);
            if (h > maxH) maxH = h;
        }

        // 2. Đo độ cao của tất cả các cột dữ liệu động (Mô tả 1, Mô tả 2, Sản phẩm, ...)
        foreach (var header in DynamicHeaders)
        {
            var val = ind.GetColumnValue(header.FldCode);
            if (!string.IsNullOrWhiteSpace(val) && val != "-")
            {
                double colW = header.ColWidth > 60 ? header.ColWidth : (header.IsStringColumn ? 220 : 160);
                double h = EstimateTextHeight(val, colW);
                if (h > maxH) maxH = h;
            }
        }

        ind.CalculatedRowHeight = Math.Max(38, Math.Ceiling(maxH));
    }

    private static double EstimateTextHeight(string text, double colWidth)
    {
        if (string.IsNullOrWhiteSpace(text)) return 38;

        // Trừ padding lề trái phải (khoảng 16px)
        double availWidth = Math.Max(30, colWidth - 16);

        // Ký tự tiếng Việt với font 12pt có chiều rộng trung bình ~7.0px
        double charsPerLine = Math.Max(4, availWidth / 7.0);

        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int totalLines = 0;

        foreach (var p in paragraphs)
        {
            if (string.IsNullOrEmpty(p))
            {
                totalLines += 1;
                continue;
            }

            var words = p.Split(' ');
            double currentLineChars = 0;
            int linesInPara = 1;

            foreach (var word in words)
            {
                double wordLen = word.Length;
                if (currentLineChars + wordLen > charsPerLine)
                {
                    if (currentLineChars > 0)
                    {
                        linesInPara++;
                        currentLineChars = wordLen + 1;
                    }
                    else
                    {
                        int extra = (int)Math.Ceiling(wordLen / charsPerLine);
                        linesInPara += Math.Max(1, extra);
                        currentLineChars = 0;
                    }
                }
                else
                {
                    currentLineChars += wordLen + 1;
                }
            }

            totalLines += linesInPara;
        }

        // Chiều cao mỗi dòng ~20px + khoảng đệm trên dưới 16px
        double calculatedHeight = (totalLines * 20.0) + 16;
        return Math.Max(38, calculatedHeight);
    }

    /// <summary>
    /// Xóa một chỉ tiêu con được thêm động
    /// </summary>
    public void DeleteSubIndicator(IndicatorItem subItem)
    {
        if (subItem == null || !subItem.IsSubInd) return;

        var parentId = subItem.ParentId;

        // Nếu chỉ tiêu đã tồn tại trên server (không phải mới tạo chưa lưu) -> lưu vào danh sách chờ xóa
        if (!subItem.IsNewSubIndicator && !string.IsNullOrWhiteSpace(subItem.IndId) && subItem.IndId != "-1")
        {
            if (!_deletedSubIndicators.Any(x => x.IndId == subItem.IndId))
            {
                _deletedSubIndicators.Add(subItem);
            }
        }

        AllIndicators.Remove(subItem);

        // Đánh số lại thứ tự các dòng con còn lại trong cùng nhóm
        var remainingSiblings = AllIndicators.Where(i => i.ParentId == parentId && i.IsSubInd).ToList();
        for (int i = 0; i < remainingSiblings.Count; i++)
        {
            remainingSiblings[i].SttDisplay = (i + 1).ToString();
        }

        IsActionColumnVisible = AllIndicators.Any(i => i.AllowAddRow || i.IsSubInd);
        ApplyFilter();
        RecalculateFormulas();
        NotifyStatsChanged();
        StatusMessage = $"Đã xóa dòng chỉ tiêu ({subItem.IndCode}).";
    }

    private void ApplyFilter()
    {
        FilteredIndicators.Clear();
        var term = FilterText?.Trim().ToLower() ?? "";

        foreach (var ind in AllIndicators)
        {
            if (string.IsNullOrWhiteSpace(term) ||
                (ind.IndName?.ToLower().Contains(term) ?? false) ||
                (ind.IndCode?.ToLower().Contains(term) ?? false) ||
                (ind.SttDisplay?.ToLower().Contains(term) ?? false) ||
                (ind.IndUnit?.ToLower().Contains(term) ?? false))
            {
                FilteredIndicators.Add(ind);
            }
        }
    }

    private void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(EditableCount));
        OnPropertyChanged(nameof(FilledCount));
    }

    /// <summary>
    /// Xuất toàn bộ dữ liệu bảng hiện tại ra file Excel (.xlsx).
    /// Không gọi máy chủ, nên vẫn xuất được số liệu người dùng đang nhập khi phiên đăng nhập đã hết hạn.
    /// </summary>
    public async Task<(bool Success, string Message)> ExportToExcelAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (AllIndicators.Count == 0)
                    return (false, "Không có dữ liệu để xuất Excel.");

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Báo cáo");

                // Cấu hình trang in & lưới
                worksheet.ShowGridLines = true;
                worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;

                // Xác định các cột dữ liệu
                var dataCols = DynamicHeaders.Count > 0
                    ? DynamicHeaders.Select(h => (
                        HeaderName: !string.IsNullOrWhiteSpace(h.HeaderName) ? h.HeaderName : h.FldCode,
                        FldCode: h.FldCode,
                        IsString: h.IsStringColumn
                    )).ToList()
                    : new List<(string HeaderName, string FldCode, bool IsString)> { ("Giá trị", "FN01", false) };

                int totalCols = 4 + dataCols.Count; // STT (1), Mã chỉ tiêu (2), Tên chỉ tiêu (3), Đơn vị tính (4), + dataCols

                // Chỉ xuất bảng dữ liệu; không thêm tiêu đề cơ quan, quốc hiệu hoặc khối ký tên.
                int headerRow = 1;
                worksheet.Row(headerRow).Height = 28;

                worksheet.Cell(headerRow, 1).Value = "STT";
                worksheet.Cell(headerRow, 2).Value = "Mã chỉ tiêu";
                worksheet.Cell(headerRow, 3).Value = "Tên chỉ tiêu";
                worksheet.Cell(headerRow, 4).Value = "Đơn vị tính";

                for (int i = 0; i < dataCols.Count; i++)
                {
                    worksheet.Cell(headerRow, 5 + i).Value = dataCols[i].HeaderName;
                }

                var headerRange = worksheet.Range(headerRow, 1, headerRow, totalCols);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontSize = 11;
                headerRange.Style.Font.FontName = "Times New Roman";
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E40AF");
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                headerRange.Style.Alignment.WrapText = true;

                // Dữ liệu chỉ tiêu
                int currentRow = headerRow + 1;
                foreach (var ind in AllIndicators)
                {
                    // Col 1: STT (Chuỗi / Ký hiệu) -> Wrap text
                    var sttCell = worksheet.Cell(currentRow, 1);
                    sttCell.Value = ind.SttDisplay;
                    sttCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sttCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    sttCell.Style.Alignment.WrapText = true;

                    // Col 2: Mã chỉ tiêu (Chuỗi) -> Wrap text
                    var codeCell = worksheet.Cell(currentRow, 2);
                    codeCell.Value = ind.IndCode;
                    codeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    codeCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    codeCell.Style.Alignment.WrapText = true;

                    // Col 3: Tên chỉ tiêu (Chuỗi) -> Wrap text
                    var nameCell = worksheet.Cell(currentRow, 3);
                    nameCell.Value = ind.IndName;
                    nameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    nameCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    nameCell.Style.Alignment.WrapText = true;
                    if (ind.Level > 0)
                    {
                        nameCell.Style.Alignment.Indent = Math.Min(ind.Level, 5);
                    }

                    // Col 4: Đơn vị tính (Chuỗi) -> Wrap text
                    var unitCell = worksheet.Cell(currentRow, 4);
                    unitCell.Value = ind.IndUnit;
                    unitCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    unitCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    unitCell.Style.Alignment.WrapText = true;

                    // Các cột dữ liệu
                    for (int i = 0; i < dataCols.Count; i++)
                    {
                        var cell = worksheet.Cell(currentRow, 5 + i);
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        var colMeta = dataCols[i];
                        var fld = colMeta.FldCode;
                        var rawVal = ind.GetColumnValue(fld);
                        if (string.IsNullOrWhiteSpace(rawVal) && dataCols.Count == 1)
                        {
                            rawVal = ind.Value;
                        }

                        if (ind.IsHeader)
                        {
                            cell.Value = "";
                        }
                        else if (!string.IsNullOrWhiteSpace(rawVal) && rawVal != "-")
                        {
                            if (colMeta.IsString)
                            {
                                cell.Value = rawVal;
                                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                                cell.Style.Alignment.WrapText = true;
                            }
                            else
                            {
                                var stdNumStr = VietnameseNumberHelper.ToStandardDecimalString(rawVal);
                                if (double.TryParse(stdNumStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
                                {
                                    cell.Value = dblVal;
                                    // Với số nguyên, không dùng định dạng có phần thập phân tùy chọn
                                    cell.Style.NumberFormat.Format = dblVal == Math.Truncate(dblVal)
                                        ? "#,##0"
                                        : "#,##0.###";
                                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                                }
                                else
                                {
                                    cell.Value = rawVal;
                                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                                    cell.Style.Alignment.WrapText = true;
                                }
                            }
                        }
                        else
                        {
                            cell.Value = ind.IsEditable ? "" : "-";
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                    }

                    var rowRange = worksheet.Range(currentRow, 1, currentRow, totalCols);
                    rowRange.Style.Font.FontName = "Times New Roman";
                    rowRange.Style.Font.FontSize = 11;
                    rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                    if (ind.IsHeader)
                    {
                        rowRange.Style.Font.Bold = true;
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF"); // Soft Indigo
                        rowRange.Style.Font.FontColor = XLColor.FromHtml("#1E3A8A");
                    }
                    else if (!ind.IsEditable)
                    {
                        rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC"); // Light Gray
                        rowRange.Style.Font.FontColor = XLColor.FromHtml("#334155");
                    }

                    currentRow++;
                }

                // Thêm Border rõ nét cho từng ô trong toàn bộ bảng
                var entireTableRange = worksheet.Range(headerRow, 1, currentRow - 1, totalCols);
                entireTableRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                entireTableRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                entireTableRange.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
                entireTableRange.Style.Border.RightBorder = XLBorderStyleValues.Thin;
                entireTableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                entireTableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                entireTableRange.Style.Border.TopBorderColor = XLColor.Black;
                entireTableRange.Style.Border.BottomBorderColor = XLColor.Black;
                entireTableRange.Style.Border.LeftBorderColor = XLColor.Black;
                entireTableRange.Style.Border.RightBorderColor = XLColor.Black;
                entireTableRange.Style.Border.InsideBorderColor = XLColor.Black;
                entireTableRange.Style.Border.OutsideBorderColor = XLColor.Black;

                // Riêng đường phân cách giữa các cột trong dòng Header giữ viền sáng để dễ nhìn
                headerRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#93C5FD");

                // Đặt chiều rộng cột tối ưu
                worksheet.Column(1).Width = 8;  // STT
                worksheet.Column(2).Width = 16; // Mã chỉ tiêu
                worksheet.Column(3).Width = 46; // Tên chỉ tiêu
                worksheet.Column(4).Width = 15; // Đơn vị tính
                for (int i = 0; i < dataCols.Count; i++)
                {
                    worksheet.Column(5 + i).Width = 20; // Cột số liệu
                }

                workbook.SaveAs(filePath);
                return (true, $"Đã xuất {AllIndicators.Count} chỉ tiêu ra file Excel thành công!");
            }
            catch (IOException)
            {
                return (false, "Không thể ghi đè file vì file đang được mở trong Excel. Vui lòng đóng file Excel hoặc lưu với tên khác!");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi khi xuất file Excel: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Nhập dữ liệu từ file Excel vào bảng báo cáo hiện tại (Hỗ trợ định dạng cũ, định dạng mới và file tự tạo)
    /// </summary>
    public async Task<(bool Success, string Message, int MatchedCount)> ImportFromExcelAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return (false, "File Excel không tồn tại.", 0);

        if (AllIndicators.Count == 0)
            return (false, "Chưa nạp danh sách chỉ tiêu của biểu mẫu.", 0);

        try
        {
            var (parsedExcelRows, error) = await Task.Run(() =>
            {
                var rows = new List<(string Stt, string Code, string Name, string Unit, Dictionary<string, string> Values)>();

                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var memoryStream = new MemoryStream();
                fileStream.CopyTo(memoryStream);
                memoryStream.Position = 0;

                using var workbook = new XLWorkbook(memoryStream);
                var worksheet = workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                    return (rows, "File Excel rỗng.");

                var lastRowUsed = worksheet.LastRowUsed();
                var lastColUsed = worksheet.LastColumnUsed();
                if (lastRowUsed == null || lastColUsed == null)
                    return (rows, "File Excel rỗng.");

                int lastRowNum = lastRowUsed.RowNumber();
                int lastColNum = lastColUsed.ColumnNumber();

                // 1. Quét tìm dòng tiêu đề bảng (trong tối đa 25 dòng đầu)
                int headerRowNum = -1;
                int sttCol = -1;
                int codeCol = -1;
                int nameCol = -1;
                int unitCol = -1;
                var valColMappings = new List<(int ColIndex, string FldCode, string HeaderText)>();

                for (int r = 1; r <= Math.Min(25, lastRowNum); r++)
                {
                    int tempStt = -1, tempCode = -1, tempName = -1, tempUnit = -1;

                    for (int c = 1; c <= lastColNum; c++)
                    {
                        var cellText = worksheet.Cell(r, c).GetString().Trim();
                        if (string.IsNullOrWhiteSpace(cellText)) continue;

                        var clean = VietnameseNumberHelper.CleanSearchKey(cellText);

                        if (clean.Contains("machitieu") || clean == "ma" || clean == "mact" || clean == "maso" || clean == "code" || clean == "indcode" || clean == "machitieubaocao")
                        {
                            tempCode = c;
                        }
                        else if (clean.Contains("tenchitieu") || clean.Contains("tenchitieubaocao") || clean.Contains("tendanhmuc") || clean == "chitieu" || clean == "ten" || clean == "noidung" || clean == "name")
                        {
                            tempName = c;
                        }
                        else if (clean.Contains("donvitinh") || clean == "dvt" || clean == "donvi" || clean == "unit")
                        {
                            tempUnit = c;
                        }
                        else if (clean == "stt" || clean == "tt" || clean == "sott" || clean == "sothutu" || clean == "no" || clean.Contains("chimuc"))
                        {
                            tempStt = c;
                        }
                    }

                    if (tempName != -1 || tempCode != -1)
                    {
                        headerRowNum = r;
                        sttCol = tempStt;
                        codeCol = tempCode;
                        nameCol = tempName;
                        unitCol = tempUnit;
                        break;
                    }
                }

                // Nếu không tìm thấy dòng tiêu đề, fallback mặc định dòng 1
                if (headerRowNum == -1)
                {
                    headerRowNum = 1;
                    sttCol = 1;
                    codeCol = 2;
                    nameCol = 3;
                    unitCol = 4;
                }

                // 2. Khớp các cột giá trị với DynamicHeaders
                for (int c = 1; c <= lastColNum; c++)
                {
                    if (c == sttCol || c == codeCol || c == nameCol || c == unitCol)
                        continue;

                    var headerText = worksheet.Cell(headerRowNum, c).GetString().Trim();
                    var cleanHead = VietnameseNumberHelper.CleanSearchKey(headerText);

                    // Bỏ qua cột ghi chú nếu có
                    if (cleanHead.Contains("ghichu") || cleanHead.Contains("note"))
                        continue;

                    // Tìm cột khớp trong DynamicHeaders
                    var matchedHeader = DynamicHeaders.FirstOrDefault(h =>
                        VietnameseNumberHelper.CleanSearchKey(h.HeaderName) == cleanHead ||
                        VietnameseNumberHelper.CleanSearchKey(h.FldCode) == cleanHead ||
                        (cleanHead.Length >= 3 && VietnameseNumberHelper.CleanSearchKey(h.HeaderName).Contains(cleanHead)) ||
                        (cleanHead.Length >= 3 && cleanHead.Contains(VietnameseNumberHelper.CleanSearchKey(h.HeaderName))));

                    if (matchedHeader != null)
                    {
                        valColMappings.Add((c, matchedHeader.FldCode, headerText));
                    }
                    else
                    {
                        valColMappings.Add((c, string.Empty, headerText));
                    }
                }

                // Nếu có cột chưa khớp FldCode, gán tuần tự cho các DynamicHeaders còn lại
                if (DynamicHeaders.Count > 0)
                {
                    var assignedFlds = valColMappings.Where(m => !string.IsNullOrWhiteSpace(m.FldCode)).Select(m => m.FldCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var unassignedFlds = DynamicHeaders.Where(h => !assignedFlds.Contains(h.FldCode)).Select(h => h.FldCode).ToList();

                    int unassignedIdx = 0;
                    for (int i = 0; i < valColMappings.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(valColMappings[i].FldCode) && unassignedIdx < unassignedFlds.Count)
                        {
                            valColMappings[i] = (valColMappings[i].ColIndex, unassignedFlds[unassignedIdx], valColMappings[i].HeaderText);
                            unassignedIdx++;
                        }
                    }
                }

                // Fallback nếu không có cột nào khớp và file có cột thứ 5 trở đi
                if (valColMappings.Count == 0 && lastColNum >= 5)
                {
                    valColMappings.Add((5, DynamicHeaders.FirstOrDefault()?.FldCode ?? "FN01", "Giá trị"));
                }
                else if (valColMappings.Count > 0 && valColMappings.All(m => string.IsNullOrWhiteSpace(m.FldCode)))
                {
                    for (int i = 0; i < valColMappings.Count; i++)
                    {
                        valColMappings[i] = (valColMappings[i].ColIndex, DynamicHeaders.Count > i ? DynamicHeaders[i].FldCode : $"FN0{i + 1}", valColMappings[i].HeaderText);
                    }
                }

                // 3. Đọc dữ liệu từng dòng
                for (int r = headerRowNum + 1; r <= lastRowNum; r++)
                {
                    var stt = sttCol > 0 ? worksheet.Cell(r, sttCol).GetString().Trim() : "";
                    var code = codeCol > 0 ? worksheet.Cell(r, codeCol).GetString().Trim() : "";
                    var name = nameCol > 0 ? worksheet.Cell(r, nameCol).GetString().Trim() : "";
                    var unit = unitCol > 0 ? worksheet.Cell(r, unitCol).GetString().Trim() : "";

                    // Bỏ qua các dòng chú thích hoặc chữ ký ở cuối file
                    if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
                    {
                        var cleanStt = VietnameseNumberHelper.CleanSearchKey(stt);
                        if (cleanStt.Contains("ngayxuat") || cleanStt.Contains("nguoilap") || cleanStt.Contains("kyten") || cleanStt.Contains("thoigian"))
                            continue;
                    }

                    var colVals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (colIdx, fld, _) in valColMappings)
                    {
                        if (colIdx <= lastColNum)
                        {
                            var cell = worksheet.Cell(r, colIdx);
                            string rawVal;

                            if (cell.DataType == XLDataType.Number)
                            {
                                rawVal = cell.GetDouble().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                rawVal = cell.GetString().Trim();
                            }

                            if (!string.IsNullOrWhiteSpace(rawVal) && rawVal != "-")
                            {
                                var formatted = VietnameseNumberHelper.FormatVietnameseNumber(rawVal);
                                if (!string.IsNullOrWhiteSpace(formatted))
                                {
                                    colVals[fld] = formatted;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(name) || colVals.Count > 0)
                    {
                        rows.Add((stt, code, name, unit, colVals));
                    }
                }

                return (rows, string.Empty);
            });

            if (!string.IsNullOrEmpty(error))
                return (false, error, 0);

            if (parsedExcelRows.Count == 0)
                return (false, "Không tìm thấy dữ liệu nào trong file Excel.", 0);

            int matchedCount = 0;
            var usedExcelIndices = new HashSet<int>();
            var editableIndicators = AllIndicators.Where(i => i.IsEditable).ToList();
            var dynamicFlds = DynamicHeaders.Select(h => h.FldCode).ToList();

            // 1. Tự động xóa sạch giá trị của các chỉ tiêu KHÔNG ĐƯỢC PHÉP GHI (không dùng kết quả ghi trong Excel)
            foreach (var ind in AllIndicators.Where(i => !i.IsEditable))
            {
                ind.ClearValues(dynamicFlds);
            }

            // 2. Xóa sạch giá trị của các chỉ tiêu được ghi trước khi nạp để cập nhật chuẩn xác theo file Excel
            foreach (var ind in editableIndicators)
            {
                ind.ClearValues(dynamicFlds);
            }

            // Pass 1: Khớp theo Mã chỉ tiêu (IndCode) - Ưu tiên số 1, chính xác 100%
            foreach (var ind in editableIndicators)
            {
                if (string.IsNullOrWhiteSpace(ind.IndCode)) continue;
                var cleanCode = VietnameseNumberHelper.CleanCode(ind.IndCode);

                for (int i = 0; i < parsedExcelRows.Count; i++)
                {
                    if (usedExcelIndices.Contains(i)) continue;
                    if (!string.IsNullOrWhiteSpace(parsedExcelRows[i].Code) && VietnameseNumberHelper.CleanCode(parsedExcelRows[i].Code) == cleanCode)
                    {
                        usedExcelIndices.Add(i);
                        ApplyExcelValues(ind, parsedExcelRows[i].Values);
                        matchedCount++;
                        break;
                    }
                }
            }

            // Pass 2: Khớp theo Tên chỉ tiêu + Đơn vị tính
            foreach (var ind in editableIndicators)
            {
                if (ind.IsModified) continue;
                var cleanName = VietnameseNumberHelper.CleanSearchKey(ind.IndName);
                var cleanUnit = VietnameseNumberHelper.CleanSearchKey(ind.IndUnit);

                for (int i = 0; i < parsedExcelRows.Count; i++)
                {
                    if (usedExcelIndices.Contains(i)) continue;
                    if (VietnameseNumberHelper.CleanSearchKey(parsedExcelRows[i].Name) == cleanName &&
                        VietnameseNumberHelper.CleanSearchKey(parsedExcelRows[i].Unit) == cleanUnit)
                    {
                        usedExcelIndices.Add(i);
                        ApplyExcelValues(ind, parsedExcelRows[i].Values);
                        matchedCount++;
                        break;
                    }
                }
            }

            // Pass 3: Khớp theo Tên chỉ tiêu
            foreach (var ind in editableIndicators)
            {
                if (ind.IsModified) continue;
                var cleanName = VietnameseNumberHelper.CleanSearchKey(ind.IndName);

                for (int i = 0; i < parsedExcelRows.Count; i++)
                {
                    if (usedExcelIndices.Contains(i)) continue;
                    if (VietnameseNumberHelper.CleanSearchKey(parsedExcelRows[i].Name) == cleanName)
                    {
                        usedExcelIndices.Add(i);
                        ApplyExcelValues(ind, parsedExcelRows[i].Values);
                        matchedCount++;
                        break;
                    }
                }
            }

            // Pass 4: Khớp theo STT Display
            foreach (var ind in editableIndicators)
            {
                if (ind.IsModified) continue;
                if (string.IsNullOrWhiteSpace(ind.SttDisplay)) continue;
                var cleanStt = VietnameseNumberHelper.CleanCode(ind.SttDisplay);

                for (int i = 0; i < parsedExcelRows.Count; i++)
                {
                    if (usedExcelIndices.Contains(i)) continue;
                    if (!string.IsNullOrWhiteSpace(parsedExcelRows[i].Stt) && VietnameseNumberHelper.CleanCode(parsedExcelRows[i].Stt) == cleanStt)
                    {
                        usedExcelIndices.Add(i);
                        ApplyExcelValues(ind, parsedExcelRows[i].Values);
                        matchedCount++;
                        break;
                    }
                }
            }

            // Pass 5: Khớp tuần tự nếu chưa có chỉ tiêu nào khớp và số dòng khớp
            if (matchedCount == 0 && parsedExcelRows.Count >= editableIndicators.Count)
            {
                for (int i = 0; i < editableIndicators.Count; i++)
                {
                    ApplyExcelValues(editableIndicators[i], parsedExcelRows[i].Values);
                    matchedCount++;
                }
            }

            RecalculateFormulas();
            NotifyStatsChanged();
            ApplyFilter();

            if (matchedCount > 0)
            {
                return (true, $"Đã nhập thành công {matchedCount}/{editableIndicators.Count} chỉ tiêu từ file Excel!", matchedCount);
            }
            else
            {
                return (false, "Không tìm thấy chỉ tiêu nào khớp giữa file Excel và biểu mẫu báo cáo.", 0);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi nhập dữ liệu từ Excel: {ex.Message}", 0);
        }
    }

    private void ApplyExcelValues(IndicatorItem ind, Dictionary<string, string> values)
    {
        if (!ind.IsEditable) return;

        foreach (var (fld, val) in values)
        {
            if (!string.IsNullOrWhiteSpace(fld))
            {
                ind.SetColumnValue(fld, val);
            }
        }

        if (DynamicHeaders.Count <= 1 && values.Count > 0)
        {
            var firstVal = values.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
            var targetFld = DynamicHeaders.FirstOrDefault()?.FldCode ?? "FN01";
            ind.SetColumnValue(targetFld, firstVal);
            ind.Value = firstVal;
        }
        else if (DynamicHeaders.Count == 0 && values.Count > 0)
        {
            ind.Value = values.Values.First();
        }

        ind.IsModified = true;
    }

    private static string CleanString(string? s)
    {
        return VietnameseNumberHelper.CleanSearchKey(s);
    }
}
