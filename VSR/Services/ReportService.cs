using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VSR.Helpers;
using VSR.Models;

namespace VSR.Services;

public class ReportColumnConfig
{
    public string SourceCol { get; set; } = "Giá trị";
    public string FldCode { get; set; } = "FN01";
    public string AttrCode { get; set; } = "CTKTXH";
    public string Desc { get; set; } = "";
}

public class IocApiConfig
{
    public string ApiUrl { get; set; } = "https://report.vnsr.vn/IOC_WS/ws_recvMsgServlet";
    public string AccessToken { get; set; } = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJoYXRpbmhAMTIzIiwibmFtZSI6ImhhdGluaC5zeW5jIiwiaWF0IjoxNTE2MjM5MDExNjgsInRlbmFudF9pZCI6ODV9.m85eHSTickKAkLSUQ9tiUVfPYjNwntE5R2MtLJv0zGk";
    public string Cookie { get; set; } = "SESSIONID=!KRBrU2loOlRAf2d1nXdznSBmexNu786vQW45pVI3wHu14KEIseRcAXa9TmWJPZHBmPjvI/eHfF9GLw==; TS01df1866=012b75ff9c51849ad4522b844f5ab72879fb89b88860cc2dd44aa16d481b73aab2d76b1454060c8e8ba2791ea7b40513fef83c4968a4ce0e6c599d1c87e77d9ea3c465b3d7";
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string> ReportCodeMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> OrgCodeMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<ReportColumnConfig>> ReportColumnsMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ReportService
{
    public async Task<(List<ReportItem> Reports, string ErrorMessage)> GetAssignedReportsAsync(AuthSession session)
    {
        var reports = new List<ReportItem>();
        if (!session.IsAuthenticated)
            return (reports, "Chưa xác thực phiên đăng nhập");

        try
        {
            // 1. Fetch Input Reports from FNC002_S107_1 (Nhập báo cáo số liệu)
            var (inputReports, inputError) = await FetchInputReportsAsync(session);

            // 2. Fetch Submit Reports from FNC010_S22_2 (Gửi báo cáo)
            var (submitReports, submitError) = await FetchSubmitReportsAsync(session);

            // 3. Fetch Tracking Reports from FNC010_S24 (Theo dõi trạng thái báo cáo)
            var (trackingReports, trackingError) = await FetchTrackingReportsAsync(session);

            // 4. Fetch Approval Reports from FNC010_S20 (Duyệt báo cáo - Chỉ tải Báo cáo đã gửi & Yêu cầu đính chính khi mới đăng nhập)
            var (approvalReports, approvalError) = await FetchApprovalReportsAsync(session, includeApproved: false);

            // 5. Fetch Aggregate Reports from FNC002_S107_1 (Tổng hợp báo cáo)
            var (aggregateReports, aggregateError) = await FetchAggregateReportsAsync(session);

            var combined = new List<ReportItem>();
            combined.AddRange(inputReports);
            combined.AddRange(submitReports);
            combined.AddRange(trackingReports);
            combined.AddRange(approvalReports);
            combined.AddRange(aggregateReports);

            return (combined, string.Empty);
        }
        catch (Exception ex)
        {
            return (reports, $"Lỗi tải danh sách báo cáo: {ex.Message}");
        }
    }

    /// <summary>
    /// Tải danh sách báo cáo cho một mục (Tab) cụ thể khi người dùng bấm Làm mới
    /// Giúp thao tác cực nhanh và không cần gọi cả 5 API
    /// </summary>
    public async Task<(List<ReportItem> Reports, string ErrorMessage)> GetReportsForTabAsync(AuthSession session, int tabIndex, bool includeApproved = false)
    {
        if (!session.IsAuthenticated)
            return (new List<ReportItem>(), "Chưa xác thực phiên đăng nhập");

        try
        {
            return tabIndex switch
            {
                0 => await FetchInputReportsAsync(session),
                1 => await FetchSubmitReportsAsync(session),
                2 => await FetchTrackingReportsAsync(session),
                3 => await FetchApprovalReportsAsync(session, includeApproved: includeApproved),
                4 => await FetchAggregateReportsAsync(session),
                _ => (new List<ReportItem>(), "Mục không hợp lệ")
            };
        }
        catch (Exception ex)
        {
            return (new List<ReportItem>(), $"Lỗi tải dữ liệu mục: {ex.Message}");
        }
    }

    /// <summary>
    /// Tải danh sách báo cáo cần nhập liệu (FNC002_S107_1)
    /// </summary>
    private async Task<(List<ReportItem> Reports, string ErrorMessage)> FetchInputReportsAsync(AuthSession session)
    {
        var reports = new List<ReportItem>();
        try
        {
            var payload = new RestServicePayload
            {
                Func = "ajaxExecuteQueryO",
                Params = new List<object?> { "", "FNC002_S107_1", null },
                Fcode = "FNC002",
                Uuid = session.Uuid,
                Options = new List<QueryOption>
                {
                    new() { Name = "[0]", Value = session.TenantId },
                    new() { Name = "[1]", Value = "-1" },
                    new() { Name = "[2]", Value = "-1" },
                    new() { Name = "[3]", Value = -1 },
                    new() { Name = "[4]", Value = -1 },
                    new() { Name = "[5]", Value = ",1," },
                    new() { Name = "[6]", Value = session.TenantId },
                    new() { Name = "[7]", Value = session.OrgId },
                    new() { Name = "[8]", Value = string.IsNullOrWhiteSpace(session.OrgType) ? "7" : session.OrgType },
                    new() { Name = "[9]", Value = -1 },
                    new() { Name = "[10]", Value = -1 },
                    new() { Name = "[11]", Value = session.OrgId },
                    new() { Name = "[12]", Value = session.TenantId },
                    new() { Name = "[13]", Value = session.UserId },
                    new() { Name = "[14]", Value = session.TenantId },
                    new() { Name = "[15]", Value = session.OrgId },
                    new() { Name = "[16]", Value = session.TenantId },
                    new() { Name = "[17]", Value = session.OrgId },
                    new() { Name = "[18]", Value = session.TenantId },
                    new() { Name = "[19]", Value = session.OrgId },
                    new() { Name = "[20]", Value = ",null," },
                    new() { Name = "[21]", Value = $",{session.UserId}," }
                }
            };

            var rows = await ExecuteRestServiceQueryAsync(session, payload);
            var stt = 1;
            foreach (var row in rows)
            {
                var objId = GetString(row, "OBJ_ID");
                var cleanObjId = NormalizeIdString(objId);

                var rawName = GetFirstNonEmpty(row, "TITLE", "OBJ_NAME", "OBJECT_NAME", "REPORT_NAME", "TEN_BIEU_MAU", "BIEU_MAU", "NAME");
                var rawPeriodType = GetFirstNonEmpty(row, "PERIOD_TYPE_NAME", "PERIOD_NAME", "");

                var rawTimeName = GetString(row, "TIME_NAME");
                var cleanTimeName = Regex.Replace(rawTimeName.Trim(), @"^\s+", "");

                var submitType = GetString(row, "SUBMIT_TYPE");
                var periodTypeName = ResolvePeriodType(cleanTimeName, rawName, rawPeriodType, submitType);
                var resolvedName = ResolveReportName(rawName, periodTypeName);

                var stateIdStr = GetFirstNonEmpty(row, "STATE_ID", "STATUS", "STATUS_ID", "OBJ_STATE");
                int.TryParse(stateIdStr, out var stateId);
                var statusName = GetString(row, "STATUS_NAME", "");

                // Filter: Tab 1 chỉ gồm các báo cáo cần nhập liệu (1, 7, 8, 5, 6, 9)
                // Tuyệt đối loại bỏ các báo cáo đã trình (2), đã gửi (3), đã duyệt (4)
                if (stateId is 2 or 3 or 4 ||
                    statusName.Contains("đã gửi") ||
                    statusName.Contains("đã duyệt") ||
                    statusName.Contains("cấp đơn vị giao") ||
                    statusName.Contains("trình lãnh đạo"))
                {
                    continue;
                }

                var senderOrg = GetFirstNonEmpty(row, "ASSIGN_ORG", "SENDER_NAME", "ORG_NAME", "");
                var reportOrg = GetFirstNonEmpty(row, "RECEIPT_NAME", "ORG_NAME", "");
                var targetOrgId = NormalizeIdString(GetFirstNonEmpty(row, "ORG_ID", "RECEIPT_ID", session.OrgId));
                var rawDec = GetFirstNonEmpty(row, "DECIMAL_PLACES", "DECIMAL_DIGITS", "DECIMAL_DIGIT", "DEC_NUM", "DEC_PLACE", "SCALE", "ROUND", "DECIMAL_CONFIG", "NUM_DECIMAL", "DECIMAL_NUM", "DECIMAL", "NUM_DIGIT", "DEC_DIGIT", "CONFIG_DECIMAL", "DECIMAL_SETTING", "DATA_SCALE", "PRECISION");
                int? decDigits = null;
                if (int.TryParse(rawDec, out var pDec) && pDec >= 0)
                {
                    decDigits = Math.Clamp(pDec, 0, 10);
                }

                var item = new ReportItem
                {
                    Stt = stt++,
                    InputGrantId = NormalizeIdString(GetString(row, "INPUT_GRANT_ID")),
                    ObjId = cleanObjId,
                    ObjCode = GetString(row, "OBJ_CODE"),
                    ObjName = resolvedName,
                    TimeId = NormalizeIdString(GetString(row, "TIME_ID")),
                    TimeName = cleanTimeName,
                    PeriodTypeName = periodTypeName,
                    Status = !string.IsNullOrWhiteSpace(stateIdStr) ? stateIdStr : "7",
                    StatusName = statusName,
                    StateId = stateId != 0 ? stateId : 7,
                    Category = ReportCategoryType.Input,
                    DecimalDigits = decDigits,
                    Note = GetFirstNonEmpty(row, "NOTE", "REASON", "REASON_REJECT", "COMMENT", "DESCR", "OPINION", ""),
                    StartDate = FormatReportDate(GetFirstNonEmpty(row, "START_DATE", "INPUT_FR", "")),
                    EndDate = FormatReportDate(GetFirstNonEmpty(row, "END_DATE", "INPUT_TO", "")),
                    OrgName = senderOrg,
                    SenderOrgName = senderOrg,
                    ReportOrgName = !string.IsNullOrWhiteSpace(reportOrg) ? reportOrg : senderOrg,
                    TargetOrgId = targetOrgId,
                    UserName = GetString(row, "USER_NAME", "")
                };

                reports.Add(item);
            }

            return (reports, string.Empty);
        }
        catch (Exception ex)
        {
            return (reports, ex.Message);
        }
    }

    /// <summary>
    /// Tải danh sách báo cáo đã trình duyệt / gửi cấp trên (FNC010_S22_2)
    /// </summary>
    private async Task<(List<ReportItem> Reports, string ErrorMessage)> FetchSubmitReportsAsync(AuthSession session)
    {
        var reports = new List<ReportItem>();
        try
        {
            var payload = new RestServicePayload
            {
                Func = "ajaxExecuteQueryO",
                Params = new List<object?> { "", "FNC010_S22_2", null },
                Fcode = "FNC010",
                Uuid = session.Uuid,
                Options = new List<QueryOption>
                {
                    new() { Name = "[0]", Value = session.OrgId },
                    new() { Name = "[1]", Value = "-1" },
                    new() { Name = "[2]", Value = "-1" },
                    new() { Name = "[3]", Value = "-1" },
                    new() { Name = "[4]", Value = "-1" },
                    new() { Name = "[5]", Value = "-1" },
                    new() { Name = "[6]", Value = "-1" },
                    new() { Name = "[7]", Value = "-1" },
                    new() { Name = "[8]", Value = "-1" },
                    new() { Name = "[9]", Value = -1 },
                    new() { Name = "[10]", Value = -1 },
                    new() { Name = "[11]", Value = session.TenantId },
                    new() { Name = "[12]", Value = -1 },
                    new() { Name = "[13]", Value = -1 },
                    new() { Name = "[14]", Value = -1 },
                    new() { Name = "[15]", Value = -1 },
                    new() { Name = "[16]", Value = -1 },
                    new() { Name = "[17]", Value = -1 },
                    new() { Name = "[18]", Value = session.TenantId },
                    new() { Name = "[19]", Value = session.UserId }
                }
            };

            var rows = await ExecuteRestServiceQueryAsync(session, payload);
            var stt = 1;
            foreach (var row in rows)
            {
                var objId = GetString(row, "OBJ_ID");
                var cleanObjId = NormalizeIdString(objId);

                var rawName = GetFirstNonEmpty(row, "OBJ_NAME", "TITLE", "OBJECT_NAME", "REPORT_NAME", "TEN_BIEU_MAU", "NAME");
                var rawPeriodType = GetFirstNonEmpty(row, "PERIOD_TYPE_NAME", "PERIOD_NAME", "");

                var rawTimeName = GetString(row, "TIME_NAME");
                var cleanTimeName = Regex.Replace(rawTimeName.Trim(), @"^\s+", "");

                var submitType = GetString(row, "SUBMIT_TYPE");
                var periodTypeName = ResolvePeriodType(cleanTimeName, rawName, rawPeriodType, submitType);
                var resolvedName = ResolveReportName(rawName, periodTypeName);

                var stateIdStr = GetFirstNonEmpty(row, "STATE_ID", "STATUS", "STATUS_ID");
                int.TryParse(stateIdStr, out var stateId);
                var stateName = GetFirstNonEmpty(row, "STATE_NAME", "STATUS_NAME", "");

                var senderOrg = GetFirstNonEmpty(row, "ASSIGN_ORG", "SENDER_NAME", "ORG_NAME", "");
                var reportOrg = GetFirstNonEmpty(row, "RECEIPT_NAME", "ORG_NAME", "");
                var targetOrgId = NormalizeIdString(GetFirstNonEmpty(row, "ORG_ID", "RECEIPT_ID", session.OrgId));

                var rawDec = GetFirstNonEmpty(row, "DECIMAL_PLACES", "DECIMAL_DIGITS", "DECIMAL_DIGIT", "DEC_NUM", "DEC_PLACE", "SCALE", "ROUND", "DECIMAL_CONFIG", "NUM_DECIMAL", "DECIMAL_NUM", "DECIMAL", "NUM_DIGIT", "DEC_DIGIT", "CONFIG_DECIMAL", "DECIMAL_SETTING", "DATA_SCALE", "PRECISION");
                int? decDigits = null;
                if (int.TryParse(rawDec, out var pDec) && pDec >= 0)
                {
                    decDigits = Math.Clamp(pDec, 0, 10);
                }

                var item = new ReportItem
                {
                    Stt = stt++,
                    InputGrantId = NormalizeIdString(GetFirstNonEmpty(row, "ID", "INPUT_GRANT_ID")),
                    ObjId = cleanObjId,
                    ObjCode = GetString(row, "OBJ_CODE"),
                    ObjName = resolvedName,
                    TimeId = NormalizeIdString(GetString(row, "TIME_ID")),
                    TimeName = cleanTimeName,
                    PeriodTypeName = periodTypeName,
                    Status = !string.IsNullOrWhiteSpace(stateIdStr) ? stateIdStr : "2",
                    StatusName = !string.IsNullOrWhiteSpace(stateName) ? stateName : "Đã trình lãnh đạo",
                    StateId = stateId != 0 ? stateId : 2,
                    Category = ReportCategoryType.Submit,
                    DecimalDigits = decDigits,
                    StartDate = FormatReportDate(GetFirstNonEmpty(row, "START_DATE", "INPUT_FR", "")),
                    EndDate = FormatReportDate(GetFirstNonEmpty(row, "END_DATE", "INPUT_TO", "")),
                    OrgName = senderOrg,
                    SenderOrgName = senderOrg,
                    ReportOrgName = !string.IsNullOrWhiteSpace(reportOrg) ? reportOrg : senderOrg,
                    TargetOrgId = targetOrgId,
                    UserName = GetString(row, "USER_NAME", "")
                };

                reports.Add(item);
            }

            return (reports, string.Empty);
        }
        catch (Exception ex)
        {
            return (reports, ex.Message);
        }
    }

    /// <summary>
    /// Tải danh sách báo cáo theo dõi trạng thái (FNC010_S24)
    /// </summary>
    private async Task<(List<ReportItem> Reports, string ErrorMessage)> FetchTrackingReportsAsync(AuthSession session)
    {
        var reports = new List<ReportItem>();
        try
        {
            var payload = new RestServicePayload
            {
                Func = "ajaxExecuteQueryO",
                Params = new List<object?> { "", "FNC010_S24", null },
                Fcode = "FNC010",
                Uuid = session.Uuid,
                Options = new List<QueryOption>
                {
                    new() { Name = "[0]", Value = session.OrgId },
                    new() { Name = "[1]", Value = "-1" },
                    new() { Name = "[2]", Value = "-1" },
                    new() { Name = "[3]", Value = "-1" },
                    new() { Name = "[4]", Value = "-1" },
                    new() { Name = "[5]", Value = "-1" },
                    new() { Name = "[6]", Value = "-1" },
                    new() { Name = "[7]", Value = "-1" },
                    new() { Name = "[8]", Value = "-1" },
                    new() { Name = "[9]", Value = -1 },
                    new() { Name = "[10]", Value = -1 },
                    new() { Name = "[11]", Value = session.OrgId },
                    new() { Name = "[12]", Value = "5" },
                    new() { Name = "[13]", Value = "5" },
                    new() { Name = "[14]", Value = "5" },
                    new() { Name = "[15]", Value = -1 },
                    new() { Name = "[16]", Value = -1 },
                    new() { Name = "[17]", Value = -1 },
                    new() { Name = "[18]", Value = -1 },
                    new() { Name = "[19]", Value = -1 },
                    new() { Name = "[20]", Value = -1 },
                    new() { Name = "[21]", Value = session.UserId }
                }
            };

            var rows = await ExecuteRestServiceQueryAsync(session, payload);
            var stt = 1;
            foreach (var row in rows)
            {
                var objId = GetString(row, "OBJ_ID");
                var cleanObjId = NormalizeIdString(objId);

                var rawName = GetFirstNonEmpty(row, "OBJ_NAME", "TITLE", "OBJECT_NAME", "REPORT_NAME", "TEN_BIEU_MAU", "NAME");
                var rawPeriodType = GetFirstNonEmpty(row, "PERIOD_TYPE_NAME", "PERIOD_NAME", "");

                var rawTimeName = GetString(row, "TIME_NAME");
                var cleanTimeName = Regex.Replace(rawTimeName.Trim(), @"^\s+", "");

                var submitType = GetString(row, "SUBMIT_TYPE");
                var periodTypeName = ResolvePeriodType(cleanTimeName, rawName, rawPeriodType, submitType);
                var resolvedName = ResolveReportName(rawName, periodTypeName);

                var stateIdStr = GetFirstNonEmpty(row, "STATE_ID", "STATUS", "STATUS_ID");
                int.TryParse(stateIdStr, out var stateId);
                var stateName = GetFirstNonEmpty(row, "STATE_NAME", "STATUS_NAME", "");
                var note = GetString(row, "NOTE", "");
                var correctionReqStr = GetString(row, "CORRECTION_REQ", "0");
                int.TryParse(correctionReqStr, out var correctionReq);

                // YÊU CẦU: Trạng thái báo cáo đã gửi CHỈ HIỆN báo cáo đã gửi và báo cáo đã duyệt
                if (stateId != 3 && stateId != 4)
                {
                    var sLower = stateName.ToLower();
                    if (sLower.Contains("đã gửi") || sLower.Contains("da gui"))
                    {
                        stateId = 3;
                    }
                    else if (sLower.Contains("đã duyệt") || sLower.Contains("cấp đơn vị giao") || sLower.Contains("phê duyệt"))
                    {
                        stateId = 4;
                    }
                    else
                    {
                        // Bỏ qua các báo cáo ở trạng thái khác
                        continue;
                    }
                }

                var finalStatusName = stateId == 4 ? "Báo cáo đã được duyệt" : (!string.IsNullOrWhiteSpace(stateName) ? stateName : "Báo cáo đã được gửi");

                var senderOrg = GetFirstNonEmpty(row, "SENDER_NAME", "ASSIGN_ORG", "ORG_NAME", "");
                var reportOrg = GetFirstNonEmpty(row, "RECEIPT_NAME", "ORG_NAME", "");
                var targetOrgId = NormalizeIdString(GetFirstNonEmpty(row, "ORG_ID", "RECEIPT_ID", session.OrgId));

                var rawDec = GetFirstNonEmpty(row, "DECIMAL_PLACES", "DECIMAL_DIGITS", "DECIMAL_DIGIT", "DEC_NUM", "DEC_PLACE", "SCALE", "ROUND", "DECIMAL_CONFIG", "NUM_DECIMAL", "DECIMAL_NUM", "DECIMAL", "NUM_DIGIT", "DEC_DIGIT", "CONFIG_DECIMAL", "DECIMAL_SETTING", "DATA_SCALE", "PRECISION");
                int? decDigits = null;
                if (int.TryParse(rawDec, out var pDec) && pDec >= 0)
                {
                    decDigits = Math.Clamp(pDec, 0, 10);
                }

                var item = new ReportItem
                {
                    Stt = stt++,
                    InputGrantId = NormalizeIdString(GetFirstNonEmpty(row, "ID", "INPUT_GRANT_ID")),
                    ObjId = cleanObjId,
                    ObjCode = GetString(row, "OBJ_CODE"),
                    ObjName = resolvedName,
                    TimeId = NormalizeIdString(GetString(row, "TIME_ID")),
                    TimeName = cleanTimeName,
                    PeriodTypeName = periodTypeName,
                    Status = stateId.ToString(),
                    StatusName = finalStatusName,
                    StateId = stateId,
                    Category = ReportCategoryType.Tracking,
                    DecimalDigits = decDigits,
                    Note = note,
                    CorrectionReq = correctionReq,
                    StartDate = FormatReportDate(GetFirstNonEmpty(row, "START_DATE", "INPUT_FR", "")),
                    EndDate = FormatReportDate(GetFirstNonEmpty(row, "END_DATE2", "END_DATE", "INPUT_TO", "")),
                    ApprovedDate = FormatReportDate(GetFirstNonEmpty(row, "SND_DATE", "TMP", "APPROVED_DATE", "APPROVE_DATE", "DATE_APPROVED", "APPROVE_TIME", "APPROVAL_DATE", "UPDATED_DATE", "UPDATE_DATE", "END_DATE2", "END_DATE", "")),
                    OrgName = senderOrg,
                    SenderOrgName = senderOrg,
                    ReportOrgName = !string.IsNullOrWhiteSpace(reportOrg) ? reportOrg : senderOrg,
                    TargetOrgId = targetOrgId,
                    UserName = GetString(row, "USER_NAME", "")
                };

                reports.Add(item);
            }

            return (reports, string.Empty);
        }
        catch (Exception ex)
        {
            return (reports, ex.Message);
        }
    }

    /// <summary>
    /// Tải danh sách báo cáo cần duyệt / đã duyệt (FNC010_S20)
    /// </summary>
    public async Task<(List<ReportItem> Reports, string ErrorMessage)> FetchApprovalReportsAsync(AuthSession session, bool includeApproved = true)
    {
        var reports = new List<ReportItem>();
        try
        {
            var payload = new RestServicePayload
            {
                Func = "ajaxExecuteQueryO",
                Params = new List<object?> { "", "FNC010_S20", null },
                Fcode = "FNC010",
                Uuid = session.Uuid,
                Options = new List<QueryOption>
                {
                    new() { Name = "[0]", Value = session.OrgId },
                    new() { Name = "[1]", Value = "-1" },
                    new() { Name = "[2]", Value = "-1" },
                    new() { Name = "[3]", Value = "-1" },
                    new() { Name = "[4]", Value = "-1" },
                    new() { Name = "[5]", Value = "-1" },
                    new() { Name = "[6]", Value = "-1" },
                    new() { Name = "[7]", Value = "-1" },
                    new() { Name = "[8]", Value = "-1" },
                    new() { Name = "[9]", Value = -1 },
                    new() { Name = "[10]", Value = -1 },
                    new() { Name = "[11]", Value = session.UserId },
                    new() { Name = "[12]", Value = session.UserId },
                    new() { Name = "[13]", Value = "-1" },
                    new() { Name = "[14]", Value = "-1" },
                    new() { Name = "[15]", Value = -1 },
                    new() { Name = "[16]", Value = -1 },
                    new() { Name = "[17]", Value = -1 },
                    new() { Name = "[18]", Value = -1 },
                    new() { Name = "[19]", Value = -1 },
                    new() { Name = "[20]", Value = -1 },
                    new() { Name = "[21]", Value = session.UserId }
                }
            };

            var rows = await ExecuteRestServiceQueryAsync(session, payload);
            var stt = 1;
            foreach (var row in rows)
            {
                var objId = GetString(row, "OBJ_ID");
                var cleanObjId = NormalizeIdString(objId);

                var rawName = GetFirstNonEmpty(row, "OBJ_NAME", "TITLE", "OBJECT_NAME", "REPORT_NAME", "TEN_BIEU_MAU", "NAME");
                var rawPeriodType = GetFirstNonEmpty(row, "PERIOD_TYPE_NAME", "PERIOD_NAME", "");

                var rawTimeName = GetString(row, "TIME_NAME");
                var cleanTimeName = Regex.Replace(rawTimeName.Trim(), @"^\s+", "");

                var submitType = GetString(row, "SUBMIT_TYPE");
                var periodTypeName = ResolvePeriodType(cleanTimeName, rawName, rawPeriodType, submitType);
                var resolvedName = ResolveReportName(rawName, periodTypeName);

                var stateIdStr = GetFirstNonEmpty(row, "STATE_ID", "STATUS", "STATUS_ID");
                int.TryParse(stateIdStr, out var stateId);
                var stateName = GetFirstNonEmpty(row, "STATE_NAME", "STATUS_NAME", "");
                var note = GetString(row, "NOTE", "");
                var correctionReqStr = GetString(row, "CORRECTION_REQ", "0");
                int.TryParse(correctionReqStr, out var correctionReq);

                // YÊU CẦU: Tại Duyệt báo cáo, CHỈ LẤY ĐÚNG 2 LOẠI BÁO CÁO:
                // 1. Báo cáo đã được gửi (STATE_ID == 3)
                // 2. Báo cáo đã được duyệt cấp đơn vị giao (STATE_ID == 4)
                if (stateId != 3 && stateId != 4)
                {
                    var sLower = stateName.ToLower();
                    if (sLower.Contains("đã gửi") || sLower.Contains("da gui"))
                    {
                        stateId = 3;
                    }
                    else if (sLower.Contains("đã duyệt") || sLower.Contains("cấp đơn vị giao") || sLower.Contains("phê duyệt"))
                    {
                        stateId = 4;
                    }
                    else
                    {
                        // Bỏ qua các loại báo cáo khác
                        continue;
                    }
                }

                // Nếu không tải báo cáo đã duyệt (chỉ tải Báo cáo đã gửi & Yêu cầu đính chính khi mới đăng nhập)
                if (!includeApproved && stateId == 4 && correctionReq == 0)
                {
                    continue;
                }

                var finalStatusName = stateId == 4 ? "Báo cáo đã được duyệt" : "Báo cáo đã được gửi";

                var senderOrg = GetFirstNonEmpty(row, "SENDER_NAME", "ASSIGN_ORG", "ORG_NAME", "");
                var reportOrg = GetFirstNonEmpty(row, "RECEIPT_NAME", "ORG_NAME", "");
                var targetOrgId = NormalizeIdString(GetFirstNonEmpty(row, "ORG_ID", "RECEIPT_ID", ""));

                var rawDec = GetFirstNonEmpty(row, "DECIMAL_PLACES", "DECIMAL_DIGITS", "DECIMAL_DIGIT", "DEC_NUM", "DEC_PLACE", "SCALE", "ROUND", "DECIMAL_CONFIG", "NUM_DECIMAL", "DECIMAL_NUM", "DECIMAL", "NUM_DIGIT", "DEC_DIGIT", "CONFIG_DECIMAL", "DECIMAL_SETTING", "DATA_SCALE", "PRECISION");
                int? decDigits = null;
                if (int.TryParse(rawDec, out var pDec) && pDec >= 0)
                {
                    decDigits = Math.Clamp(pDec, 0, 10);
                }

                var item = new ReportItem
                {
                    Stt = stt++,
                    InputGrantId = NormalizeIdString(GetFirstNonEmpty(row, "ID", "INPUT_GRANT_ID")),
                    ObjId = cleanObjId,
                    ObjCode = GetString(row, "OBJ_CODE"),
                    ObjName = resolvedName,
                    TimeId = NormalizeIdString(GetString(row, "TIME_ID")),
                    TimeName = cleanTimeName,
                    PeriodTypeName = periodTypeName,
                    Status = stateId.ToString(),
                    StatusName = finalStatusName,
                    StateId = stateId,
                    Category = ReportCategoryType.Approval,
                    DecimalDigits = decDigits,
                    Note = note,
                    CorrectionReq = correctionReq,
                    StartDate = FormatReportDate(GetFirstNonEmpty(row, "START_DATE", "INPUT_FR", "")),
                    EndDate = FormatReportDate(GetFirstNonEmpty(row, "END_DATE2", "END_DATE", "INPUT_TO", "")),
                    ApprovedDate = FormatReportDate(GetFirstNonEmpty(row, "SND_DATE", "TMP", "APPROVED_DATE", "APPROVE_DATE", "DATE_APPROVED", "APPROVE_TIME", "APPROVAL_DATE", "UPDATED_DATE", "UPDATE_DATE", "END_DATE2", "END_DATE", "")),
                    OrgName = senderOrg,
                    SenderOrgName = senderOrg,
                    ReportOrgName = reportOrg,
                    TargetOrgId = targetOrgId,
                    UserName = GetString(row, "USER_NAME", "")
                };

                reports.Add(item);
            }

            return (reports, string.Empty);
        }
        catch (Exception ex)
        {
            return (reports, ex.Message);
        }
    }

    /// <summary>
    /// Tải danh sách báo cáo tổng hợp (FNC002_S08 - Danh sách báo cáo tổng hợp)
    /// Sử dụng API khác biệt hoàn toàn so với FNC002_S107_1 của Nhập báo cáo số liệu
    /// </summary>
    private async Task<(List<ReportItem> Reports, string ErrorMessage)> FetchAggregateReportsAsync(AuthSession session)
    {
        var reports = new List<ReportItem>();
        try
        {
            var orgType = string.IsNullOrWhiteSpace(session.OrgType) ? "2" : session.OrgType;

            var payload = new RestServicePayload
            {
                Func = "ajaxExecuteQueryO",
                Params = new List<object?> { "", "FNC002_S08", null },
                Fcode = "FNC002",
                Uuid = session.Uuid,
                Options = new List<QueryOption>
                {
                    new() { Name = "[0]", Value = session.TenantId },
                    new() { Name = "[1]", Value = "-1" },
                    new() { Name = "[2]", Value = "-1" },
                    new() { Name = "[3]", Value = -1 },
                    new() { Name = "[4]", Value = -1 },
                    new() { Name = "[5]", Value = "1" },
                    new() { Name = "[6]", Value = session.TenantId },
                    new() { Name = "[7]", Value = session.OrgId },
                    new() { Name = "[8]", Value = orgType },
                    new() { Name = "[9]", Value = orgType },
                    new() { Name = "[10]", Value = -1 },
                    new() { Name = "[11]", Value = -1 },
                    new() { Name = "[12]", Value = session.OrgId }
                }
            };

            var rows = await ExecuteRestServiceQueryAsync(session, payload);
            var stt = 1;
            foreach (var row in rows)
            {
                var objId = GetString(row, "OBJ_ID");
                var cleanObjId = NormalizeIdString(objId);

                var rawName = GetFirstNonEmpty(row, "TITLE", "OBJ_NAME", "OBJECT_NAME", "REPORT_NAME", "NAME");
                var rawPeriodType = GetFirstNonEmpty(row, "PERIOD_TYPE_NAME", "PERIOD_NAME", "");

                var rawTimeName = GetString(row, "TIME_NAME");
                var cleanTimeName = Regex.Replace(rawTimeName.Trim(), @"^\s+", "");

                var submitType = GetString(row, "SUBMIT_TYPE");
                var periodTypeName = ResolvePeriodType(cleanTimeName, rawName, rawPeriodType, submitType);
                var resolvedName = ResolveReportName(rawName, periodTypeName);

                // FNC002_S08 trả về STATUS dạng text (VD: "Đã giao", "Đang nhập liệu/tổng hợp")
                var statusText = GetString(row, "STATUS", "");
                var stateId = ResolveStateIdFromStatusText(statusText);

                var senderOrg = GetFirstNonEmpty(row, "ASSIGN_ORG", "SENDER_NAME", "ORG_NAME", "");
                var reportOrg = GetFirstNonEmpty(row, "RECEIPT_NAME", "ORG_NAME", senderOrg);
                var targetOrgId = NormalizeIdString(GetFirstNonEmpty(row, "ORG_ID", "RECEIPT_ID", session.OrgId));
                var programName = GetString(row, "PROGRAM_NAME", "");

                var rawDec = GetFirstNonEmpty(row, "DECIMAL_PLACES", "DECIMAL_DIGITS", "DECIMAL_DIGIT", "DEC_NUM", "DEC_PLACE", "SCALE", "ROUND", "DECIMAL_CONFIG", "NUM_DECIMAL", "DECIMAL_NUM", "DECIMAL", "NUM_DIGIT", "DEC_DIGIT", "CONFIG_DECIMAL", "DECIMAL_SETTING", "DATA_SCALE", "PRECISION");
                int? decDigits = null;
                if (int.TryParse(rawDec, out var pDec) && pDec >= 0)
                {
                    decDigits = Math.Clamp(pDec, 0, 10);
                }

                var item = new ReportItem
                {
                    Stt = stt++,
                    InputGrantId = NormalizeIdString(GetFirstNonEmpty(row, "KEY", "INPUT_GRANT_ID", "ID")),
                    ObjId = cleanObjId,
                    ObjCode = GetString(row, "OBJ_CODE"),
                    ObjName = resolvedName,
                    TimeId = NormalizeIdString(GetString(row, "TIME_ID")),
                    TimeName = cleanTimeName,
                    PeriodTypeName = periodTypeName,
                    Status = stateId.ToString(),
                    StatusName = statusText,
                    StateId = stateId,
                    Category = ReportCategoryType.Aggregate,
                    DecimalDigits = decDigits,
                    Note = GetString(row, "NOTE", ""),
                    StartDate = FormatReportDate(GetFirstNonEmpty(row, "START_DATE", "INPUT_FR", "")),
                    EndDate = FormatReportDate(GetFirstNonEmpty(row, "END_DATE", "INPUT_TO", "")),
                    OrgName = senderOrg,
                    SenderOrgName = senderOrg,
                    ReportOrgName = reportOrg,
                    TargetOrgId = targetOrgId,
                    UserName = GetString(row, "USER_NAME", "")
                };

                reports.Add(item);
            }

            return (reports, string.Empty);
        }
        catch (Exception ex)
        {
            return (reports, ex.Message);
        }
    }

    /// <summary>
    /// Chuyển đổi STATUS text từ FNC002_S08 sang StateId số
    /// </summary>
    private static int ResolveStateIdFromStatusText(string statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText))
            return 1;

