using MiniExcelLibs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VSR.Models;

namespace VSR.Services;

public class ExcelRowData
{
    public string Stt { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class ExcelMatchResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public int MatchedCount { get; set; }
    public int TotalServerLeaves { get; set; }
    public int TotalExcelRows { get; set; }
    public List<string> UnmatchedExcelRows { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class ExcelBackupService
{
    private static readonly string[] SearchDirectories = new[]
    {
        "Backup",
        "CTKTXH",
        "CT57",
        "CT57 + CTKTXH",
        "Save Data to DB"
    };

    public string? FindBackupFile(string workspaceRoot, string reportName, string objId, string orgId, string timeId)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            workspaceRoot = AppDomain.CurrentDomain.BaseDirectory;
        }

        // Try locating Backup folder
        var possibleBackupRoots = new List<string>
        {
            Path.Combine(workspaceRoot, "Backup"),
            Path.Combine(Directory.GetParent(workspaceRoot)?.FullName ?? "", "Backup"),
            Path.Combine(Directory.GetParent(workspaceRoot)?.Parent?.FullName ?? "", "Backup"),
            Path.Combine(Directory.GetParent(workspaceRoot)?.Parent?.Parent?.FullName ?? "", "Backup"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backup"),
            Path.Combine(Directory.GetCurrentDirectory(), "Backup")
        };

        string? backupDir = null;
        foreach (var dir in possibleBackupRoots)
        {
            if (Directory.Exists(dir))
            {
                backupDir = dir;
                break;
            }
        }

        if (backupDir == null || !Directory.Exists(backupDir))
            return null;

        var targetSuffix = $"_{objId}_{orgId}_{timeId}.xlsx";
        var altSuffix = $"_{objId}_{timeId}.xlsx";

        // 1. Search in subfolders of Backup (e.g. Backup/CTKTXH_QUY/, Backup/CT53_THANG/, etc.)
        try
        {
            var files = Directory.GetFiles(backupDir, "*.xlsx", SearchOption.AllDirectories);

            // First priority: matches targetSuffix (obj_id + org_id + time_id)
            var match1 = files.FirstOrDefault(f => !Path.GetFileName(f).StartsWith("~$") &&
                                                   Path.GetFileName(f).EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase));
            if (match1 != null) return match1;

            // Second priority: matches altSuffix (obj_id + time_id)
            var match2 = files.FirstOrDefault(f => !Path.GetFileName(f).StartsWith("~$") &&
                                                   Path.GetFileName(f).EndsWith(altSuffix, StringComparison.OrdinalIgnoreCase));
            if (match2 != null) return match2;

            // Third priority: contains both obj_id and time_id in filename
            var match3 = files.FirstOrDefault(f => !Path.GetFileName(f).StartsWith("~$") &&
                                                   Path.GetFileName(f).Contains(objId) &&
                                                   Path.GetFileName(f).Contains(timeId));
            if (match3 != null) return match3;
        }
        catch
        {
            // Ignore search errors
        }

        return null;
    }

    public List<ExcelRowData> ReadExcelFile(string filePath)
    {
        var result = new List<ExcelRowData>();
        if (!File.Exists(filePath)) return result;

        try
        {
            var rows = MiniExcel.Query(filePath, useHeaderRow: false).ToList();
            if (rows.Count < 2) return result;

            // Inspect header row (row 0)
            var headerRow = rows[0] as IDictionary<string, object?>;
            if (headerRow == null) return result;

            var headerCols = headerRow.Values.Select(v => v?.ToString()?.Trim() ?? string.Empty).ToList();

            int sttIdx = 0;
            int nameIdx = 1;
            int unitIdx = 2;
            int valIdx = 3;

            for (int i = 0; i < headerCols.Count; i++)
            {
                var hClean = headerCols[i].ToLower().Replace(" ", "");
                if (hClean.Contains("tênchỉtiêu") || hClean.Contains("têndanhmục") || hClean.Contains("tenchitieu"))
                    nameIdx = i;
                else if (hClean.Contains("đơnvịtính") || hClean.Contains("đơnvị") || hClean.Contains("donvitinh"))
                    unitIdx = i;
                else if (hClean.Contains("giátrị") || hClean.Contains("giatri") || hClean.Contains("sốliệu"))
                    valIdx = i;
                else if (hClean.Contains("stt") || hClean.Contains("chỉmục") || hClean.Contains("mã"))
                    sttIdx = i;
            }

            for (int r = 1; r < rows.Count; r++)
            {
                var rowDict = rows[r] as IDictionary<string, object?>;
                if (rowDict == null) continue;

                var valuesList = rowDict.Values.ToList();

                var stt = sttIdx < valuesList.Count ? valuesList[sttIdx]?.ToString()?.Trim() ?? "" : "";
                var name = nameIdx < valuesList.Count ? valuesList[nameIdx]?.ToString()?.Trim() ?? "" : "";
                var unit = unitIdx < valuesList.Count ? valuesList[unitIdx]?.ToString()?.Trim() ?? "" : "";
                var val = valIdx < valuesList.Count ? valuesList[valIdx]?.ToString()?.Trim() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(new ExcelRowData
                    {
                        Stt = stt,
                        Name = name,
                        Unit = unit,
                        Value = val
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi đọc file Excel ({filePath}): {ex.Message}");
        }

        return result;
    }

    public ExcelMatchResult MatchAndPopulate(List<IndicatorItem> indicators, string excelFilePath)
    {
        var result = new ExcelMatchResult
        {
            FilePath = excelFilePath,
            TotalServerLeaves = indicators.Count(i => i.IsEditable)
        };

        var excelRows = ReadExcelFile(excelFilePath);
        result.TotalExcelRows = excelRows.Count;

        if (excelRows.Count == 0)
        {
            result.Success = false;
            result.Message = "Không đọc được dữ liệu nào từ file Excel (File rỗng hoặc sai cấu trúc).";
            return result;
        }

        var usedExcelIndices = new HashSet<int>();
        var serverLeaves = indicators.Where(i => i.IsEditable).ToList();

        foreach (var sv in serverLeaves)
        {
            var svNameClean = CleanString(sv.IndName);
            var svUnitClean = CleanString(sv.IndUnit);

            int matchedIdx = -1;

            // Pass 1: Match Name and Unit
            for (int i = 0; i < excelRows.Count; i++)
            {
                if (usedExcelIndices.Contains(i)) continue;

                var exNameClean = CleanString(excelRows[i].Name);
                var exUnitClean = CleanString(excelRows[i].Unit);

                if (exNameClean == svNameClean && exUnitClean == svUnitClean)
                {
                    matchedIdx = i;
                    break;
                }
            }

            // Pass 2 Fallback: Match Name only
            if (matchedIdx == -1)
            {
                for (int i = 0; i < excelRows.Count; i++)
                {
                    if (usedExcelIndices.Contains(i)) continue;

                    var exNameClean = CleanString(excelRows[i].Name);
                    if (exNameClean == svNameClean)
                    {
                        matchedIdx = i;
                        break;
                    }
                }
            }

            if (matchedIdx != -1)
            {
                usedExcelIndices.Add(matchedIdx);
                var rawVal = excelRows[matchedIdx].Value;
                sv.Value = SanitizeDecimal(rawVal);
                sv.MatchStatus = "✅ Đã nạp từ Backup Excel";
                result.MatchedCount++;
            }
        }

        for (int i = 0; i < excelRows.Count; i++)
        {
            if (!usedExcelIndices.Contains(i))
            {
                result.UnmatchedExcelRows.Add($"{excelRows[i].Name} ({excelRows[i].Unit}): {excelRows[i].Value}");
            }
        }

        result.Success = result.MatchedCount > 0;
        result.Message = $"Đã tự động điền thành công {result.MatchedCount}/{result.TotalServerLeaves} chỉ tiêu từ file {Path.GetFileName(excelFilePath)}";

        return result;
    }

    private static string CleanString(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return string.Concat(s.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
    }

    public static string SanitizeDecimal(string rawVal)
    {
        if (string.IsNullOrWhiteSpace(rawVal))
            return string.Empty;

        var valStr = rawVal.Trim();
        if (valStr.Contains(','))
        {
            var clean = valStr.Replace(".", "").Replace(",", ".");
            return clean;
        }

        if (valStr.Contains('.'))
        {
            if (valStr.Count(c => c == '.') == 1)
                return valStr;
            return valStr.Replace(".", "");
        }

        return valStr;
    }
}
