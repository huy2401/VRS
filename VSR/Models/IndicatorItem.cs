using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using VSR.ViewModels;

namespace VSR.Models;

public class IndicatorItem : ViewModelBase
{
    private readonly Dictionary<string, string> _columnValues = new(StringComparer.OrdinalIgnoreCase);
    // Snapshot received from IOC.  It lets an empty value mean either "untouched"
    // or an explicit user deletion, instead of treating both cases as the same.
    private readonly Dictionary<string, string> _originalColumnValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _conflictingServerValues = new(StringComparer.OrdinalIgnoreCase);
    // Các ô bị thay đổi bởi người dùng khác trong lần lưu vừa rồi (hiển thị viền xanh)
    private readonly HashSet<string> _externallyUpdatedColumns = new(StringComparer.OrdinalIgnoreCase);
    private string _value = string.Empty;
    private string _matchStatus = string.Empty;
    private bool _isModified;
    private string _sttDisplay = string.Empty;
    private string _indCode = string.Empty;
    private string _indName = string.Empty;
    private string _indUnit = string.Empty;
    private bool _allowAddRow;
    private bool _isSubInd;
    private bool _isNewSubIndicator;
    private string _rootId = string.Empty;
    private string _dataId = string.Empty;
    private string _orgId = string.Empty;

    public int Stt { get; set; }
    
    public string SttDisplay
    {
        get => _sttDisplay;
        set
        {
            if (SetProperty(ref _sttDisplay, value))
            {
                IsModified = true;
            }
        }
    }

    public string IndId { get; set; } = string.Empty;

    public string IndCode
    {
        get => _indCode;
        set
        {
            if (SetProperty(ref _indCode, value))
            {
                IsModified = true;
            }
        }
    }

    public string IndName
    {
        get => _indName;
        set
        {
            if (SetProperty(ref _indName, value))
            {
                IsModified = true;
            }
        }
    }

    public string IndUnit
    {
        get => _indUnit;
        set
        {
            if (SetProperty(ref _indUnit, value))
            {
                IsModified = true;
            }
        }
    }

    public string IndType { get; set; } = "1"; // "1": Chỉ tiêu nhập liệu (leaf), "2": Nhóm chỉ tiêu (parent)
    public string FieldId { get; set; } = string.Empty;
    public string PreVal { get; set; } = string.Empty;
    public int Level { get; set; } = 0;