        var lower = statusText.ToLower();

        if (lower.Contains("đã duyệt"))
            return 4;
        if (lower.Contains("đã gửi"))
            return 3;
        if (lower.Contains("trình") || lower.Contains("lãnh đạo"))
            return 2;
        if (lower.Contains("từ chối"))
        {
            if (lower.Contains("cấp đơn vị giao"))
                return 8;
            return 5;
        }
        if (lower.Contains("đính chính"))
            return 6;
        if (lower.Contains("nhập liệu") || lower.Contains("tổng hợp"))
            return 7;
        if (lower.Contains("đã giao"))
            return 1;

        return 1;
    }

    private static async Task<List<JsonElement>> ExecuteRestServiceQueryAsync(AuthSession session, RestServicePayload payload)
    {
        var resultList = new List<JsonElement>();

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var jsonBody = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(session.ApiUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            return resultList;
        }

        var respText = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respText);

        JsonElement rowsElement;
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("result", out var resProp))
        {
            if (resProp.ValueKind == JsonValueKind.String)
            {
                var innerJson = resProp.GetString() ?? "[]";
                using var innerDoc = JsonDocument.Parse(innerJson);
                rowsElement = innerDoc.RootElement.Clone();
            }
            else
            {
                rowsElement = resProp.Clone();
            }
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            rowsElement = doc.RootElement.Clone();
        }
        else
        {
            return resultList;
        }

        if (rowsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rowsElement.EnumerateArray())
            {
                resultList.Add(r.Clone());
            }
        }

        return resultList;
    }

    private static string NormalizeIdString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim();
        if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return ((long)Math.Round(d)).ToString();
        }
        return s;
    }

    public static string ResolvePeriodType(string timeName, string reportName, string explicitPeriodName, string submitType)
    {
        if (!string.IsNullOrWhiteSpace(explicitPeriodName) &&
            explicitPeriodName != "null" &&
            explicitPeriodName != "-1" &&
            explicitPeriodName != "0")
        {
            var expLower = explicitPeriodName.Trim().ToLower();
            if (expLower.Contains("6 tháng") || expLower.Contains("6thang") || expLower.Contains("bán niên"))
                return "6 Tháng";
            if (expLower.Contains("quý") || expLower.Contains("quy"))
                return "Quý";
            if (expLower.Contains("tháng") || expLower.Contains("thang"))
                return "Tháng";
            if (expLower.Contains("tuần") || expLower.Contains("tuan"))
                return "Tuần";
            if (expLower.Contains("năm") || expLower.Contains("nam"))
                return "Năm";
            if (expLower.Contains("ngày") || expLower.Contains("ngay"))
                return "Ngày";

            return explicitPeriodName.Trim();
        }

        var combined = $"{timeName} {reportName}".ToLower();

        if (combined.Contains("6 tháng") || combined.Contains("6thang") || combined.Contains("bán niên") || combined.Contains("ban nien"))
            return "6 Tháng";
        if (combined.Contains("quý") || combined.Contains("quy") || Regex.IsMatch(combined, @"\b(quý|quy|q)[1-4]\b"))
            return "Quý";
        if (combined.Contains("tháng") || combined.Contains("thang") || Regex.IsMatch(combined, @"\b(tháng|thang|t)(0?[1-9]|1[0-2])\b"))
            return "Tháng";
        if (combined.Contains("tuần") || combined.Contains("tuan"))
            return "Tuần";
        if (combined.Contains("năm") || combined.Contains("nam"))
            return "Năm";
        if (combined.Contains("ngày") || combined.Contains("ngay"))
            return "Ngày";

        var st = (submitType ?? "").Trim();
        return st switch
        {
            "1" or "5" => "Ngày",
            "6" => "Tuần",
            "2" => "Tháng",
            "3" => "Quý",
            "4" => "Năm",
            "8" => "6 Tháng",
            _ => "Tháng"
        };
    }

    private static string ResolveReportName(string rawName, string periodTypeName)
    {
        if (!string.IsNullOrWhiteSpace(rawName) && rawName != "null")
            return rawName;

        if (!string.IsNullOrWhiteSpace(periodTypeName))
            return $"Báo cáo ({periodTypeName})";

        return "Báo cáo số liệu";
    }

    private static string FormatReportDate(string rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
            return string.Empty;

        var cleaned = rawDate.Trim();
        var parts = cleaned.Split(new[] { '/', '-', ' ', ':', '.' }, StringSplitOptions.RemoveEmptyEntries);

        // Case 1: yyyy/MM/dd/HH/mm/ss or yyyy-MM-dd HH:mm:ss
        if (parts.Length >= 3 && parts[0].Length == 4 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month) && int.TryParse(parts[2], out var day))
        {
            if (parts.Length >= 6 && int.TryParse(parts[3], out var h) && int.TryParse(parts[4], out var min) && int.TryParse(parts[5], out var sec))
            {
                // Nếu giờ, phút, giây đều là 0 hoặc 23:59:59 (thường là mốc đầu/cuối ngày mặc định), chỉ hiển thị dd/MM/yyyy
                if ((h == 0 && min == 0 && sec == 0) || (h == 23 && min == 59 && sec == 59))
                {
                    return $"{day:D2}/{month:D2}/{year:D4}";
                }
                return $"{day:D2}/{month:D2}/{year:D4} {h:D2}:{min:D2}:{sec:D2}";
            }
            return $"{day:D2}/{month:D2}/{year:D4}";
        }

        // Case 2: dd/MM/yyyy/HH/mm/ss or dd/MM/yyyy HH:mm:ss (Ví dụ: "15/09/2026 09:09:00" từ SND_DATE/UPDATE_DATE)
        if (parts.Length >= 3 && parts[2].Length == 4 && int.TryParse(parts[0], out var d) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var y))
        {
            if (parts.Length >= 5 && int.TryParse(parts[3], out var h) && int.TryParse(parts[4], out var min))
            {
                var sec = (parts.Length >= 6 && int.TryParse(parts[5], out var s)) ? s : 0;
                if ((h == 0 && min == 0 && sec == 0) || (h == 23 && min == 59 && sec == 59))
                {
                    return $"{d:D2}/{m:D2}/{y:D4}";
                }
                return $"{d:D2}/{m:D2}/{y:D4} {h:D2}:{min:D2}:{sec:D2}";
            }
            return $"{d:D2}/{m:D2}/{y:D4}";
        }

        // Fallback DateTime parser
        if (DateTime.TryParse(cleaned, out var dt))
        {
            if (dt.TimeOfDay != TimeSpan.Zero && dt.TimeOfDay != new TimeSpan(23, 59, 59))
            {
                return dt.ToString("dd/MM/yyyy HH:mm:ss");
            }
            return dt.ToString("dd/MM/yyyy");
        }

        return cleaned;
    }

    private static string GetFirstNonEmpty(JsonElement elem, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            var val = GetString(elem, name);
            if (!string.IsNullOrWhiteSpace(val) && val != "null")
                return val;
        }
        return string.Empty;
    }

    private static string GetString(JsonElement elem, string propName, string defaultVal = "")
    {
        if (elem.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? defaultVal;
            if (prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt64(out var longVal))
                    return longVal.ToString();
                if (prop.TryGetDouble(out var dblVal))
                    return ((long)Math.Round(dblVal)).ToString();
                return prop.ToString();
            }
        }
        return defaultVal;
    }

    /// <summary>
    /// Lấy danh sách các cột động theo cấu trúc của báo cáo (FNC003_S13 + FNC003_S315 / FNC003_S312)
    /// </summary>
    public async Task<(List<ColumnHeaderInfo> Headers, bool IsUnitVisible, bool IsSttVisible, bool IsCodeVisible, double NameWidth, double SttWidth, double CodeWidth, double UnitWidth)> GetReportHeadersAsync(AuthSession session, string objId, string attrId, int? defaultDecimalDigits = null)
    {
        var headers = new List<ColumnHeaderInfo>();
        bool isUnitVisible = true;
        bool isSttVisible = true;
        bool isCodeVisible = false;
        double nameWidth = 400;
        double sttWidth = 65;
        double codeWidth = 180;
        double unitWidth = 110;
        int? s101DecPlaces = null;
        if (!session.IsAuthenticated) return (headers, isUnitVisible, isSttVisible, isCodeVisible, nameWidth, sttWidth, codeWidth, unitWidth);

        try
        {
            var tenantId = session.TenantId;

            // 0. Gọi FNC003_S101 để trích xuất cấu hình hiển thị cột gốc của biểu mẫu (INDEX_SHOW, IND_CODE_SHOW, IND_UNIT_SHOW, Cấu hình số thập phân)
            try
            {
                var s101Payload = new RestServicePayload
                {
                    Func = "ajaxExecuteQueryO",
                    Params = new List<object?> { "", "FNC003_S101", null },
                    Fcode = "FNC003",
                    Uuid = session.Uuid,
                    Options = new List<QueryOption>
                    {
                        new() { Name = "[0]", Value = objId },
                        new() { Name = "[1]", Value = tenantId }
                    }
                };
                var s101Rows = await ExecuteRestServiceQueryAsync(session, s101Payload);
                if (s101Rows.Count > 0)
                {
                    var s101 = s101Rows[0];
                    if (double.TryParse(GetString(s101, "INDEX_WIDTH"), out var iw) && iw >= 30) sttWidth = iw;
                    if (double.TryParse(GetString(s101, "IND_CODE_WIDTH"), out var cw) && cw >= 30) codeWidth = cw;
                    if (double.TryParse(GetString(s101, "IND_UNIT_WIDTH"), out var uw) && uw >= 30) unitWidth = uw;
                    if (double.TryParse(GetString(s101, "IND_NAME_WIDTH"), out var nw) && nw >= 50) nameWidth = nw;

                    var s101Dec = GetFirstNonEmpty(s101, "DECIMAL_PLACES", "DECIMAL_DIGITS", "DECIMAL_DIGIT", "DEC_NUM", "DEC_PLACE", "SCALE", "ROUND", "DECIMAL_CONFIG", "NUM_DECIMAL", "DECIMAL_NUM", "DECIMAL", "NUM_DIGIT", "DEC_DIGIT", "CONFIG_DECIMAL", "DECIMAL_SETTING", "DATA_SCALE", "PRECISION");
                    if (int.TryParse(s101Dec, out var sDec) && sDec >= 0)
                    {
                        s101DecPlaces = Math.Clamp(sDec, 0, 10);
                    }
                }
            }
            catch { }

            // 1. Call FNC003_S13 (sqlGetAttrHidden) to get hidden attribute IDs
            var s13Payload = new RestServicePayload
            {
                Func = "ajaxExecuteQueryO",
                Params = new List<object?> { "", "FNC003_S13", null },
                Fcode = "FNC003",
                Uuid = session.Uuid,
                Options = new List<QueryOption>
                {
                    new() { Name = "[0]", Value = objId }
                }
            };

            var s13Rows = await ExecuteRestServiceQueryAsync(session, s13Payload);
            var hiddenAttrIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hiddenList = new List<string>();

            foreach (var r in s13Rows)
            {
                var hId = GetString(r, "ATTR_ID");
                var hCode = GetFirstNonEmpty(r, "ATTR_CODE", "CODE", "FIELD", "FLD_CODE");
                var hName = GetFirstNonEmpty(r, "ATTR_NAME", "NAME", "SHORT_NAME", "TITLE").ToLower();
                if (!string.IsNullOrWhiteSpace(hId) && hId != "null")
                {
                    hiddenAttrIds.Add(hId);
                    hiddenList.Add(hId);
                }
                if (hCode.Equals("IND_UNIT", StringComparison.OrdinalIgnoreCase) || hCode.Equals("UNIT", StringComparison.OrdinalIgnoreCase) || hName.Contains("đơn vị tính") || hName.Contains("don vi tinh") || hName.Contains("đvt") || hName.Contains("dvt"))
                {
                    isUnitVisible = false;
                }
                if (hCode.Equals("RN", StringComparison.OrdinalIgnoreCase) || hCode.Equals("STT", StringComparison.OrdinalIgnoreCase) || hCode.Equals("IND_INDEX", StringComparison.OrdinalIgnoreCase) || hCode.Equals("ORDER_BY", StringComparison.OrdinalIgnoreCase) || hCode.Equals("STT_DISPLAY", StringComparison.OrdinalIgnoreCase) || hName.Contains("stt") || hName.Contains("số thứ tự") || hName.Contains("so thu tu") || hName.Contains("thứ tự") || hName.Contains("thu tu") || hName.Contains("chỉ mục") || hName.Contains("chi muc"))
                {
                    isSttVisible = false;
                }
                if (hCode.Equals("IND_CODE", StringComparison.OrdinalIgnoreCase) || hCode.Equals("CODE", StringComparison.OrdinalIgnoreCase) || hName.Contains("mã chỉ tiêu") || hName.Contains("ma chi tieu") || hName.Contains("mã số") || hName.Contains("ma so"))
                {
                    isCodeVisible = false;
                }
            }

            string hiddenStr = hiddenList.Count > 0 ? $",{string.Join(",", hiddenList)}," : "";

            // 2. Call FNC003_S315 (if hidden attributes exist) or FNC003_S312
            RestServicePayload headerPayload;
            if (!string.IsNullOrWhiteSpace(hiddenStr))
            {
                headerPayload = new RestServicePayload
                {
                    Func = "ajaxExecuteQueryO",
                    Params = new List<object?> { "", "FNC003_S315", null },
                    Fcode = "FNC003",
                    Uuid = session.Uuid,
                    Options = new List<QueryOption>
                    {
                        new() { Name = "[0]", Value = objId },
                        new() { Name = "[1]", Value = tenantId },
                        new() { Name = "[2]", Value = objId },
                        new() { Name = "[3]", Value = tenantId },
                        new() { Name = "[4]", Value = attrId },
                        new() { Name = "[5]", Value = objId },
                        new() { Name = "[6]", Value = tenantId },
                        new() { Name = "[7]", Value = objId },
                        new() { Name = "[8]", Value = tenantId },
                        new() { Name = "[9]", Value = attrId },
                        new() { Name = "[10]", Value = hiddenStr }
                    }
                };
            }
            else
            {
                headerPayload = new RestServicePayload
                {
                    Func = "ajaxExecuteQueryO",
                    Params = new List<object?> { "", "FNC003_S312", null },
                    Fcode = "FNC003",
                    Uuid = session.Uuid,
                    Options = new List<QueryOption>
                    {
                        new() { Name = "[0]", Value = objId },
                        new() { Name = "[1]", Value = tenantId },
                        new() { Name = "[2]", Value = objId },
                        new() { Name = "[3]", Value = tenantId },
                        new() { Name = "[4]", Value = attrId },
                        new() { Name = "[5]", Value = objId },
                        new() { Name = "[6]", Value = tenantId },
                        new() { Name = "[7]", Value = objId },
                        new() { Name = "[8]", Value = tenantId },
                        new() { Name = "[9]", Value = attrId }
                    }
                };
            }

            var headerRows = await ExecuteRestServiceQueryAsync(session, headerPayload);
            if (headerRows.Count == 0 && !string.IsNullOrWhiteSpace(hiddenStr))
            {
                // Fallback to FNC003_S312 if FNC003_S315 returned 0 rows
                try
                {
                    var fallbackPayload = new RestServicePayload
                    {
                        Func = "ajaxExecuteQueryO",
                        Params = new List<object?> { "", "FNC003_S312", null },
                        Fcode = "FNC003",
                        Uuid = session.Uuid,
                        Options = new List<QueryOption>
                        {
                            new() { Name = "[0]", Value = objId },
                            new() { Name = "[1]", Value = tenantId },
                            new() { Name = "[2]", Value = objId },
                            new() { Name = "[3]", Value = tenantId },
                            new() { Name = "[4]", Value = attrId },
                            new() { Name = "[5]", Value = objId },
                            new() { Name = "[6]", Value = tenantId },
                            new() { Name = "[7]", Value = objId },
                            new() { Name = "[8]", Value = tenantId },
                            new() { Name = "[9]", Value = attrId }
                        }
                    };
                    var s312Rows = await ExecuteRestServiceQueryAsync(session, fallbackPayload);
                    if (s312Rows.Count > 0)
                        headerRows = s312Rows;
                }
                catch { }
            }

            bool isSttFound = false;
            bool isUnitFound = false;
            bool isCodeFound = false;

            // Filter leaf headers that are visible and not hidden
            foreach (var h in headerRows)
            {
                var isLeaf = GetFirstNonEmpty(h, "IS_LEAF", "CNT_LEAF");
                if (isLeaf != "1" && isLeaf != "1.0") continue;

                var isHidden = GetString(h, "IS_HIDDEN", "0");
                var visible = GetString(h, "VISIBLE", "1");
                var isHideField = GetString(h, "IS_HIDE", "0");
                var isShowField = GetString(h, "IS_SHOW", "1");
                var showField = GetString(h, "SHOW", "1");
                var statusField = GetString(h, "STATUS", "1");
                var colAttrId = GetString(h, "ATTR_ID");
                var colWidthStr = GetString(h, "COL_WIDTH", "0");
                _ = double.TryParse(colWidthStr, out var w);

                var isHide = isHidden == "1" || isHidden.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                             visible == "0" || visible.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                             isHideField == "1" || isHideField.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                             isShowField == "0" || isShowField.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                             showField == "0" || showField.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                             statusField == "0" ||
                             hiddenAttrIds.Contains(colAttrId);

                var attrCode = GetFirstNonEmpty(h, "ATTR_CODE", "CODE", "FIELD", "FLD_CODE");
                var attrName = GetFirstNonEmpty(h, "SHORT_NAME", "ATTR_NAME", "NAME", "TITLE");
                var attrNameLower = attrName.ToLower();

                bool isSttCol = attrCode.Equals("RN", StringComparison.OrdinalIgnoreCase) ||
                                attrCode.Equals("STT", StringComparison.OrdinalIgnoreCase) ||
                                attrCode.Equals("IND_INDEX", StringComparison.OrdinalIgnoreCase) ||
                                attrCode.Equals("ORDER_BY", StringComparison.OrdinalIgnoreCase) ||
                                attrCode.Equals("STT_DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                                attrCode.Equals("INDEX", StringComparison.OrdinalIgnoreCase) ||
                                attrNameLower.Equals("stt") ||
                                attrNameLower.Contains("số thứ tự") ||
                                attrNameLower.Contains("so thu tu") ||
                                attrNameLower.Equals("thứ tự") ||
                                attrNameLower.Equals("thu tu");

                bool isUnitCol = attrCode.Equals("IND_UNIT", StringComparison.OrdinalIgnoreCase) ||
                                 attrCode.Equals("UNIT", StringComparison.OrdinalIgnoreCase) ||
                                 attrNameLower.Contains("đơn vị tính") || attrNameLower.Contains("don vi tinh") ||
                                 attrNameLower.Equals("đvt") || attrNameLower.Equals("dvt");

                bool isCodeCol = attrCode.Equals("IND_CODE", StringComparison.OrdinalIgnoreCase) ||
                                 attrCode.Equals("CODE", StringComparison.OrdinalIgnoreCase) ||
                                 attrNameLower.Contains("mã chỉ tiêu") || attrNameLower.Contains("ma chi tieu") ||
                                 attrNameLower.Contains("mã số") || attrNameLower.Contains("ma so");

                bool isNameCol = attrCode.Equals("IND_NAME", StringComparison.OrdinalIgnoreCase) ||
                                 attrCode.Equals("NAME", StringComparison.OrdinalIgnoreCase) ||
                                 attrNameLower.Contains("tên chỉ tiêu") || attrNameLower.Contains("ten chi tieu") ||
                                 attrNameLower.Contains("nhiệm vụ") || attrNameLower.Contains("nhiem vu");

                if (isSttCol)
                {
                    isSttFound = true;
                    isSttVisible = !isHide;
                    if (w >= 40) sttWidth = w;
                    continue;
                }
                if (isUnitCol)
                {
                    isUnitFound = true;
                    isUnitVisible = !isHide;
                    if (w >= 50) unitWidth = w;
                    continue;
                }
                if (isCodeCol)
                {
                    isCodeFound = true;
                    isCodeVisible = !isHide;
                    if (w >= 50) codeWidth = w;
                    continue;
                }
                if (isNameCol)
                {
                    if (w >= 80) nameWidth = w;
                    continue;
                }

                if (isHide) continue;

                var fldCode = GetString(h, "FLD_CODE");
                var field = GetString(h, "FIELD");

                var rawDataType = GetFirstNonEmpty(h, "DATA_TYPE", "DATA_TYPE_ID", "ATTR_TYPE", "FIELD_TYPE", "TYPE", "");
                var rawAlign = GetFirstNonEmpty(h, "ALIGN", "TEXT_ALIGN", "ALIGNMENT", "");
                var rawFontSize = GetFirstNonEmpty(h, "FONT_SIZE", "SIZE", "12");
                _ = double.TryParse(rawFontSize, out var fontSize);
                if (fontSize <= 0) fontSize = 12;

                var rawDec = GetFirstNonEmpty(h, "DECIMAL_PLACES", "DECIMAL_DIGITS", "DECIMAL_DIGIT", "DEC_NUM", "DEC_PLACE", "SCALE", "ROUND", "DECIMAL_CONFIG", "NUM_DECIMAL", "DECIMAL_NUM", "DECIMAL", "NUM_DIGIT", "DEC_DIGIT", "CONFIG_DECIMAL", "DECIMAL_SETTING", "DATA_SCALE", "PRECISION");
                int decPlaces = 4;
                if (int.TryParse(rawDec, out var parsedDec) && parsedDec >= 0)
                {
                    decPlaces = Math.Clamp(parsedDec, 0, 10);
                }
                else if (s101DecPlaces.HasValue)
                {
                    decPlaces = s101DecPlaces.Value;
                }
                else if (defaultDecimalDigits.HasValue)
                {
                    decPlaces = defaultDecimalDigits.Value;
                }

                // Tên trường khác nhau giữa các cấu hình IOC. Giá trị 0/thiếu nghĩa là
                // không giới hạn từ API, nên áp dụng mức an toàn 1.000 ký tự theo yêu cầu UI.
                var rawMaxLength = GetFirstNonEmpty(h,
                    "MAX_LENGTH", "MAX_LEN", "MAXLENGTH", "DATA_LENGTH", "DATA_LEN",
                    "FIELD_LENGTH", "COLUMN_LENGTH", "ATTR_LENGTH", "CHAR_LENGTH",
                    "CHARACTER_MAXIMUM_LENGTH", "MAX_CHAR", "MAX_CHARS", "LENGTH");
                var maxLength = int.TryParse(rawMaxLength, out var parsedMaxLength) && parsedMaxLength > 0
                    ? parsedMaxLength
                    : 1000;

                string resolvedDataType = "Real";
                var dtLower = rawDataType.ToLower();
                if (dtLower == "1" || dtLower.Contains("nguyên") || dtLower.Contains("integer") || dtLower.Contains("int"))
                {
                    resolvedDataType = "Integer";
                }
                else if (dtLower == "3" || dtLower.Contains("chuỗi") || dtLower.Contains("string") || dtLower.Contains("text") || dtLower.Contains("varchar") || dtLower.Contains("char") || dtLower.Contains("date"))
                {
                    resolvedDataType = "String";
                }
                else if (dtLower == "2" || dtLower.Contains("thực") || dtLower.Contains("real") || dtLower.Contains("float") || dtLower.Contains("double") || dtLower.Contains("decimal") || dtLower.Contains("number"))
                {
                    resolvedDataType = "Real";
                }

                string resolvedAlign = "Right";
                var alLower = rawAlign.ToLower();
                if (alLower == "1" || alLower.Contains("left") || alLower.Contains("trái"))
                {
                    resolvedAlign = "Left";
                }
                else if (alLower == "2" || alLower.Contains("center") || alLower.Contains("giữa"))
                {
                    resolvedAlign = "Center";
                }
                else if (alLower == "3" || alLower.Contains("right") || alLower.Contains("phải"))
                {
                    resolvedAlign = "Right";
                }
                else
                {
                    resolvedAlign = resolvedDataType == "String" ? "Left" : "Right";
                }

                headers.Add(new ColumnHeaderInfo
                {
                    AttrId = colAttrId,
                    AttrCode = !string.IsNullOrWhiteSpace(attrCode) ? attrCode : "CTKTXH",
                    FldCode = fldCode,
                    HeaderName = !string.IsNullOrWhiteSpace(attrName) ? attrName : "Giá trị",
                    Field = !string.IsNullOrWhiteSpace(field) ? field : fldCode,
                    ColWidth = w > 50 ? w : (resolvedDataType == "String" ? 220 : 150),
                    DataType = resolvedDataType,
                    DecimalDigits = decPlaces,
                    MaxLength = maxLength,
                    Align = resolvedAlign,
                    FontSize = fontSize
                });
            }

            if (headerRows.Count > 0)
            {
                if (!isSttFound) isSttVisible = false;
                if (!isUnitFound) isUnitVisible = false;
                if (!isCodeFound) isCodeVisible = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi lấy header động: {ex.Message}");
        }

        // If no dynamic columns returned from API, fallback to default 1 "Giá trị" column
        if (headers.Count == 0)
        {
            headers.Add(new ColumnHeaderInfo
            {
                AttrId = attrId,
                AttrCode = "CTKTXH",
                FldCode = "FN01",
                HeaderName = "Giá trị",
                Field = "FN01",
                ColWidth = 160,
                DataType = "Real",
                DecimalDigits = s101DecPlaces ?? defaultDecimalDigits ?? 4,
                Align = "Right",
                FontSize = 12
            });
        }

        return (headers, isUnitVisible, isSttVisible, isCodeVisible, nameWidth, sttWidth, codeWidth, unitWidth);
    }

    /// <summary>
    /// Lấy danh sách chỉ tiêu chi tiết của báo cáo kèm dữ liệu đã nhập (FNC003_P03 + FNC003_S315 + FNC003_P105 + FNC003_S202)
    /// </summary>
    public async Task<(List<IndicatorItem> Indicators, List<ColumnHeaderInfo> Headers, string AttrId, string TopIndId, string SubmitType, bool IsUnitVisible, bool IsSttVisible, bool IsCodeVisible, double NameWidth, double SttWidth, double CodeWidth, double UnitWidth, string ErrorMessage)> GetReportIndicatorsAsync(AuthSession session, ReportItem report)
    {
        var indicators = new List<IndicatorItem>();
        var headers = new List<ColumnHeaderInfo>();

        if (!session.IsAuthenticated)
            return (indicators, headers, "", "", "", true, true, false, 400, 65, 180, 110, "Chưa xác thực phiên đăng nhập");

        try
        {
            var objId = report.ObjId;
            var targetOrgId = !string.IsNullOrWhiteSpace(report.TargetOrgId) && report.TargetOrgId != "-1" && report.TargetOrgId != "0"
                ? report.TargetOrgId
                : session.OrgId;
            var tenantId = session.TenantId;
            var periodId = report.TimeId;

            // 1. Call FNC003_P03 to get ATTR_ID and TOP_IND_ID
            var p03Payload = new RestServicePayload
            {
                Func = IocOperations.CallSpQuery,
                Params = new List<object?> { IocProcedures.GetStructure, objId, 0 },
                Fcode = IocFunctionCodes.Fnc003,
                Uuid = session.Uuid
            };

            var p03Rows = await ExecuteRestServiceQueryAsync(session, p03Payload);
            string attrId = "";
            string topIndId = "";
            string submitType = "";
            double? p03SttWidth = null;
            double? p03CodeWidth = null;
            double? p03UnitWidth = null;
            double? p03NameWidth = null;

            if (p03Rows.Count > 0)
            {
                var row0 = p03Rows[0];
                var fetchedAttr = GetString(row0, IocFields.AttrId);
                var fetchedInd = GetString(row0, IocFields.IndId);
                var fetchedSubmit = GetString(row0, IocFields.SubmitType);

                if (!string.IsNullOrWhiteSpace(fetchedAttr) && fetchedAttr != "null")
                    attrId = fetchedAttr;
                if (!string.IsNullOrWhiteSpace(fetchedInd) && fetchedInd != "null")
                    topIndId = fetchedInd;
                if (!string.IsNullOrWhiteSpace(fetchedSubmit) && fetchedSubmit != "null" && fetchedSubmit != "0")
                    submitType = fetchedSubmit;

                var p03Dec = GetFirstNonEmpty(row0, "DECIMAL_PLACES", "DECIMAL_DIGITS", "DECIMAL_DIGIT", "DEC_NUM", "DEC_PLACE", "SCALE", "ROUND", "DECIMAL_CONFIG", "NUM_DECIMAL", "DECIMAL_NUM", "DECIMAL", "NUM_DIGIT", "DEC_DIGIT", "CONFIG_DECIMAL", "DECIMAL_SETTING", "DATA_SCALE", "PRECISION");
                if (int.TryParse(p03Dec, out var pDec) && pDec >= 0)
                {
                    report.DecimalDigits = Math.Clamp(pDec, 0, 10);
                }

                if (double.TryParse(GetString(row0, "INDEX_WIDTH"), out var iw) && iw >= 30) p03SttWidth = iw;
                if (double.TryParse(GetString(row0, "IND_CODE_WIDTH"), out var cw) && cw >= 30) p03CodeWidth = cw;
                if (double.TryParse(GetString(row0, "IND_UNIT_WIDTH"), out var uw) && uw >= 30) p03UnitWidth = uw;
                if (double.TryParse(GetString(row0, "IND_NAME_WIDTH"), out var nw) && nw >= 50) p03NameWidth = nw;
            }

            if (string.IsNullOrWhiteSpace(attrId) || string.IsNullOrWhiteSpace(topIndId))
            {
                return (indicators, headers, attrId, topIndId, submitType, true, true, false, 400, 65, 180, 110, "Không tìm thấy thông tin cấu trúc biểu mẫu (ATTR_ID / TOP_IND_ID) từ hệ thống IOC.");
            }

            // 2. Fetch dynamic column headers for this specific report (FNC003_S13 + S315/S312)
            var (loadedHeaders, isUnitVisible, isSttVisible, isCodeVisible, nameWidth, sttWidth, codeWidth, unitWidth) = await GetReportHeadersAsync(session, objId, attrId, report.DecimalDigits);

            if (p03SttWidth.HasValue) sttWidth = p03SttWidth.Value;
            if (p03CodeWidth.HasValue) codeWidth = p03CodeWidth.Value;
            if (p03UnitWidth.HasValue) unitWidth = p03UnitWidth.Value;
            if (p03NameWidth.HasValue) nameWidth = p03NameWidth.Value;

            headers = loadedHeaders;

            // 3. Resolve submit_type and pre_time_id
            if (string.IsNullOrWhiteSpace(submitType))
            {
                submitType = ResolveSubmitType(report.PeriodTypeName, report.TimeName);
            }
            var preTimeId = CalculatePreTimeId(periodId, submitType);

            // 3.5. Fetch formulas from FNC003_S202
            var formulaMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var s202Payload = new RestServicePayload
                {
                    Func = "ajaxExecuteQueryO",
                    Params = new List<object?> { "", "FNC003_S202", null },
                    Fcode = "FNC003",
                    Uuid = session.Uuid,
                    Options = new List<QueryOption>
                    {
                        new() { Name = "[0]", Value = objId },
                        new() { Name = "[1]", Value = tenantId }
                    }
                };
                var s202Rows = await ExecuteRestServiceQueryAsync(session, s202Payload);
                foreach (var s202 in s202Rows)
                {
                    var fIndId = NormalizeIdString(GetString(s202, "IND_ID"));
                    var fIndCode = GetString(s202, "IND_CODE");
                    var formula = GetString(s202, "FORMULA");
                    if (!string.IsNullOrWhiteSpace(formula))
                    {
                        if (!string.IsNullOrWhiteSpace(fIndId)) formulaMap[fIndId] = formula;
                        if (!string.IsNullOrWhiteSpace(fIndCode)) formulaMap[fIndCode] = formula;
                    }
                }
            }
            catch { }

            // 4. Call FNC003_P105
            var paramStr = $"{tenantId}${objId}${attrId}${topIndId}${targetOrgId}${periodId}${submitType}${preTimeId}";
            var p105Payload = new RestServicePayload
            {
                Func = IocOperations.CallSpQuery,
                Params = new List<object?> { IocProcedures.GetIndicators, paramStr, 0 },
                Fcode = IocFunctionCodes.Fnc003,
                Uuid = session.Uuid
            };

            var p105Rows = await ExecuteRestServiceQueryAsync(session, p105Payload);
            if (p105Rows.Count == 0)
            {
                return (indicators, headers, attrId, topIndId, submitType, isUnitVisible, isSttVisible, isCodeVisible, nameWidth, sttWidth, codeWidth, unitWidth, "Không lấy được chỉ tiêu nào từ hệ thống IOC.");
            }

            // Detect all parent IDs present in p105Rows
            var allParentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in p105Rows)
            {
                var pId = NormalizeIdString(GetString(row, IocFields.ParentId));
                if (!string.IsNullOrWhiteSpace(pId) && pId != "0" && pId != topIndId)
                {
                    allParentIds.Add(pId);
                }
            }

            int stt = 1;
            foreach (var row in p105Rows)
            {
                var indId = NormalizeIdString(GetString(row, IocFields.IndId));
                var indCode = GetString(row, IocFields.IndCode);
                var indName = GetFirstNonEmpty(row, IocFields.IndName, "NAME", "TITLE");
                var indUnit = GetFirstNonEmpty(row, IocFields.IndUnit, "UNIT_NAME", "UNIT");
                var indType = GetString(row, IocFields.IndType, "1");
                var groupId = GetString(row, "GROUP_ID", "1");
                var fieldId = NormalizeIdString(GetString(row, "FIELD_ID"));
                var preVal = GetFirstNonEmpty(row, "PRE_VAL", "VAL_PRE", "");
                var parentId = NormalizeIdString(GetString(row, IocFields.ParentId));
                var rootId = NormalizeIdString(GetString(row, "ROOT_ID"));
                var dataId = NormalizeIdString(GetString(row, "DATA_ID"));
                var orgId = NormalizeIdString(GetString(row, "ORG_ID"));

                var rawAllowAdd = GetString(row, "ALLOW_ADD_ROW");
                bool allowAddRow = rawAllowAdd == "1" || rawAllowAdd.Equals("true", StringComparison.OrdinalIgnoreCase);

                var rawIsSubInd = GetString(row, "IS_SUB_IND");
                bool isSubInd = rawIsSubInd == "1" || rawIsSubInd.Equals("true", StringComparison.OrdinalIgnoreCase);

                var formula = GetString(row, IocFields.Formula);
                if (string.IsNullOrWhiteSpace(formula) && formulaMap.TryGetValue(indId, out var mappedF))
                {
                    formula = mappedF;
                }
                if (string.IsNullOrWhiteSpace(formula) && formulaMap.TryGetValue(indCode, out var mappedF2))
                {
                    formula = mappedF2;
                }

                bool hasChildren = allParentIds.Contains(indId);

                // Resolve STT / Indicator Index (STT Display)
                var sttDisplay = GetFirstNonEmpty(row, IocFields.IndIndex, "RN", "STT", "ORDER_BY", "STT_DISPLAY");
                if (string.IsNullOrWhiteSpace(sttDisplay))
                {
                    sttDisplay = indType == "2" ? "—" : stt.ToString();
                }

                int level = 0;
                if (indCode.Contains('.'))
                {
                    level = indCode.Count(c => c == '.');
                }
                else if (isSubInd)
                {
                    level = 1;
                }

                var indItem = new IndicatorItem
                {
                    Stt = stt++,
                    SttDisplay = sttDisplay,
                    IndId = indId,
                    IndCode = indCode,
                    IndName = indName,
                    IndUnit = indUnit,
                    IndType = indType,
                    GroupId = groupId,
                    FieldId = fieldId,
                    PreVal = preVal,
                    Level = level,
                    ParentId = parentId,
                    RootId = rootId,
                    DataId = dataId,
                    OrgId = orgId,
                    AllowAddRow = allowAddRow,
                    IsSubInd = isSubInd,
                    Formula = formula,
                    HasChildren = hasChildren,
                    RawAttrInfo = row.TryGetProperty("ATTR_INFO", out var ap) ? ap.Clone() : null
                };

                // Chỉ tiêu có FORMULA: bỏ qua dữ liệu từ API, sẽ được tính lại bởi RecalculateFormulas()
                if (!string.IsNullOrWhiteSpace(formula))
                {
                    indicators.Add(indItem);
                    continue;
                }

                // Extract values for each dynamic column header
                foreach (var col in headers)
                {
                    string colVal = "";

                    // A. Search in direct properties on row: Field (e.g. C958089), C{AttrId}, FldCode, AttrCode
                    var candidates = new List<string>();
                    if (!string.IsNullOrWhiteSpace(col.Field)) candidates.Add(col.Field);
                    if (!string.IsNullOrWhiteSpace(col.AttrId)) candidates.Add($"C{col.AttrId}");
                    if (!string.IsNullOrWhiteSpace(col.FldCode)) candidates.Add(col.FldCode);
                    if (!string.IsNullOrWhiteSpace(col.AttrCode)) candidates.Add(col.AttrCode);

                    foreach (var cand in candidates)
                    {
                        var candVal = GetString(row, cand);
                        if (!string.IsNullOrWhiteSpace(candVal) && candVal != "null" && candVal != "-")
                        {
                            colVal = candVal;
                            break;
                        }
                    }

                    // B. Search in ATTR_INFO array
                    if (string.IsNullOrEmpty(colVal) && row.TryGetProperty("ATTR_INFO", out var attrInfoProp) && attrInfoProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in attrInfoProp.EnumerateArray())
                        {
                            var ac = GetString(a, "ATTR_CODE");
                            var fc = GetString(a, "FLD_CODE");
                            var av = GetString(a, "ATTR_VAL");

                            if ((!string.IsNullOrWhiteSpace(fc) && fc.Equals(col.FldCode, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(ac) && ac.Equals(col.AttrCode, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!string.IsNullOrWhiteSpace(av) && av != "null" && av != "-")
                                {
                                    colVal = av;
                                    break;
                                }
                            }
                        }
                    }

                    // C. Fallback for primary column only if single dynamic column exists
                    if (string.IsNullOrEmpty(colVal) && headers.Count == 1)
                    {
                        var fallback = GetFirstNonEmpty(row, "VAL_0", "VAL", "VALUE", "ATTR_VAL", "RESULT", "DATA_VAL");
                        if (!string.IsNullOrWhiteSpace(fallback) && fallback != "null" && fallback != "-")
                        {
                            colVal = fallback;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(colVal))
                    {
                        colVal = VietnameseNumberHelper.FormatVietnameseNumberFromIoc(colVal);
                    }

                    indItem.SetColumnValue(col.FldCode, colVal);
                }

                indicators.Add(indItem);
            }

            // Nếu toàn bộ chỉ tiêu không có đơn vị tính -> ẩn cột đơn vị tính
            if (isUnitVisible && indicators.All(i => string.IsNullOrWhiteSpace(i.IndUnit)))
            {
                isUnitVisible = false;
            }

            return (indicators, headers, attrId, topIndId, submitType, isUnitVisible, isSttVisible, isCodeVisible, nameWidth, sttWidth, codeWidth, unitWidth, string.Empty);
        }
        catch (Exception ex)
        {
            return (indicators, headers, "", "", "", true, true, false, 400, 65, 180, 110, $"Lỗi nạp chỉ tiêu: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetch nhẹ dữ liệu chỉ tiêu mới nhất từ server (FNC003_P105) để phục vụ merge trước khi lưu.
    /// Trả về Dictionary[indId → [fldCode → value]] — chỉ các ô có giá trị không rỗng.
    /// Nếu gọi thất bại thì trả về Dictionary rỗng (không chặn luồng lưu).
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, string>>> FetchLatestIndicatorValuesAsync(
        AuthSession session, ReportItem report, List<ColumnHeaderInfo> headers,
        string attrId, string topIndId)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var objId = report.ObjId;
            var targetOrgId = !string.IsNullOrWhiteSpace(report.TargetOrgId) && report.TargetOrgId != "-1" && report.TargetOrgId != "0"
                ? report.TargetOrgId
                : session.OrgId;
            var tenantId = session.TenantId;
            var periodId = report.TimeId;

            // Tính submitType và preTimeId từ thông tin báo cáo (không gọi lại FNC003_P03)
            var submitType = ResolveSubmitType(report.PeriodTypeName, report.TimeName);
            var preTimeId = CalculatePreTimeId(periodId, submitType);

            var paramStr = $"{tenantId}${objId}${attrId}${topIndId}${targetOrgId}${periodId}${submitType}${preTimeId}";
            var p105Payload = new RestServicePayload
            {
                Func = IocOperations.CallSpQuery,
                Params = new List<object?> { IocProcedures.GetIndicators, paramStr, 0 },
                Fcode = IocFunctionCodes.Fnc003,
                Uuid = session.Uuid
            };

            var p105Rows = await ExecuteRestServiceQueryAsync(session, p105Payload);

            foreach (var row in p105Rows)
            {
                var indId = NormalizeIdString(GetString(row, IocFields.IndId));
                if (string.IsNullOrWhiteSpace(indId)) continue;

                var colValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var col in headers)
                {
                    string colVal = string.Empty;

                    // A. Tìm giá trị trong thuộc tính trực tiếp của row
                    var candidates = new List<string>();
                    if (!string.IsNullOrWhiteSpace(col.Field)) candidates.Add(col.Field);
                    if (!string.IsNullOrWhiteSpace(col.AttrId)) candidates.Add($"C{col.AttrId}");
                    if (!string.IsNullOrWhiteSpace(col.FldCode)) candidates.Add(col.FldCode);
                    if (!string.IsNullOrWhiteSpace(col.AttrCode)) candidates.Add(col.AttrCode);

                    foreach (var cand in candidates)
                    {
                        var v = GetString(row, cand);
                        if (!string.IsNullOrWhiteSpace(v) && v != "null" && v != "-")
                        {
                            colVal = v;
                            break;
                        }
                    }

                    // B. Tìm trong ATTR_INFO array
                    if (string.IsNullOrEmpty(colVal) &&
                        row.TryGetProperty("ATTR_INFO", out var attrInfoProp) &&
                        attrInfoProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in attrInfoProp.EnumerateArray())
                        {
                            var ac = GetString(a, "ATTR_CODE");
                            var fc = GetString(a, "FLD_CODE");
                            var av = GetString(a, "ATTR_VAL");
                            if ((!string.IsNullOrWhiteSpace(fc) && fc.Equals(col.FldCode, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(ac) && ac.Equals(col.AttrCode, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!string.IsNullOrWhiteSpace(av) && av != "null" && av != "-")
                                {
                                    colVal = av;
                                    break;
                                }
                            }
                        }
                    }

                    // C. Fallback nếu chỉ có 1 cột
                    if (string.IsNullOrEmpty(colVal) && headers.Count == 1)
                    {
                        var fallback = GetFirstNonEmpty(row, "VAL_0", "VAL", "VALUE", "ATTR_VAL", "RESULT", "DATA_VAL");
                        if (!string.IsNullOrWhiteSpace(fallback) && fallback != "null" && fallback != "-")
                            colVal = fallback;
                    }

                    if (!string.IsNullOrWhiteSpace(colVal))
                    {
                        // Chuẩn hóa về định dạng hiển thị Việt Nam để nhất quán với
                        // _originalColumnValues (cũng được lưu ở định dạng này).
                        // Đảm bảo so sánh conflict không bị sai do khác format (vd: "1234" vs "1.234").
                        colVal = VietnameseNumberHelper.FormatVietnameseNumberFromIoc(colVal);
                    }
                    else
                    {
                        colVal = string.Empty;
                    }

                    colValues[col.FldCode] = colVal;
                }

                result[indId] = colValues;
            }
        }
        catch
        {
            // Lỗi khi fetch → trả về rỗng, SaveReportDataAsync sẽ tự xử lý
        }
        return result;
    }

    /// <summary>
    /// Lưu số liệu báo cáo lên hệ thống IOC (FNC003_P220) cho tất cả các cột động.
    /// Tự động fetch dữ liệu mới nhất từ server trước khi lưu để merge an toàn khi nhiều người
    /// cùng điền đồng thời (Fetch-before-Save Merge):
    ///   - Ô người dùng đã chỉnh sửa (IsModified = true) → ưu tiên giá trị cục bộ
    ///   - Ô chưa chạm vào (IsModified = false)          → lấy giá trị mới nhất từ server (kể cả khi đã bị xóa)
    /// </summary>
    public async Task<(bool Success, string Message)> SaveReportDataAsync(AuthSession session, ReportItem report, List<IndicatorItem> indicators, List<ColumnHeaderInfo> headers, string topIndId, string attrId = "", List<IndicatorItem>? deletedIndicators = null)
    {
        if (!session.IsAuthenticated)
            return (false, "Hết phiên đăng nhập, hãy đăng nhập lại.");

        try
        {
            // ── Fetch-before-Save Merge ──────────────────────────────────────────────────
            // Bước 1: Lấy dữ liệu mới nhất từ server (tránh ghi đè dữ liệu người khác vừa lưu)
            var serverValues = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(attrId) && !string.IsNullOrWhiteSpace(topIndId))
            {
                serverValues = await FetchLatestIndicatorValuesAsync(session, report, headers, attrId, topIndId);
            }
            // ────────────────────────────────────────────────────────────────────────────

            // A conflict exists only when this user changed a cell and IOC changed the same
            // cell since the form was loaded.  Mark it for the UI and stop before writing so
            // the user can explicitly choose which value to keep.
            var conflicts = new List<string>();
            foreach (var ind in indicators.Where(i => i.IsEditable))
            {
                serverValues.TryGetValue(ind.IndId, out var latestValues);
                foreach (var col in headers)
                {
                    if (!ind.IsCellModified(col.FldCode))
                    {
                        ind.ClearConflict(col.FldCode);
                        continue;
                    }

                    var original = ind.GetOriginalColumnValue(col.FldCode);
                    var latest = latestValues != null && latestValues.TryGetValue(col.FldCode, out var value)
                        ? value ?? string.Empty : string.Empty;
                    // Chuẩn hóa cả hai về dạng thập phân chuẩn trước khi so sánh:
                    // tránh false-negative do khác ký tự phân cách ("1.234" vs "1234", "11,5" vs "11.5").
                    var originalNorm = VietnameseNumberHelper.ToStandardDecimalString(original);
                    var latestNorm = VietnameseNumberHelper.ToStandardDecimalString(latest);
                    if (!string.Equals(latestNorm, originalNorm, StringComparison.Ordinal))
                    {
                        ind.SetConflict(col.FldCode, latest);
                        conflicts.Add($"{ind.IndName} – {col.HeaderName}");
                    }
                    else
                    {
                        ind.ClearConflict(col.FldCode);
                    }
                }
            }
            if (conflicts.Count > 0)
            {
                return (false, $"Có {conflicts.Count} ô xung đột với dữ liệu mới trên máy chủ. Chọn dữ liệu cần giữ tại các ô viền đỏ trước khi lưu.");
            }

            var objSave = new List<object>();

            foreach (var ind in indicators)
            {
                if (ind.IsHeader && !ind.IsSubInd)
                {
                    objSave.Add(new
                    {
                        IND_ID = ind.IndId,
                        IND_CODE = ind.IndCode,
                        IND_TYPE = "2",
                        FIELD_ID = ind.FieldId,
                        ATTR_INFO = Array.Empty<object>()
                    });
                }
                else
                {
                    var attrInfoList = new List<object>();

                    // Lấy giá trị server cho chỉ tiêu này (nếu có)
                    serverValues.TryGetValue(ind.IndId, out var serverColValues);

                    foreach (var col in headers)
                    {
                        string finalVal;

                        var localVal = ind.GetColumnValue(col.FldCode);
                        var isCellModified = ind.IsCellModified(col.FldCode);

                        // ── Merge logic ──────────────────────────────────────────────────
                        // IsModified = true  → người dùng đã chỉnh sửa → giữ giá trị cục bộ
                        // IsModified = false → chưa chỉnh sửa → lấy giá trị mới nhất từ server (kể cả trường hợp ô bị người khác xóa thành rỗng)
                        if (isCellModified)
                        {
                            finalVal = localVal; // Local wins, including an explicit empty deletion
                        }
                        else if (serverColValues != null && serverColValues.TryGetValue(col.FldCode, out var serverVal))
                        {
                            // Server wins (fresher data from another user hoặc ô đã bị người khác xóa).
                            // FetchLatestIndicatorValuesAsync đã chuẩn hóa về display format hoặc rỗng nếu bị xóa.
                            finalVal = serverVal ?? string.Empty;
                        }
                        else
                        {
                            finalVal = localVal;  // Fallback to local khi không lấy được dữ liệu server
                        }
                        // ────────────────────────────────────────────────────────────────

                        // Empty values are sent only for an edited cell: this is the explicit
                        // deletion command.  Untouched empty cells remain omitted.
                        if ((!string.IsNullOrWhiteSpace(finalVal) && finalVal != "-") || isCellModified)
                        {
                            // IOC nhận ATTR_VAL theo chuẩn dấu chấm thập phân (183,123 -> 183.123).
                            // Cả giá trị cục bộ (display format) lẫn giá trị server (đã format ở Fetch)
                            // đều đi qua ToStandardDecimalString để chuyển về IOC format đúng.
                            var cleanVal = string.IsNullOrWhiteSpace(finalVal) || finalVal == "-"
                                ? string.Empty
                                : VietnameseNumberHelper.ToStandardDecimalString(finalVal);
                            attrInfoList.Add(new
                            {
                                ATTR_CODE = col.AttrCode,
                                FLD_CODE = col.FldCode,
                                ATTR_VAL = cleanVal
                            });
                        }
                    }

                    if (ind.IsSubInd)
                    {
                        objSave.Add(new
                        {
                            // Web IOC gửi -1 cho dòng mới; IOC sẽ cấp IND_ID thật khi lưu.
                            IND_ID = ind.IsNewSubIndicator ? "-1" : ind.IndId,
                            IND_CODE = ind.IndCode,
                            IND_NAME = ind.IndName,
                            IND_UNIT = ind.IndUnit,
                            IND_INDEX = ind.SttDisplay,
                            IND_TYPE = "1",
                            PARENT_ID = ind.ParentId,
                            ORG_ID = ind.OrgId,
                            FIELD_ID = ind.FieldId,
                            ATTR_INFO = attrInfoList
                        });
                    }
                    else
                    {
                        objSave.Add(new
                        {
                            IND_ID = ind.IndId,
                            IND_CODE = ind.IndCode,
                            IND_TYPE = ind.IndType,
                            FIELD_ID = ind.FieldId,
                            ATTR_INFO = attrInfoList
                        });
                    }
                }
            }

            // Đưa các chỉ tiêu con đã bị người dùng xóa vào gói lưu với cờ DELETE
            if (deletedIndicators != null && deletedIndicators.Count > 0)
            {
                foreach (var delInd in deletedIndicators)
                {
                    objSave.Add(new
                    {
                        IND_ID = delInd.IndId,
                        IND_CODE = delInd.IndCode,
                        IND_NAME = delInd.IndName,
                        IND_UNIT = delInd.IndUnit,
                        IND_INDEX = delInd.SttDisplay,
                        IND_TYPE = "1",
                        PARENT_ID = delInd.ParentId,
                        ORG_ID = delInd.OrgId,
                        FIELD_ID = delInd.FieldId,
                        COMMAND = "DELETE",
                        IS_DELETE = 1,
                        IS_DELETED = 1,
                        STATUS = "DELETE",
                        STATE = "DELETE",
                        ATTR_INFO = Array.Empty<object>()
                    });
                }
            }

            var targetOrgId = !string.IsNullOrWhiteSpace(report.TargetOrgId) && report.TargetOrgId != "-1" && report.TargetOrgId != "0"
                ? report.TargetOrgId
                : session.OrgId;
            var jsonSaveStr = JsonSerializer.Serialize(objSave);
            var paramStrSave = $"{session.TenantId}${targetOrgId}${report.TimeId}${report.ObjId}${jsonSaveStr}$1${topIndId}";

            var savePayload = new RestServicePayload
            {
                Func = IocOperations.CallSpSave,
                Params = new List<object?> { IocProcedures.SaveReport, paramStrSave },
                Fcode = IocFunctionCodes.Fnc003,
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var jsonBody = JsonSerializer.Serialize(savePayload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            var respText = await response.Content.ReadAsStringAsync();
            if (IsSessionExpiredResponse(response, respText))
                return (false, "Hết phiên đăng nhập, hãy đăng nhập lại.");

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    return (false, "Có thể đã hết phiên đăng nhập, hãy đăng nhập lại.");

                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(respText);

            if (doc.RootElement.TryGetProperty("result", out var resProp))
            {
                string resStr = resProp.ValueKind == JsonValueKind.String ? resProp.GetString() ?? "" : resProp.GetRawText();
                if (!string.IsNullOrWhiteSpace(resStr))
                {
                    try
                    {
                        using var resDoc = JsonDocument.Parse(resStr);
                        var msgCode = GetString(resDoc.RootElement, "MSG_CODE", "0");
                        var msgText = GetString(resDoc.RootElement, "MSG_TEXT", "Thành công");

                        if (msgCode == "1")
                        {
                            return (true, $"Lưu thành công: {msgText}");
                        }
                        else
                        {
                            return (false, $"Hệ thống từ chối lưu (Mã {msgCode}): {msgText}");
                        }
                    }
                    catch
                    {
                        if (resStr.Contains("\"MSG_CODE\":\"1\"") || resStr.Contains("\"MSG_CODE\":1") || resStr.Contains("1"))
                        {
                            return (true, "Lưu dữ liệu báo cáo thành công!");
                        }
                    }
                }
            }

            return (true, "Lưu dữ liệu báo cáo hoàn tất!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi lưu: {ex.Message}");
        }
    }

    private static bool IsSessionExpiredResponse(HttpResponseMessage response, string responseText)
    {
        if ((int)response.StatusCode is 401 or 403)
            return true;

        var responseUrl = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;
        if (responseUrl.Contains("login", StringComparison.OrdinalIgnoreCase))
            return true;

        var text = responseText.ToLowerInvariant();
        return text.Contains("login.jsp") ||
               text.Contains("j_username") ||
               text.Contains("session expired") ||
               text.Contains("phiên đăng nhập đã hết hạn") ||
               text.Contains("het phien dang nhap");
    }

    public static string ResolveSubmitType(string periodTypeName, string timeName)
    {
        var combined = $"{periodTypeName} {timeName}".ToLower();
        if (combined.Contains("6 tháng") || combined.Contains("6thang") || combined.Contains("bán niên") || combined.Contains("ban nien")) return "8";
        if (combined.Contains("quý") || combined.Contains("quy") || Regex.IsMatch(combined, @"\b(quý|quy|q)[1-4]\b")) return "3";
        if (combined.Contains("tháng") || combined.Contains("thang") || Regex.IsMatch(combined, @"\b(tháng|thang|t)(0?[1-9]|1[0-2])\b")) return "2";
        if (combined.Contains("tuần") || combined.Contains("tuan")) return "6";
        if (combined.Contains("năm") || combined.Contains("nam")) return "4";
        if (combined.Contains("ngày") || combined.Contains("ngay")) return "5";
        return "2";
    }

    public static string CalculatePreTimeId(string timeId, string submitType)
    {
        if (string.IsNullOrWhiteSpace(timeId)) return timeId;
        var s = timeId.Trim();

        try
        {
            if (submitType == "2") // Tháng (yyyyMM)
            {
                if (s.Length >= 6 && int.TryParse(s[..4], out var y) && int.TryParse(s[4..], out var m))
                {
                    return m == 1 ? $"{y - 1}12" : $"{y}{m - 1:D2}";
                }
            }
            else if (submitType == "3") // Quý (yyyyQ)
            {
                if (s.Length >= 5 && int.TryParse(s[..4], out var y) && int.TryParse(s[4..], out var q))
                {
                    return q == 1 ? $"{y - 1}4" : $"{y}{q - 1}";
                }
            }
            else if (submitType == "4") // Năm (yyyy)
            {
                if (int.TryParse(s, out var y)) return (y - 1).ToString();
            }
            else if (submitType == "5") // Ngày (yyyyMMdd)
            {
                if (DateTime.TryParseExact(s, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                {
                    return dt.AddDays(-1).ToString("yyyyMMdd");
                }
            }
            else if (submitType == "6") // Tuần (yyyyWWW)
            {
                if (s.Length >= 7 && int.TryParse(s[..4], out var y) && int.TryParse(s[4..], out var w))
                {
                    return w == 1 ? $"{y - 1}052" : $"{y}{w - 1:D3}";
                }
            }
            else if (submitType == "8") // 6 Tháng (yyyy1 hoặc yyyy06/yyyy12)
            {
                if (s.Length >= 5 && int.TryParse(s[..4], out var y))
                {
                    var code = s[4..];
                    return (code == "1" || code == "06") ? $"{y - 1}2" : $"{y}1";
                }
            }
        }
        catch { }

        return s;
    }

    /// <summary>
    /// Lấy danh sách chỉ tiêu và số liệu từ kỳ trước của báo cáo
    /// </summary>
    public async Task<(List<IndicatorItem> Indicators, List<ColumnHeaderInfo> Headers, string PreTimeId, string ErrorMessage)> GetPrePeriodIndicatorsAsync(AuthSession session, ReportItem report, string attrId, string topIndId, string submitType)
    {
        var indicators = new List<IndicatorItem>();
        var headers = new List<ColumnHeaderInfo>();
        if (!session.IsAuthenticated)
            return (indicators, headers, "", "Chưa xác thực phiên đăng nhập");

        try
        {
            if (string.IsNullOrWhiteSpace(submitType))
            {
                submitType = ResolveSubmitType(report.PeriodTypeName, report.TimeName);
            }

            var preTimeId = CalculatePreTimeId(report.TimeId, submitType);
            if (string.IsNullOrWhiteSpace(preTimeId) || preTimeId == report.TimeId)
            {
                return (indicators, headers, preTimeId, "Không xác định được chu kỳ trước của báo cáo này.");
            }

            var prePreTimeId = CalculatePreTimeId(preTimeId, submitType);
            var targetOrgId = !string.IsNullOrWhiteSpace(report.TargetOrgId) && report.TargetOrgId != "-1" && report.TargetOrgId != "0"
                ? report.TargetOrgId
                : session.OrgId;
            var tenantId = session.TenantId;
            var objId = report.ObjId;

            if (string.IsNullOrWhiteSpace(attrId) || string.IsNullOrWhiteSpace(topIndId))
            {
                var p03Payload = new RestServicePayload
                {
                    Func = "ajaxCALL_SP_O",
                    Params = new List<object?> { "FNC003_P03", objId, 0 },
                    Fcode = "FNC003",
                    Uuid = session.Uuid
                };
                var p03Rows = await ExecuteRestServiceQueryAsync(session, p03Payload);
                if (p03Rows.Count > 0)
                {
                    var r0 = p03Rows[0];
                    attrId = GetString(r0, "ATTR_ID", attrId);
                    topIndId = GetString(r0, "IND_ID", topIndId);
                }
            }

            // 1. Fetch dynamic column headers
            var (loadedHeaders, _, _, _, _, _, _, _) = await GetReportHeadersAsync(session, objId, attrId, report.DecimalDigits);
            headers = loadedHeaders;

            // 2. Call FNC003_P105 with preTimeId
            var paramStr = $"{tenantId}${objId}${attrId}${topIndId}${targetOrgId}${preTimeId}${submitType}${prePreTimeId}";
            var p105Payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_O",
                Params = new List<object?> { "FNC003_P105", paramStr, 0 },
                Fcode = "FNC003",
                Uuid = session.Uuid
            };

            var p105Rows = await ExecuteRestServiceQueryAsync(session, p105Payload);
            if (p105Rows.Count == 0)
            {
                return (indicators, headers, preTimeId, "Không có dữ liệu kỳ trước trên hệ thống IOC.");
            }

            int stt = 1;
            foreach (var row in p105Rows)
            {
                var indId = NormalizeIdString(GetString(row, "IND_ID"));
                var indCode = GetString(row, "IND_CODE");
                var indName = GetFirstNonEmpty(row, "IND_NAME", "NAME", "TITLE");
                var indUnit = GetFirstNonEmpty(row, "IND_UNIT", "UNIT_NAME", "UNIT");
                var indType = GetString(row, "IND_TYPE", "1");
                var fieldId = NormalizeIdString(GetString(row, "FIELD_ID"));
                var preVal = GetFirstNonEmpty(row, "PRE_VAL", "VAL_PRE", "");
                var parentId = NormalizeIdString(GetString(row, "PARENT_ID"));

                var indItem = new IndicatorItem
                {
                    Stt = stt++,
                    IndId = indId,
                    IndCode = indCode,
                    IndName = indName,
                    IndUnit = indUnit,
                    IndType = indType,
                    FieldId = fieldId,
                    PreVal = preVal,
                    ParentId = parentId
                };

                foreach (var col in headers)
                {
                    string colVal = "";
                    var candidates = new List<string>();
                    if (!string.IsNullOrWhiteSpace(col.Field)) candidates.Add(col.Field);
                    if (!string.IsNullOrWhiteSpace(col.AttrId)) candidates.Add($"C{col.AttrId}");
                    if (!string.IsNullOrWhiteSpace(col.FldCode)) candidates.Add(col.FldCode);
                    if (!string.IsNullOrWhiteSpace(col.AttrCode)) candidates.Add(col.AttrCode);

                    foreach (var cand in candidates)
                    {
                        var candVal = GetString(row, cand);
                        if (!string.IsNullOrWhiteSpace(candVal) && candVal != "null" && candVal != "-")
                        {
                            colVal = candVal;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(colVal) && row.TryGetProperty("ATTR_INFO", out var attrInfoProp) && attrInfoProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in attrInfoProp.EnumerateArray())
                        {
                            var ac = GetString(a, "ATTR_CODE");
                            var fc = GetString(a, "FLD_CODE");
                            var av = GetString(a, "ATTR_VAL");

                            if ((!string.IsNullOrWhiteSpace(fc) && fc.Equals(col.FldCode, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(ac) && ac.Equals(col.AttrCode, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!string.IsNullOrWhiteSpace(av) && av != "null" && av != "-")
                                {
                                    colVal = av;
                                    break;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(colVal))
                    {
                        colVal = VietnameseNumberHelper.FormatVietnameseNumberFromIoc(colVal);
                    }

                    indItem.SetColumnValue(col.FldCode, colVal);
                }

                indicators.Add(indItem);
            }

            return (indicators, headers, preTimeId, "");
        }
        catch (Exception ex)
        {
            return (indicators, headers, "", $"Lỗi nạp dữ liệu kỳ trước: {ex.Message}");
        }
    }

    /// <summary>
    /// Gửi yêu cầu đính chính số liệu lên hệ thống IOC (FNC010_P23, State = 6)
    /// Khớp chính xác với payload từ hệ thống IOC Hà Tĩnh
    /// </summary>
    public async Task<(bool Success, string Message)> RequestCorrectionAsync(AuthSession session, ReportItem report, string reason = "")
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        try
        {
            var inputGrantId = report.InputGrantId;
            if (string.IsNullOrWhiteSpace(inputGrantId))
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) của biểu mẫu này.");

            // Stored procedure FNC010_P23: {INPUT_GRANT_ID}$6${REASON}${USER_ID}
            var paramStr = $"{inputGrantId}$6${reason}${session.UserId}";
            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P23", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            // Tùy chọn: Gửi kèm thông báo SMS (như web client thực hiện)
            try
            {
                if (long.TryParse(inputGrantId, out var grantIdNum))
                {
                    var smsUrl = session.ApiUrl.Replace("/RestService", "/sendSms");
                    var smsObj = new
                    {
                        TYPE = 2,
                        DATA = new
                        {
                            ARR_INPUT_GRANT_ID = new long[] { grantIdNum },
                            STATE = 6
                        }
                    };
                    var smsJson = JsonSerializer.Serialize(smsObj);
                    using var smsContent = new StringContent(smsJson, Encoding.UTF8, "application/x-www-form-urlencoded");
                    await client.PostAsync(smsUrl, smsContent);
                }
            }
            catch { }

            return (true, "Đã gửi yêu cầu đính chính thành công lên cấp trên!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi gửi yêu cầu đính chính: {ex.Message}");
        }
    }

    /// <summary>
    /// Gửi báo cáo lên Tỉnh / Cấp trên (FNC010_P19, State = 3)
    /// </summary>
    public async Task<(bool Success, string Message)> SendReportsAsync(AuthSession session, List<ReportItem> reports)
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        if (reports == null || reports.Count == 0)
            return (false, "Không có báo cáo nào để gửi");

        try
        {
            var idList = reports
                .Where(r => !string.IsNullOrWhiteSpace(r.InputGrantId))
                .Select(r => new { ID = r.InputGrantId })
                .ToList();

            if (idList.Count == 0)
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) hợp lệ");

            var jsonIds = JsonSerializer.Serialize(idList);
            var paramStr = $"{jsonIds}$3$${session.UserId}";

            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P19", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            // Tùy chọn: Gửi SMS
            try
            {
                var numericIds = reports
                    .Where(r => long.TryParse(r.InputGrantId, out _))
                    .Select(r => long.Parse(r.InputGrantId))
                    .ToList();

                if (numericIds.Count > 0)
                {
                    var smsUrl = session.ApiUrl.Replace("/RestService", "/sendSms");
                    var smsObj = new
                    {
                        TYPE = 2,
                        DATA = new
                        {
                            ARR_INPUT_GRANT_ID = numericIds,
                            STATE = 3
                        }
                    };
                    var smsJson = JsonSerializer.Serialize(smsObj);
                    using var smsContent = new StringContent(smsJson, Encoding.UTF8, "application/x-www-form-urlencoded");
                    await client.PostAsync(smsUrl, smsContent);
                }
            }
            catch { }

            return (true, $"Đã gửi thành công {reports.Count} báo cáo lên cấp trên!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi gửi báo cáo: {ex.Message}");
        }
    }

    /// <summary>
    /// Phê duyệt nhiều báo cáo cùng lúc (FNC010_P19 hoặc FNC010_P23, State = 4)
    /// </summary>
    public async Task<(bool Success, string Message)> ApproveReportsAsync(AuthSession session, List<ReportItem> reports)
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        if (reports == null || reports.Count == 0)
            return (false, "Không có báo cáo nào để duyệt");

        try
        {
            var idList = reports
                .Where(r => !string.IsNullOrWhiteSpace(r.InputGrantId))
                .Select(r => new { ID = r.InputGrantId })
                .ToList();

            if (idList.Count == 0)
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) hợp lệ");

            var jsonIds = JsonSerializer.Serialize(idList);
            var paramStr = $"{jsonIds}$4$${session.UserId}";

            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P19", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                // Fallback: Duyệt từng báo cáo bằng FNC010_P23 nếu P19 không hỗ trợ
                int successCount = 0;
                foreach (var r in reports)
                {
                    var (s, _) = await ApproveReportAsync(session, r);
                    if (s) successCount++;
                }
                if (successCount > 0)
                {
                    return (true, $"Đã phê duyệt thành công {successCount}/{reports.Count} báo cáo!");
                }
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            return (true, $"Đã phê duyệt thành công {reports.Count} báo cáo!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi phê duyệt báo cáo: {ex.Message}");
        }
    }

    /// <summary>
    /// Từ chối nhiều báo cáo cùng lúc (FNC010_P19, State = 8)
    /// Gọi API đúng 1 lần duy nhất với mảng JSON danh sách INPUT_GRANT_ID
    /// </summary>
    public async Task<(bool Success, string Message)> RejectReportsAsync(AuthSession session, List<ReportItem> reports, string reason = "")
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        if (reports == null || reports.Count == 0)
            return (false, "Không có báo cáo nào để từ chối");

        try
        {
            var idList = reports
                .Where(r => !string.IsNullOrWhiteSpace(r.InputGrantId))
                .Select(r => new { ID = r.InputGrantId })
                .ToList();

            if (idList.Count == 0)
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) hợp lệ");

            var jsonIds = JsonSerializer.Serialize(idList);
            var paramStr = $"{jsonIds}$8${reason}${session.UserId}";

            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P19", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                // Fallback: nếu P19 không hỗ trợ từ chối hàng loạt, gọi từng báo cáo qua P23
                int successCount = 0;
                foreach (var r in reports)
                {
                    var (s, _) = await RejectApprovalReportAsync(session, r, reason);
                    if (s) successCount++;
                }
                if (successCount > 0)
                {
                    return (true, $"Đã từ chối thành công {successCount}/{reports.Count} báo cáo!");
                }
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            return (true, $"Đã từ chối thành công {reports.Count} báo cáo!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi từ chối hàng loạt báo cáo: {ex.Message}");
        }
    }

    /// <summary>
    /// Phê duyệt báo cáo (FNC010_P23, State = 4)
    /// Chuyển báo cáo từ trạng thái "Báo cáo đã được gửi" sang "Báo cáo đã được duyệt"
    /// </summary>
    public async Task<(bool Success, string Message)> ApproveReportAsync(AuthSession session, ReportItem report)
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        try
        {
            var inputGrantId = report.InputGrantId;
            if (string.IsNullOrWhiteSpace(inputGrantId))
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) của biểu mẫu này.");

            var paramStr = $"{inputGrantId}$4$${session.UserId}";
            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P23", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            // Gửi thông báo SMS nếu có
            try
            {
                if (long.TryParse(inputGrantId, out var grantIdNum))
                {
                    var smsUrl = session.ApiUrl.Replace("/RestService", "/sendSms");
                    var smsObj = new
                    {
                        TYPE = 2,
                        DATA = new
                        {
                            ARR_INPUT_GRANT_ID = new long[] { grantIdNum },
                            STATE = 4
                        }
                    };
                    var smsJson = JsonSerializer.Serialize(smsObj);
                    using var smsContent = new StringContent(smsJson, Encoding.UTF8, "application/x-www-form-urlencoded");
                    await client.PostAsync(smsUrl, smsContent);
                }
            }
            catch { }

            report.ApprovedDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            return (true, $"Đã phê duyệt thành công biểu mẫu \"{report.ObjName}\"!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi phê duyệt báo cáo: {ex.Message}");
        }
    }

    /// <summary>
    /// Duyệt yêu cầu đính chính (FNC010_P23, State = 6)
    /// Cho phép đơn vị cấp dưới mở lại quyền chỉnh sửa số liệu
    /// </summary>
    public async Task<(bool Success, string Message)> ApproveCorrectionRequestAsync(AuthSession session, ReportItem report, string reason = "")
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        try
        {
            var inputGrantId = report.InputGrantId;
            if (string.IsNullOrWhiteSpace(inputGrantId))
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) của biểu mẫu này.");

            var paramStr = $"{inputGrantId}$6${reason}${session.UserId}";
            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P23", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            try
            {
                if (long.TryParse(inputGrantId, out var grantIdNum))
                {
                    var smsUrl = session.ApiUrl.Replace("/RestService", "/sendSms");
                    var smsObj = new
                    {
                        TYPE = 2,
                        DATA = new
                        {
                            ARR_INPUT_GRANT_ID = new long[] { grantIdNum },
                            STATE = 6
                        }
                    };
                    var smsJson = JsonSerializer.Serialize(smsObj);
                    using var smsContent = new StringContent(smsJson, Encoding.UTF8, "application/x-www-form-urlencoded");
                    await client.PostAsync(smsUrl, smsContent);
                }
            }
            catch { }

            return (true, $"Đã duyệt yêu cầu đính chính cho biểu mẫu \"{report.ObjName}\" thành công!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi duyệt đính chính: {ex.Message}");
        }
    }

    /// <summary>
    /// Từ chối yêu cầu đính chính trong màn hình Duyệt báo cáo (FNC010_P23, State = 4)
    /// Giữ nguyên trạng thái báo cáo đã duyệt, xóa yêu cầu đính chính
    /// </summary>
    public async Task<(bool Success, string Message)> RejectCorrectionRequestAsync(AuthSession session, ReportItem report, string reason = "")
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        try
        {
            var inputGrantId = report.InputGrantId;
            if (string.IsNullOrWhiteSpace(inputGrantId))
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) của biểu mẫu này.");

            var paramStr = $"{inputGrantId}$4${reason}${session.UserId}";
            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P23", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            return (true, $"Đã từ chối yêu cầu đính chính cho biểu mẫu \"{report.ObjName}\" thành công!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi từ chối yêu cầu đính chính: {ex.Message}");
        }
    }

    /// <summary>
    /// Xử lý nhiều yêu cầu đính chính cùng lúc (song song tối ưu)
    /// </summary>
    public async Task<(int SuccessCount, int FailCount, string Message)> BatchProcessCorrectionRequestsAsync(
        AuthSession session,
        List<ReportItem> reports,
        bool agree,
        string responseNote = "")
    {
        if (!session.IsAuthenticated)
            return (0, reports.Count, "Chưa xác thực phiên đăng nhập");

        if (reports == null || reports.Count == 0)
            return (0, 0, "Không có báo cáo nào để xử lý");

        int successCount = 0;
        int failCount = 0;

        // Xử lý song song với độ trễ tối thiểu
        await Parallel.ForEachAsync(reports, new ParallelOptions { MaxDegreeOfParallelism = 5 }, async (report, ct) =>
        {
            try
            {
                bool s;
                if (agree)
                {
                    var res = await ApproveCorrectionRequestAsync(session, report, responseNote);
                    s = res.Success;
                }
                else
                {
                    var res = await RejectCorrectionRequestAsync(session, report, responseNote);
                    s = res.Success;
                }

                if (s)
                {
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    Interlocked.Increment(ref failCount);
                }
            }
            catch
            {
                Interlocked.Increment(ref failCount);
            }
        });

        var actionText = agree ? "đồng ý đính chính" : "không đồng ý đính chính";
        return (successCount, failCount, $"Đã xử lý {actionText}: Thành công {successCount}/{reports.Count} báo cáo.");
    }

    /// <summary>
    /// Từ chối báo cáo trong màn hình Duyệt báo cáo (FNC010_P23, State = 8 hoặc 5)
    /// Trả lại báo cáo cho đơn vị kèm lý do
    /// </summary>
    public async Task<(bool Success, string Message)> RejectApprovalReportAsync(AuthSession session, ReportItem report, string reason)
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        try
        {
            var inputGrantId = report.InputGrantId;
            if (string.IsNullOrWhiteSpace(inputGrantId))
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) của biểu mẫu này.");

            var paramStr = $"{inputGrantId}$8${reason}${session.UserId}";
            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P23", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            try
            {
                if (long.TryParse(inputGrantId, out var grantIdNum))
                {
                    var smsUrl = session.ApiUrl.Replace("/RestService", "/sendSms");
                    var smsObj = new
                    {
                        TYPE = 2,
                        DATA = new
                        {
                            ARR_INPUT_GRANT_ID = new long[] { grantIdNum },
                            STATE = 8
                        }
                    };
                    var smsJson = JsonSerializer.Serialize(smsObj);
                    using var smsContent = new StringContent(smsJson, Encoding.UTF8, "application/x-www-form-urlencoded");
                    await client.PostAsync(smsUrl, smsContent);
                }
            }
            catch { }

            return (true, $"Đã từ chối báo cáo \"{report.ObjName}\" thành công!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi từ chối báo cáo: {ex.Message}");
        }
    }

    /// <summary>
    /// Trình nhiều báo cáo lên lãnh đạo bằng đúng một lệnh FNC010_P19 (State = 2).
    /// </summary>
    public async Task<(bool Success, string Message)> SubmitReportsToLeaderAsync(AuthSession session, List<ReportItem> reports, string opinion = "")
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        var idList = reports?
            .Where(r => !string.IsNullOrWhiteSpace(r.InputGrantId))
            .Select(r => new { ID = r.InputGrantId })
            .ToList();

        if (idList == null || idList.Count == 0)
            return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) hợp lệ");

        try
        {
            var jsonIds = JsonSerializer.Serialize(idList);
            var paramStr = $"{jsonIds}$2${opinion}${session.UserId}";
            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P19", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
                return (false, $"Lỗi server HTTP {response.StatusCode}");

            return (true, $"Đã trình lãnh đạo thành công {idList.Count} báo cáo!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi trình lãnh đạo: {ex.Message}");
        }
    }

    /// <summary>
    /// Trình báo cáo lên lãnh đạo (FNC010_P23, State = 2)
    /// Cập nhật trạng thái báo cáo thành "Đã trình lãnh đạo" kèm ý kiến/giải trình
    /// </summary>
    public async Task<(bool Success, string Message)> SubmitReportToLeaderAsync(AuthSession session, ReportItem report, string opinion = "")
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập");

        try
        {
            var inputGrantId = report.InputGrantId;
            if (string.IsNullOrWhiteSpace(inputGrantId))
                return (false, "Không tìm thấy mã phân bổ (INPUT_GRANT_ID) của biểu mẫu này.");

            var paramStr = $"{inputGrantId}$2${opinion}${session.UserId}";
            var payload = new RestServicePayload
            {
                Func = "ajaxCALL_SP_I",
                Params = new List<object?> { "FNC010_P23", paramStr },
                Fcode = "FNC010",
                Uuid = session.Uuid
            };

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            client.DefaultRequestHeaders.Add("Cookie", session.CookieString);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

            var jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(session.ApiUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Lỗi server HTTP {response.StatusCode}");
            }

            try
            {
                if (long.TryParse(inputGrantId, out var grantIdNum))
                {
                    var smsUrl = session.ApiUrl.Replace("/RestService", "/sendSms");
                    var smsObj = new
                    {
                        TYPE = 2,
                        DATA = new
                        {
                            ARR_INPUT_GRANT_ID = new long[] { grantIdNum },
                            STATE = 2
                        }
                    };
                    var smsJson = JsonSerializer.Serialize(smsObj);
                    using var smsContent = new StringContent(smsJson, Encoding.UTF8, "application/x-www-form-urlencoded");
                    await client.PostAsync(smsUrl, smsContent);
                }
            }
            catch { }

            return (true, $"Đã trình lãnh đạo báo cáo \"{report.ObjName}\" thành công!");
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối khi trình lãnh đạo: {ex.Message}");
        }
    }

    /// <summary>
    /// Khởi tạo cấu hình API IOC
    /// </summary>
    public static IocApiConfig LoadIocApiConfig()
    {
        return new IocApiConfig();
    }

    public static string ResolveReportCode(ReportItem report, IocApiConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(report.ObjId) && cfg.ReportCodeMap.TryGetValue(report.ObjId, out var mappedCode))
            return mappedCode;

        if (!string.IsNullOrWhiteSpace(report.ObjCode))
            return report.ObjCode;

        return report.ObjId ?? "";
    }

    public static (Dictionary<string, string> OrgNameToCodeMap, Dictionary<string, string> OrgNameToIdMap, Dictionary<string, List<string>> DefaultAssigneesMap) LoadOrganizationMaps(IocApiConfig? cfg = null)
    {
        var orgNameToCodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var orgNameToIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var defaultAssigneesMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (cfg != null)
        {
            foreach (var (k, v) in cfg.OrgCodeMap)
            {
                orgNameToCodeMap[v] = k;
                orgNameToIdMap[v] = k;
            }
        }

        return (orgNameToCodeMap, orgNameToIdMap, defaultAssigneesMap);
    }

    /// <summary>
    /// Gọi API FNC006_S200 để lấy danh sách đơn vị được giao gửi báo cáo kèm trạng thái nộp.
    /// Payload: func=ajaxExecuteQueryO, params=["","FNC006_S200",null], fcode=FNC006
    /// Options: [0],[1],[2]=TenantId; [3]=,{ObjId},; [4]=OrgId; [5]=TimeId; [6]=""
    /// Response: STT, OBJ_ID, ORG_ID, TITLE, STATUS (int), STATUS_STR, SND_DATE
    /// </summary>
    public async Task<List<AssignedUnitStatus>> GetUnitsSendingStatusViaFNC006Async(AuthSession session, ReportItem report)
    {
        var result = new List<AssignedUnitStatus>();
        if (!session.IsAuthenticated) return result;

        try
        {
            var tenantId = session.TenantId;
            var objId = report.ObjId;
            var orgId = !string.IsNullOrWhiteSpace(report.TargetOrgId) ? report.TargetOrgId : session.OrgId;
            var timeId = report.TimeId;

            var payload = new RestServicePayload
            {
                Func = "ajaxExecuteQueryO",
                Params = new List<object?> { "", "FNC006_S200", null },
                Fcode = "FNC006",
                Uuid = session.Uuid,
                Options = new List<QueryOption>
                {
                    new() { Name = "[0]", Value = tenantId },
                    new() { Name = "[1]", Value = tenantId },
                    new() { Name = "[2]", Value = tenantId },
                    new() { Name = "[3]", Value = $",{objId}," },
                    new() { Name = "[4]", Value = orgId },
                    new() { Name = "[5]", Value = timeId },
                    new() { Name = "[6]", Value = "" }
                }
            };

            var rows = await ExecuteRestServiceQueryAsync(session, payload);

            // Bảng map màu badge theo STATUS_STR (chuẩn theo AssignedUnitStatus)
            int stt = 1;
            foreach (var row in rows)
            {
                var unitName = GetFirstNonEmpty(row, "TITLE", "ORG_NAME", "UNIT_NAME", "");
                var orgIdStr = NormalizeIdString(GetFirstNonEmpty(row, "ORG_ID", ""));
                var statusStr = GetFirstNonEmpty(row, "STATUS_STR", "STATUS_NAME", "");
                var sndDate = GetFirstNonEmpty(row, "SND_DATE", "SUBMIT_DATE", "UPDATE_DATE", "");
                var rawStatus = GetString(row, "STATUS", "");

                // Nếu STATUS_STR rỗng thì map từ STATUS (int)
                if (string.IsNullOrWhiteSpace(statusStr) && !string.IsNullOrWhiteSpace(rawStatus))
                {
                    statusStr = rawStatus switch
                    {
                        "1" => "Đã giao",
                        "2" => "Đã trình lãnh đạo",
                        "3" => "Lãnh đạo đã duyệt",
                        "4" => "Báo cáo đã được duyệt cấp đơn vị giao",
                        "5" => "Báo cáo bị từ chối cấp đơn vị giao",
                        "6" => "Báo cáo cần đính chính",
                        _ => $"Trạng thái {rawStatus}"
                    };
                }

                if (string.IsNullOrWhiteSpace(unitName)) continue;

                int.TryParse(rawStatus, out var statusCode);
                if (statusCode == 0 && !string.IsNullOrWhiteSpace(statusStr))
                {
                    var s = statusStr.ToLowerInvariant();
                    if (s.Contains("đã giao")) statusCode = 1;
                    else if (s.Contains("trình")) statusCode = 2;
                    else if (s.Contains("đã gửi") || s.Contains("lãnh đạo đã duyệt")) statusCode = 3;
                    else if (s.Contains("đã được duyệt") || s.Contains("đã phê duyệt")) statusCode = 4;
                    else if (s.Contains("từ chối") || s.Contains("trả lại")) statusCode = 5;
                    else if (s.Contains("đính chính")) statusCode = 6;
                }

                result.Add(new AssignedUnitStatus
                {
                    Stt = stt++,
                    UnitName = unitName,
                    OrgCode = orgIdStr,
                    OrgId = orgIdStr,
                    StatusCode = statusCode,
                    StatusStr = statusStr,
                    SubmitDate = sndDate
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi GetUnitsSendingStatusViaFNC006Async: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Kiểm tra danh sách đơn vị được giao gửi báo cáo, lấy mã đơn vị ("org") và trạng thái nộp (STATUS_STR)
    /// Ưu tiên gọi FNC006_S200 trước; nếu không có kết quả thì fallback về FNC002_S08 + ws_recvMsgServlet
    /// </summary>
    public async Task<List<AssignedUnitStatus>> GetAssignedUnitsStatusAsync(AuthSession session, ReportItem report)
    {
        var result = new List<AssignedUnitStatus>();
        try
        {
            // ─── Ưu tiên 1: Gọi FNC006_S200 (API chuyên biệt cho kiểm tra đơn vị gửi báo cáo) ───
            if (session.IsAuthenticated && !string.IsNullOrWhiteSpace(report.ObjId))
            {
                var fnc006Result = await GetUnitsSendingStatusViaFNC006Async(session, report);
                if (fnc006Result.Count > 0)
                {
                    return fnc006Result;
                }
            }

            // ─── Fallback: FNC002_S08 + ws_recvMsgServlet getReport (logic cũ) ───
            var cfg = LoadIocApiConfig();
            var reportCode = ResolveReportCode(report, cfg);
            var (orgNameToCodeMap, orgNameToIdMap, defaultAssigneesMap) = LoadOrganizationMaps(cfg);
            var period = report.TimeId;

            // 1. Lấy danh sách tên các đơn vị được giao theo biểu mẫu
            var assignedNames = new List<string>();
            if (defaultAssigneesMap.TryGetValue(reportCode, out var cfgAssignees) && cfgAssignees.Count > 0)
            {
                assignedNames.AddRange(cfgAssignees);
            }

            // 2. Tra cứu trạng thái từ FNC002_S08 nếu phiên hợp lệ
            var statusMapByOrgName = new Dictionary<string, (string Status, string SubmitDate)>(StringComparer.OrdinalIgnoreCase);
            var statusMapByOrgId = new Dictionary<string, (string Status, string SubmitDate)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (session.IsAuthenticated)
                {
                    var orgType = string.IsNullOrWhiteSpace(session.OrgType) ? "2" : session.OrgType;
                    var payload = new RestServicePayload
                    {
                        Func = "ajaxExecuteQueryO",
                        Params = new List<object?> { "", "FNC002_S08", null },
                        Fcode = "FNC002",
                        Uuid = session.Uuid,
                        Options = new List<QueryOption>
                        {
                            new() { Name = "[0]", Value = session.TenantId },
                            new() { Name = "[1]", Value = "-1" },
                            new() { Name = "[2]", Value = "-1" },
                            new() { Name = "[3]", Value = -1 },
                            new() { Name = "[4]", Value = -1 },
                            new() { Name = "[5]", Value = "1" },
                            new() { Name = "[6]", Value = session.TenantId },
                            new() { Name = "[7]", Value = session.OrgId },
                            new() { Name = "[8]", Value = orgType },
                            new() { Name = "[9]", Value = orgType },
                            new() { Name = "[10]", Value = -1 },
                            new() { Name = "[11]", Value = -1 },
                            new() { Name = "[12]", Value = session.OrgId }
                        }
                    };

                    var rows = await ExecuteRestServiceQueryAsync(session, payload);
                    foreach (var row in rows)
                    {
                        var timeId = NormalizeIdString(GetString(row, "TIME_ID"));
                        var objId = NormalizeIdString(GetString(row, "OBJ_ID"));
                        if (timeId == report.TimeId || objId == report.ObjId)
                        {
                            var statusStr = GetFirstNonEmpty(row, "STATUS", "STATUS_NAME", "STATE_NAME", "");
                            var assignOrg = GetFirstNonEmpty(row, "ASSIGN_ORG", "ORG_NAME", "RECEIPT_NAME", "");
                            var orgId = NormalizeIdString(GetFirstNonEmpty(row, "ORG_ID", "RECEIPT_ID", ""));
                            var rawDate = GetFirstNonEmpty(row, "UPDATE_DATE", "SND_DATE", "START_DATE", "");
                            var fmtDate = FormatReportDate(rawDate);

                            if (!string.IsNullOrWhiteSpace(assignOrg))
                            {
                                if (!string.IsNullOrWhiteSpace(statusStr))
                                {
                                    statusMapByOrgName[assignOrg] = (statusStr, fmtDate);
                                }
                                if (!assignedNames.Contains(assignOrg, StringComparer.OrdinalIgnoreCase))
                                {
                                    assignedNames.Add(assignOrg);
                                }
                            }
                            if (!string.IsNullOrWhiteSpace(orgId) && !string.IsNullOrWhiteSpace(statusStr))
                            {
                                statusMapByOrgId[orgId] = (statusStr, fmtDate);
                            }
                        }
                    }
                }
            }
            catch { }

            if (assignedNames.Count == 0 && !string.IsNullOrWhiteSpace(report.OrgName))
            {
                assignedNames.Add(report.OrgName);
            }

            // 3. Chuẩn bị gọi API getReport song song cho từng đơn vị để xác định ngày gửi và dữ liệu thực tế
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds : 15)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            if (!string.IsNullOrWhiteSpace(cfg.Cookie))
            {
                client.DefaultRequestHeaders.Add("Cookie", cfg.Cookie);
            }

            var unitTasks = assignedNames.Distinct(StringComparer.OrdinalIgnoreCase).Select(async unitName =>
            {
                // Tìm mã org
                string orgCode = unitName;
                if (orgNameToCodeMap.TryGetValue(unitName, out var foundCode) && !string.IsNullOrWhiteSpace(foundCode))
                {
                    orgCode = foundCode;
                }
                else
                {
                    var cleanTarget = VietnameseNumberHelper.CleanSearchKey(unitName);
                    foreach (var (k, v) in orgNameToCodeMap)
                    {
                        if (VietnameseNumberHelper.CleanSearchKey(k) == cleanTarget)
                        {
                            orgCode = v;
                            break;
                        }
                    }
                }

                // Tìm ID đơn vị
                string orgId = "";
                if (orgNameToIdMap.TryGetValue(unitName, out var foundId))
                {
                    orgId = foundId;
                }

                string statusStr = "Đã giao";
                string submitDate = "";
                int dataRowCount = 0;

                if (statusMapByOrgName.TryGetValue(unitName, out var sInfo) && !string.IsNullOrWhiteSpace(sInfo.Status))
                {
                    statusStr = sInfo.Status;
                    submitDate = sInfo.SubmitDate;
                }
                else if (!string.IsNullOrWhiteSpace(orgId) && statusMapByOrgId.TryGetValue(orgId, out var sInfoId) && !string.IsNullOrWhiteSpace(sInfoId.Status))
                {
                    statusStr = sInfoId.Status;
                    submitDate = sInfoId.SubmitDate;
                }

                try
                {
                    var wsPayload = new
                    {
                        func = "getReport",
                        data = new
                        {
                            header = new
                            {
                                code = reportCode,
                                org = orgCode,
                                period = period
                            }
                        },
                        access_token = cfg.AccessToken
                    };

                    var jsonBody = JsonSerializer.Serialize(wsPayload);
                    using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    var wsResponse = await client.PostAsync(cfg.ApiUrl, content);
                    var wsJsonText = await wsResponse.Content.ReadAsStringAsync();

                    if (wsResponse.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(wsJsonText) && !wsJsonText.Contains("Request Rejected"))
                    {
                        using var doc = JsonDocument.Parse(wsJsonText);
                        var rootElem = doc.RootElement;
                        if (rootElem.TryGetProperty("err_code", out var errCodeProp) && errCodeProp.GetString() == "0")
                        {
                            if (rootElem.TryGetProperty("data", out var dataElem))
                            {
                                if (dataElem.TryGetProperty("header", out var hdrElem) && hdrElem.ValueKind == JsonValueKind.Object)
                                {
                                    var rawUpd = GetString(hdrElem, "updatedate");
                                    if (!string.IsNullOrWhiteSpace(rawUpd))
                                    {
                                        if (DateTime.TryParse(rawUpd.Split('.')[0], out var dtUpd))
                                        {
                                            submitDate = dtUpd.ToString("dd/MM/yyyy");
                                        }
                                        else
                                        {
                                            submitDate = rawUpd;
                                        }
                                    }

                                    var headerStatus = GetFirstNonEmpty(hdrElem, "status", "status_name", "state_name", "status_str");
                                    if (!string.IsNullOrWhiteSpace(headerStatus))
                                    {
                                        statusStr = headerStatus;
                                    }
                                }

                                if (dataElem.TryGetProperty("data", out var arrElem) && arrElem.ValueKind == JsonValueKind.Array)
                                {
                                    dataRowCount = arrElem.GetArrayLength();
                                }
                            }
                        }
                    }
                }
                catch { }

                // Cập nhật trạng thái thuần túy theo dữ liệu thực tế từ server
                if (dataRowCount > 0 && !string.IsNullOrWhiteSpace(submitDate))
                {
                    if (statusStr == "Đã giao")
                    {
                        statusStr = "Đã nộp báo cáo";
                    }
                }
                else if (dataRowCount == 0 && string.IsNullOrWhiteSpace(submitDate))
                {
                    statusStr = "Đã giao";
                }

                return new AssignedUnitStatus
                {
                    UnitName = unitName,
                    OrgCode = orgCode,
                    OrgId = orgId,
                    StatusStr = statusStr,
                    SubmitDate = submitDate
                };
            });

            var unitList = (await Task.WhenAll(unitTasks)).ToList();

            // 4. Sắp xếp theo thứ tự bảng chữ cái tiếng Việt (A -> Z)
            var viCulture = new System.Globalization.CultureInfo("vi-VN");
            var viComparer = StringComparer.Create(viCulture, false);
            var sortedUnits = unitList.OrderBy(u => u.UnitName, viComparer).ToList();

            int stt = 1;
            foreach (var u in sortedUnits)
            {
                u.Stt = stt++;
                result.Add(u);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi GetAssignedUnitsStatusAsync: {ex.Message}");
        }

        return result;
    }

    public static string ResolveOrgCode(AuthSession session, ReportItem report)
    {
        return !string.IsNullOrWhiteSpace(report.TargetOrgId) ? report.TargetOrgId : session.OrgId;
    }

    public static void RecalculateParentSums(List<IndicatorItem> indicators, List<ColumnHeaderInfo> headers)
    {
        if (indicators.Count == 0 || headers.Count == 0) return;

        var codeMap = new Dictionary<string, IndicatorItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var ind in indicators)
        {
            if (!string.IsNullOrWhiteSpace(ind.IndCode))
                codeMap[ind.IndCode] = ind;
            if (!string.IsNullOrWhiteSpace(ind.IndId))
                codeMap[ind.IndId] = ind;
            if (!string.IsNullOrWhiteSpace(ind.FieldId))
                codeMap[ind.FieldId] = ind;
        }

        var childrenByParent = indicators
            .Where(i => !string.IsNullOrWhiteSpace(i.ParentId))
            .GroupBy(i => i.ParentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var orderedIndicators = indicators
            .OrderByDescending(i => i.Level)
            .ThenByDescending(i => i.Stt)
            .ToList();

        foreach (var col in headers)
        {
            var colKey = col.FldCode;
            foreach (var ind in orderedIndicators)
            {
                if (ind.IsHeader)
                {
                    ind.SetColumnValue(colKey, "");
                    continue;
                }

                // 1. Nếu chỉ tiêu có FORMULA
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

                // 2. Nếu là chỉ tiêu tính toán phân cấp cha con (3: SUM, 4: AVG, 5: MAX, 6: MIN)
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

                        double result = ind.IndType switch
                        {
                            "4" => childValues.Average(),
                            "5" => childValues.Max(),
                            "6" => childValues.Min(),
                            _ => childValues.Sum()
                        };

                        var formatted = VietnameseNumberHelper.FormatVietnameseDouble(result);
                        ind.SetColumnValue(colKey, formatted);
                    }
                }
            }
        }
    }

    public static (double? Value, bool HasAnyInput) EvaluateFormula(string formula, Dictionary<string, IndicatorItem> codeMap, string colKey)
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
                    result = System.Convert.ToDouble(computed, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
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

    /// <summary>
    /// Tổng hợp số liệu báo cáo trực tiếp từ hệ thống IOC qua FNC006_S200 + FNC003_P03 + FNC003_P105.
    /// - onlyApproved = true: Chỉ lấy các đơn vị có trạng thái ĐÃ DUYỆT (StatusCode == 4 / Báo cáo đã được duyệt)
    /// - onlyApproved = false: Lấy tất cả đơn vị ngoại trừ trạng thái 'Đã giao' (StatusCode != 1)
    /// - Không sử dụng cơ chế có sẵn của Frontend (getReport) vì làm null thành 0.
    /// - Giữ nguyên null (rỗng) nếu không có đơn vị nào nhập số liệu cho chỉ tiêu đó.
    /// - Tính đúng các hàm tổng hợp (Sum, Avg, Max, Min) theo IND_TYPE và các công thức FORMULA.
    /// - Sau khi tổng hợp, tự động tính tổng phân cấp cha con và lưu vào IOC (FNC003_P220).
    /// </summary>
    public async Task<(bool Success, string Message, List<IndicatorItem> Indicators, List<ColumnHeaderInfo> Headers, string TopIndId)> AggregateReportAsync(
        AuthSession session,
        ReportItem report,
        List<IndicatorItem>? existingIndicators = null,
        List<ColumnHeaderInfo>? existingHeaders = null,
        string? existingTopIndId = null,
        bool onlyApproved = false)
    {
        if (!session.IsAuthenticated)
            return (false, "Chưa xác thực phiên đăng nhập", existingIndicators ?? new(), existingHeaders ?? new(), existingTopIndId ?? "");

        try
        {
            var indicators = existingIndicators;
            var headers = existingHeaders;
            var topIndId = existingTopIndId;
            string attrId = "";
            string submitType = "";

            if (indicators == null || indicators.Count == 0 || headers == null || headers.Count == 0 || string.IsNullOrWhiteSpace(topIndId))
            {
                var (loadedInds, loadedHeaders, loadedAttr, loadedTop, loadedSubmit, _, _, _, _, _, _, _, err) = await GetReportIndicatorsAsync(session, report);
                if (!string.IsNullOrWhiteSpace(err) && loadedInds.Count == 0)
                {
                    return (false, $"Lỗi tải chỉ tiêu báo cáo: {err}", loadedInds, loadedHeaders, loadedTop);
                }
                indicators = loadedInds;
                headers = loadedHeaders;
                attrId = loadedAttr;
                topIndId = loadedTop;
                submitType = loadedSubmit;
            }

            // 1. Lấy thông tin cấu trúc biểu mẫu (FNC003_P03) nếu chưa có attrId hoặc topIndId hoặc submitType
            if (string.IsNullOrWhiteSpace(attrId) || string.IsNullOrWhiteSpace(topIndId) || string.IsNullOrWhiteSpace(submitType))
            {
                var p03Payload = new RestServicePayload
                {
                    Func = "ajaxCALL_SP_O",
                    Params = new List<object?> { "FNC003_P03", report.ObjId, 0 },
                    Fcode = "FNC003",
                    Uuid = session.Uuid
                };
                var p03Rows = await ExecuteRestServiceQueryAsync(session, p03Payload);
                if (p03Rows.Count > 0)
                {
                    var r0 = p03Rows[0];
                    if (string.IsNullOrWhiteSpace(attrId)) attrId = GetString(r0, "ATTR_ID");
                    if (string.IsNullOrWhiteSpace(topIndId)) topIndId = GetString(r0, "IND_ID");
                    if (string.IsNullOrWhiteSpace(submitType)) submitType = GetString(r0, "SUBMIT_TYPE", "2");
                }
            }

            if (string.IsNullOrWhiteSpace(submitType))
            {
                submitType = ResolveSubmitType(report.PeriodTypeName, report.TimeName);
            }
            var preTimeId = CalculatePreTimeId(report.TimeId, submitType);

            // 2. Lấy danh sách đơn vị được giao và trạng thái của từng đơn vị (FNC006_S200)
            var assignedUnits = await GetAssignedUnitsStatusAsync(session, report);

            // Tập hợp ID của đơn vị chủ trì / tổng hợp cần loại trừ khỏi danh sách nộp số liệu
            var masterOrgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeIdString(session.OrgId),
                NormalizeIdString(report.TargetOrgId),
                "3105323" // UBND Tỉnh Hà Tĩnh
            };

            // 3. Lọc đơn vị theo chế độ:
            // - Loại trừ chính đơn vị tổng hợp / đơn vị giao (master org)
            // - onlyApproved: chỉ lấy StatusCode == 4 (Đã duyệt)
            // - all: lấy tất cả ngoại trừ StatusCode == 1 (Đã giao)
            var targetUnits = assignedUnits
                .Where(u => !masterOrgIds.Contains(NormalizeIdString(u.OrgId)) &&
                            !masterOrgIds.Contains(NormalizeIdString(u.OrgCode)))
                .Where(u => onlyApproved ? u.IsApproved : u.IsEligible)
                .ToList();

            var skippedUnits = assignedUnits.Where(u => !targetUnits.Contains(u)).ToList();

            if (targetUnits.Count == 0)
            {
                var reason = onlyApproved
                    ? "Không có đơn vị nào có báo cáo ở trạng thái 'Đã duyệt' để tổng hợp."
                    : "Không có đơn vị nào có số liệu mới (tất cả đơn vị đều ở trạng thái 'Đã giao').";
                return (false, reason, indicators, headers, topIndId);
            }

            // 4. Lấy danh sách công thức FNC003_S202
            var formulaMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var s202Payload = new RestServicePayload
                {
                    Func = "ajaxExecuteQueryO",
                    Params = new List<object?> { "", "FNC003_S202", null },
                    Fcode = "FNC003",
                    Uuid = session.Uuid,
                    Options = new List<QueryOption>
                    {
                        new() { Name = "[0]", Value = report.ObjId },
                        new() { Name = "[1]", Value = session.TenantId }
                    }
                };
                var s202Rows = await ExecuteRestServiceQueryAsync(session, s202Payload);
                foreach (var s202 in s202Rows)
                {
                    var fIndId = NormalizeIdString(GetString(s202, "IND_ID"));
                    var fIndCode = GetString(s202, "IND_CODE");
                    var formula = GetString(s202, "FORMULA");
                    if (!string.IsNullOrWhiteSpace(formula))
                    {
                        if (!string.IsNullOrWhiteSpace(fIndId)) formulaMap[fIndId] = formula;
                        if (!string.IsNullOrWhiteSpace(fIndCode)) formulaMap[fIndCode] = formula;
                    }
                }
            }
            catch { }

            // 5. Lần lượt gọi FNC003_P105 cho từng đơn vị hợp lệ để lấy dữ liệu thực tế
            // Map: indKey (IndCode / IndId / CleanName) -> Dictionary<colFldCode, List<string>>
            var collectedValuesByInd = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

            var successfulUnits = new List<string>();
            var failedUnits = new List<string>();

            foreach (var unit in targetUnits)
            {
                var unitOrgId = !string.IsNullOrWhiteSpace(unit.OrgId) ? unit.OrgId : unit.OrgCode;
                var paramStr = $"{session.TenantId}${report.ObjId}${attrId}${topIndId}${unitOrgId}${report.TimeId}${submitType}${preTimeId}";

                var p105Payload = new RestServicePayload
                {
                    Func = "ajaxCALL_SP_O",
                    Params = new List<object?> { "FNC003_P105", paramStr, 0 },
                    Fcode = "FNC003",
                    Uuid = session.Uuid
                };

                try
                {
                    var p105Rows = await ExecuteRestServiceQueryAsync(session, p105Payload);
                    if (p105Rows.Count == 0)
                    {
                        continue;
                    }

                    int unitDataCount = 0;
                    foreach (var row in p105Rows)
                    {
                        var uIndId = NormalizeIdString(GetString(row, "IND_ID"));
                        var uIndCode = GetString(row, "IND_CODE")?.Trim();

                        var keysToRegister = new List<string>();
                        if (!string.IsNullOrWhiteSpace(uIndId)) keysToRegister.Add(uIndId);
                        if (!string.IsNullOrWhiteSpace(uIndCode)) keysToRegister.Add(uIndCode);

                        foreach (var col in headers)
                        {
                            var colKey = col.FldCode;
                            string colVal = "";

                            var candidates = new List<string>();
                            if (!string.IsNullOrWhiteSpace(col.Field)) candidates.Add(col.Field);
                            if (!string.IsNullOrWhiteSpace(col.AttrId)) candidates.Add($"C{col.AttrId}");
                            if (!string.IsNullOrWhiteSpace(col.FldCode)) candidates.Add(col.FldCode);
                            if (!string.IsNullOrWhiteSpace(col.AttrCode)) candidates.Add(col.AttrCode);

                            foreach (var cand in candidates)
                            {
                                var candVal = GetString(row, cand);
                                if (!string.IsNullOrWhiteSpace(candVal) && candVal != "null" && candVal != "-")
                                {
                                    colVal = candVal;
                                    break;
                                }
                            }

                            if (string.IsNullOrEmpty(colVal) && row.TryGetProperty("ATTR_INFO", out var attrInfoProp) && attrInfoProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in attrInfoProp.EnumerateArray())
                                {
                                    var fld = GetString(item, "FIELD");
                                    var aid = GetString(item, "ATTR_ID");
                                    if ((!string.IsNullOrWhiteSpace(col.Field) && fld.Equals(col.Field, StringComparison.OrdinalIgnoreCase)) ||
                                        (!string.IsNullOrWhiteSpace(col.AttrId) && aid.Equals(col.AttrId, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        colVal = GetFirstNonEmpty(item, "VAL", "VAL_0", "VALUE", "");
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(colVal) && colVal != "null" && colVal != "-")
                            {
                                unitDataCount++;
                                foreach (var k in keysToRegister)
                                {
                                    if (!collectedValuesByInd.TryGetValue(k, out var colDict))
                                    {
                                        colDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                                        collectedValuesByInd[k] = colDict;
                                    }
                                    if (!colDict.TryGetValue(colKey, out var vList))
                                    {
                                        vList = new List<string>();
                                        colDict[colKey] = vList;
                                    }
                                    vList.Add(colVal);
                                }
                            }
                        }
                    }

                    if (unitDataCount > 0)
                    {
                        successfulUnits.Add(unit.UnitName);
                    }
                }
                catch (Exception ex)
                {
                    failedUnits.Add($"{unit.UnitName}: {ex.Message}");
                }
            }

            // 6. Ghi số liệu tổng hợp vào danh sách chỉ tiêu Master
            int totalUpdatedIndicators = 0;
            foreach (var ind in indicators)
            {
                if (ind.IsHeader)
                {
                    foreach (var h in headers)
                        ind.SetColumnValue(h.FldCode, "");
                    continue;
                }

                // Cập nhật công thức nếu có
                if (string.IsNullOrWhiteSpace(ind.Formula))
                {
                    if (!string.IsNullOrWhiteSpace(ind.IndId) && formulaMap.TryGetValue(ind.IndId, out var f1)) ind.Formula = f1;
                    else if (!string.IsNullOrWhiteSpace(ind.IndCode) && formulaMap.TryGetValue(ind.IndCode, out var f2)) ind.Formula = f2;
                }

                foreach (var h in headers)
                {
                    var colKey = h.FldCode;

                    // Tìm các giá trị đã thu thập được từ các đơn vị: Khớp chính xác tuyệt đối theo IND_ID hoặc IND_CODE (tránh trùng tên chỉ tiêu giữa các phòng ban)
                    List<string>? rawList = null;
                    if (!string.IsNullOrWhiteSpace(ind.IndId) && collectedValuesByInd.TryGetValue(NormalizeIdString(ind.IndId), out var d1) && d1.TryGetValue(colKey, out var l1))
                    {
                        rawList = l1;
                    }
                    else if (!string.IsNullOrWhiteSpace(ind.IndCode) && collectedValuesByInd.TryGetValue(ind.IndCode.Trim(), out var d2) && d2.TryGetValue(colKey, out var l2))
                    {
                        rawList = l2;
                    }

                    // QUY TẮC QUAN TRỌNG: Nếu không có đơn vị nào nhập -> để trống ("" / null), TUYỆT ĐỐI KHÔNG GÁN 0!
                    if (rawList == null || rawList.Count == 0)
                    {
                        ind.SetColumnValue(colKey, "");
                        continue;
                    }

                    if (rawList.Count == 1)
                    {
                        var formatted = VietnameseNumberHelper.FormatVietnameseNumberFromIoc(rawList[0]);
                        ind.SetColumnValue(colKey, formatted);
                        ind.IsModified = true;
                        totalUpdatedIndicators++;
                    }
                    else
                    {
                        // Nhiều đơn vị cùng nhập cho 1 chỉ tiêu -> tính toán theo IND_TYPE
                        var numList = new List<double>();
                        foreach (var raw in rawList)
                        {
                            // Giá trị tổng hợp được lấy trực tiếp từ IOC, dùng dấu chấm thập phân.
                            var clean = raw.Trim();
                            if (double.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                            {
                                numList.Add(d);
                            }
                        }

                        if (numList.Count == 0)
                        {
                            ind.SetColumnValue(colKey, "");
                        }
                        else
                        {
                            double result = ind.IndType switch
                            {
                                "4" => numList.Average(), // Trung bình cộng
                                "5" => numList.Max(),     // Lớn nhất
                                "6" => numList.Min(),     // Nhỏ nhất
                                _ => numList.Sum()      // Tổng
                            };
                            var formatted = VietnameseNumberHelper.FormatVietnameseDouble(result);
                            ind.SetColumnValue(colKey, formatted);
                            ind.IsModified = true;
                            totalUpdatedIndicators++;
                        }
                    }
                }
            }

            // 7. Tự động tính toán công thức (FORMULA) và phân cấp cây cha - con
            RecalculateParentSums(indicators, headers);

            // 8. Lưu số liệu tổng hợp vào cơ sở dữ liệu server qua FNC003_P220
            var (saveSuccess, saveMsg) = await SaveReportDataAsync(session, report, indicators, headers, topIndId);
            if (!saveSuccess)
            {
                return (false, $"Đã tổng hợp số liệu ({totalUpdatedIndicators} chỉ tiêu) nhưng lỗi lưu lên IOC: {saveMsg}", indicators, headers, topIndId);
            }

            var modeText = onlyApproved ? "báo cáo đã duyệt" : "tất cả báo cáo hợp lệ";
            var skipInfo = skippedUnits.Count > 0 ? $", bỏ qua {skippedUnits.Count} đơn vị không thuộc phạm vi" : "";
            var unitSummary = successfulUnits.Count > 0 ? $" từ {successfulUnits.Count} đơn vị ({string.Join(", ", successfulUnits.Take(3))}{(successfulUnits.Count > 3 ? "..." : "")})" : "";

            return (true, $"Đã tổng hợp {modeText} thành công ({totalUpdatedIndicators} chỉ tiêu{unitSummary}{skipInfo}) và lưu vào Báo cáo tổng hợp!", indicators, headers, topIndId);
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi tổng hợp báo cáo: {ex.Message}", existingIndicators ?? new(), existingHeaders ?? new(), existingTopIndId ?? "");
        }
    }

    /// <summary>
    /// Tổng hợp hàng loạt nhiều báo cáo song song với số luồng tùy chỉnh
    /// </summary>
    public async Task<(int SuccessCount, int FailCount, List<string> ErrorMessages)> BatchAggregateReportsAsync(
        AuthSession session,
        List<ReportItem> reports,
        int maxDegreeOfParallelism,
        IProgress<(int Current, int Total, string ReportName, bool Success, string Message)>? progress = null,
        bool onlyApproved = false)
    {
        if (!session.IsAuthenticated || reports.Count == 0)
            return (0, 0, new List<string>());

        var semaphore = new SemaphoreSlim(Math.Max(1, Math.Min(20, maxDegreeOfParallelism)));
        int total = reports.Count;
        int current = 0;
        int successCount = 0;
        int failCount = 0;
        var errorMessages = new List<string>();

        var tasks = reports.Select(async report =>
        {
            await semaphore.WaitAsync();
            try
            {
                var (success, msg, _, _, _) = await AggregateReportAsync(session, report, onlyApproved: onlyApproved);
                int done = Interlocked.Increment(ref current);
                if (success)
                {
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    Interlocked.Increment(ref failCount);
                    lock (errorMessages)
                    {
                        errorMessages.Add($"{report.ObjName} ({report.TimeName}): {msg}");
                    }
                }
                progress?.Report((done, total, report.ObjName, success, msg));
            }
            catch (Exception ex)
            {
                int done = Interlocked.Increment(ref current);
                Interlocked.Increment(ref failCount);
                lock (errorMessages)
                {
                    errorMessages.Add($"{report.ObjName}: {ex.Message}");
                }
                progress?.Report((done, total, report.ObjName, false, ex.Message));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return (successCount, failCount, errorMessages);
    }
}