    public string ParentId { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
    public string GroupId { get; set; } = "1";
    public bool HasChildren { get; set; } = false;

    public bool AllowAddRow
    {
        get => _allowAddRow;
        set => SetProperty(ref _allowAddRow, value);
    }

    public bool IsSubInd
    {
        get => _isSubInd;
        set => SetProperty(ref _isSubInd, value);
    }

    /// <summary>Chỉ tiêu con vừa thêm tại màn hình hiện tại, chưa được IOC cấp IND_ID.</summary>
    public bool IsNewSubIndicator
    {
        get => _isNewSubIndicator;
        set => SetProperty(ref _isNewSubIndicator, value);
    }

    public string RootId
    {
        get => _rootId;
        set => SetProperty(ref _rootId, value);
    }

    public string DataId
    {
        get => _dataId;
        set => SetProperty(ref _dataId, value);
    }

    public string OrgId
    {
        get => _orgId;
        set => SetProperty(ref _orgId, value);
    }

    /// <summary>
    /// Lưu lại cấu trúc ATTR_INFO gốc từ server nếu có
    /// </summary>
    public JsonElement? RawAttrInfo { get; set; }
    public string OrigAttrCode { get; set; } = "CTKTXH";
    public string OrigFldCode { get; set; } = "FN01";

    public bool HasFormula => !string.IsNullOrWhiteSpace(Formula);

    /// <summary>
    /// Nhóm chỉ tiêu (Header category): (IND_TYPE == "2" hoặc GROUP_ID == "0") và không phải là chỉ tiêu con được thêm động
    /// </summary>
    public bool IsHeader => !IsSubInd && (IndType == "2" || GroupId == "0");

    /// <summary>
    /// Chỉ tiêu lá ban đầu từ hệ thống (hiển thị icon kim cương 🔹, chỉ tiêu thêm vào IsSubInd sẽ không có icon)
    /// </summary>
    public bool IsNormalLeaf => !IsHeader && !IsSubInd;

    /// <summary>
    /// Chỉ tiêu tự động tính: CHỈ KHI server trả về FORMULA hoặc IND_TYPE là loại tính toán (3: SUM, 4: AVG, 5: MAX, 6: MIN)
    /// </summary>
    public bool IsCalculated => HasFormula || IndType == "3" || IndType == "4" || IndType == "5" || IndType == "6";

    /// <summary>
    /// Chỉ tiêu được phép nhập liệu: IND_TYPE == "1" (hoặc GROUP_ID == "1" hoặc IsSubInd) và không có công thức khóa
    /// </summary>
    public bool IsEditable => (!IsHeader && !IsCalculated && (IndType == "1" || GroupId == "1")) || IsSubInd;

    public Thickness IndentMargin => new(Level * 16, 0, 0, 0);

    /// <summary>
    /// Màu nền hàng: Tô màu xám đậm rõ nét (#E2E8F0) cho các hàng không thể điền dữ liệu
    /// </summary>
    public string RowBackground => !IsEditable && !AllowAddRow ? "#E2E8F0" : (IsSubInd ? "#F0FDF4" : "#FFFFFF");
    public string NameFontWeight => IsHeader ? "Bold" : "Normal";
    public string NameForeground => IsHeader ? "#1E3A8A" : (!IsEditable ? "#1E293B" : "#334155");

    private double _calculatedRowHeight = 38;
    public double CalculatedRowHeight
    {
        get => _calculatedRowHeight;
        set => SetProperty(ref _calculatedRowHeight, value);
    }

    /// <summary>
    /// Giá trị cột chính (Col 0)
    /// </summary>
    public string Value
    {
        get => !string.IsNullOrEmpty(_value) ? _value : GetFirstValue();
        set
        {
            if (SetProperty(ref _value, value))
            {
                if (_columnValues.Count > 0)
                {
                    var firstKey = _columnValues.Keys.First();
                    _columnValues[firstKey] = value;
                }
                IsModified = true;
            }
        }
    }

    public string GetColumnValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        if (key.Equals("STT", StringComparison.OrdinalIgnoreCase) || key.Equals("SttDisplay", StringComparison.OrdinalIgnoreCase)) return SttDisplay;
        if (key.Equals("IndCode", StringComparison.OrdinalIgnoreCase) || key.Equals("IND_CODE", StringComparison.OrdinalIgnoreCase)) return IndCode;
        if (key.Equals("IndName", StringComparison.OrdinalIgnoreCase) || key.Equals("IND_NAME", StringComparison.OrdinalIgnoreCase)) return IndName;
        if (key.Equals("IndUnit", StringComparison.OrdinalIgnoreCase) || key.Equals("IND_UNIT", StringComparison.OrdinalIgnoreCase)) return IndUnit;
        return _columnValues.TryGetValue(key, out var v) ? v : string.Empty;
    }

    public void SetColumnValue(string key, string val)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (key.Equals("STT", StringComparison.OrdinalIgnoreCase) || key.Equals("SttDisplay", StringComparison.OrdinalIgnoreCase))
        {
            SttDisplay = val;
            return;
        }
        if (key.Equals("IndCode", StringComparison.OrdinalIgnoreCase) || key.Equals("IND_CODE", StringComparison.OrdinalIgnoreCase))
        {
            IndCode = val;
            return;
        }
        if (key.Equals("IndName", StringComparison.OrdinalIgnoreCase) || key.Equals("IND_NAME", StringComparison.OrdinalIgnoreCase))
        {
            IndName = val;
            return;
        }
        if (key.Equals("IndUnit", StringComparison.OrdinalIgnoreCase) || key.Equals("IND_UNIT", StringComparison.OrdinalIgnoreCase))
        {
            IndUnit = val;
            return;
        }
        _columnValues[key] = val;
        _value = GetFirstValue();
        IsModified = true;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged($"Col_{key}");
    }

    public void CaptureServerSnapshot(IEnumerable<string> fldCodes)
    {
        _originalColumnValues.Clear();
        foreach (var key in fldCodes)
            _originalColumnValues[key] = GetColumnValue(key) ?? string.Empty;
        _conflictingServerValues.Clear();
        _externallyUpdatedColumns.Clear();
        IsModified = false;
    }

    public string GetOriginalColumnValue(string key) =>
        _originalColumnValues.TryGetValue(key, out var value) ? value : string.Empty;

    public bool IsCellModified(string key) => !string.Equals(
        GetColumnValue(key) ?? string.Empty,
        GetOriginalColumnValue(key), StringComparison.Ordinal);

    public bool HasConflict(string key) => _conflictingServerValues.ContainsKey(key);

    public string GetConflictingServerValue(string key) =>
        _conflictingServerValues.TryGetValue(key, out var value) ? value : string.Empty;

    public void SetConflict(string key, string serverValue)
    {
        _conflictingServerValues[key] = serverValue ?? string.Empty;
        OnPropertyChanged($"Conflict_{key}");
    }

    public void ClearConflict(string key)
    {
        if (_conflictingServerValues.Remove(key)) OnPropertyChanged($"Conflict_{key}");
    }

    /// <summary>
    /// Đánh dấu ô này đã được người dùng khác cập nhật trong khoảng thời gian lưu vừa rồi.
    /// UI sẽ tô viền xanh để thông báo.
    /// </summary>
    public void MarkExternallyUpdated(string key)
    {
        _externallyUpdatedColumns.Add(key);
        OnPropertyChanged($"Ext_{key}");
    }

    /// <summary>Xóa trạng thái "được người khác cập nhật" (khi người dùng focus vào ô).</summary>
    public void ClearExternallyUpdated(string key)
    {
        if (_externallyUpdatedColumns.Remove(key)) OnPropertyChanged($"Ext_{key}");
    }

    public bool IsExternallyUpdated(string key) => _externallyUpdatedColumns.Contains(key);

    public void ResolveConflictKeepingLocal(string key)
    {
        // The latest server value becomes the comparison base; the local value stays dirty
        // and may now be deliberately saved over it.
        _originalColumnValues[key] = GetConflictingServerValue(key);
        ClearConflict(key);
    }

    public void ResolveConflictKeepingServer(string key)
    {
        var serverValue = GetConflictingServerValue(key);
        _columnValues[key] = serverValue;
        _originalColumnValues[key] = serverValue;
        _value = GetFirstValue();
        ClearConflict(key);
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged($"Col_{key}");
    }

    /// <summary>
    /// Sau khi lưu thành công, khôi phục cả giá trị hiển thị lẫn baseline của ô về đúng
    /// giá trị vừa được lưu lên server. Mục đích: nếu người khác đã thay đổi ô này trên
    /// server trong khoảng thời gian giữa lúc lưu và lúc reload, lần lưu tiếp theo sẽ
    /// phát hiện đúng xung đột (fetch ≠ baseline), thay vì bỏ qua do baseline bị cập nhật
    /// thành giá trị của người khác trong quá trình reload.
    /// </summary>
    public void RestoreSavedCellValue(string key, string savedValue)
    {
        _columnValues[key] = savedValue;
        _originalColumnValues[key] = savedValue;
        _value = GetFirstValue();
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged($"Col_{key}");
    }

    public string GetFirstValue()
    {
        return _columnValues.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }

    public void ClearValues(IEnumerable<string>? fldCodes = null)
    {
        var keys = fldCodes?.ToList() ?? _columnValues.Keys.ToList();
        _columnValues.Clear();
        _value = string.Empty;
        IsModified = true;
        OnPropertyChanged(nameof(Value));
        foreach (var key in keys)
        {
            _columnValues[key] = string.Empty;
            OnPropertyChanged($"Col_{key}");
        }
    }

    public Dictionary<string, string> GetAllValues() => _columnValues;

    public string MatchStatus
    {
        get => _matchStatus;
        set => SetProperty(ref _matchStatus, value);
    }

    public bool IsModified
    {
        get => _isModified;
        set => SetProperty(ref _isModified, value);
    }
}
