namespace VSR.Helpers;

/// <summary>
/// Định nghĩa các tên cột và trường dữ liệu (Field/Attribute Keys) từ hệ thống IOC.
/// Các hằng số này được ánh xạ chi tiết kèm chú thích trong ioc_fields_config.json.
/// </summary>
public static class IocFields
{
    // --- Đối tượng / Báo cáo ---
    /// <summary>Mã ID đối tượng báo cáo / ID biểu mẫu</summary>
    public const string ObjId = "OBJ_ID";
    /// <summary>Mã ký hiệu của biểu mẫu báo cáo (VD: BC01, NQ71...)</summary>
    public const string ObjCode = "OBJ_CODE";
    /// <summary>Tên biểu mẫu báo cáo</summary>
    public const string ObjName = "OBJ_NAME";

    // --- Cột / Thuộc tính (Attribute) ---
    /// <summary>Mã ID thuộc tính / Mã cột dữ liệu</summary>
    public const string AttrId = "ATTR_ID";
    /// <summary>Mã định danh cột trong bảng biểu</summary>
    public const string AttrCode = "ATTR_CODE";
    /// <summary>Giá trị dữ liệu của ô tương ứng</summary>
    public const string AttrVal = "ATTR_VAL";
    /// <summary>Thông tin bổ sung / metadata của thuộc tính</summary>
    public const string AttrInfo = "ATTR_INFO";

    // --- Dòng / Chỉ tiêu (Indicator) ---
    /// <summary>Mã ID chỉ tiêu (Dòng dữ liệu)</summary>
    public const string IndId = "IND_ID";
    /// <summary>Mã ký hiệu chỉ tiêu</summary>
    public const string IndCode = "IND_CODE";
    /// <summary>Tên hiển thị chỉ tiêu</summary>
    public const string IndName = "IND_NAME";
    /// <summary>Đơn vị tính của chỉ tiêu</summary>
    public const string IndUnit = "IND_UNIT";
    /// <summary>Kiểu chỉ tiêu (cha / con / tổng hợp...)</summary>
    public const string IndType = "IND_TYPE";
    /// <summary>Số thứ tự dòng chỉ tiêu</summary>
    public const string IndIndex = "IND_INDEX";
    /// <summary>Mã ID chỉ tiêu cha (dùng xây dựng cây phân cấp)</summary>
    public const string ParentId = "PARENT_ID";
    /// <summary>Cờ đánh dấu nút lá (không có con: 1 là lá, 0 là nhóm cha)</summary>
    public const string IsLeaf = "IS_LEAF";
    /// <summary>Công thức tính toán của chỉ tiêu</summary>
    public const string Formula = "FORMULA";

    // --- Kỳ / Thời gian (Time) ---
    /// <summary>Mã kỳ thời gian báo cáo</summary>
    public const string TimeId = "TIME_ID";
    /// <summary>Tên kỳ thời gian báo cáo (Tháng X, Quý Y...)</summary>
    public const string TimeName = "TIME_NAME";

    // --- Loại gửi / Phê duyệt / Trạng thái ---
    /// <summary>Loại hình nộp / chế độ nộp báo cáo (Ví dụ: REGULAR, ADJUST...)</summary>
    public const string SubmitType = "SUBMIT_TYPE";
    /// <summary>Mã trạng thái báo cáo (1: Chưa gửi, 2: Chờ duyệt, 3: Đã duyệt, 4: Từ chối...)</summary>
    public const string StateId = "STATE_ID";
    /// <summary>Tên trạng thái báo cáo</summary>
    public const string StateName = "STATE_NAME";
    /// <summary>Ghi chú / Nhận xét của báo cáo</summary>
    public const string Note = "NOTE";
    /// <summary>Yêu cầu đính chính / lý do từ chối</summary>
    public const string CorrectionReq = "CORRECTION_REQ";

    // --- Cấu hình giao diện & Kiểu dữ liệu cột ---
    /// <summary>Cờ đánh dấu cột bị ẩn theo cấu hình IOC (1: Ẩn, 0: Hiện)</summary>
    public const string IsHidden = "IS_HIDDEN";
    /// <summary>Trạng thái hiển thị cột (1: Hiện, 0: Ẩn)</summary>
    public const string Visible = "VISIBLE";
    /// <summary>Độ rộng cột hiển thị</summary>
    public const string ColWidth = "COL_WIDTH";
    /// <summary>Kiểu dữ liệu cột (NUMBER, TEXT, DATE...)</summary>
    public const string DataType = "DATA_TYPE";
    /// <summary>Căn lề hiển thị (LEFT, RIGHT, CENTER)</summary>
    public const string Align = "ALIGN";
    /// <summary>Cỡ chữ hiển thị</summary>
    public const string FontSize = "FONT_SIZE";
    /// <summary>Mã trường ánh xạ dữ liệu</summary>
    public const string FldCode = "FLD_CODE";
}

/// <summary>
/// Danh sách các Stored Procedures (Thủ tục SP) gọi qua REST Service của IOC.
/// </summary>
public static class IocProcedures
{
    /// <summary>Lấy danh sách báo cáo đầu vào</summary>
    public const string GetReportInput = "FNC002_S01";
    /// <summary>Lấy danh sách biểu mẫu tổng hợp</summary>
    public const string GetAggregatedReports = "FNC002_S06";
    /// <summary>Lấy danh sách báo cáo theo đơn vị / kỳ</summary>
    public const string GetReportsByUnit = "FNC002_S04";
    /// <summary>Lấy danh sách lịch sử nộp</summary>
    public const string GetSubmissionHistory = "FNC002_S08";
    /// <summary>Lấy cấu trúc dòng/cột của biểu mẫu</summary>
    public const string GetStructure = "FNC003_P03";
    /// <summary>Lấy thông tin thuộc tính cột chi tiết</summary>
    public const string GetColumnAttributes = "FNC003_S13";
    /// <summary>Lấy danh sách cột động</summary>
    public const string GetDynamicColumns = "FNC003_S315";
    /// <summary>Lấy dữ liệu ma trận dòng/cột</summary>
    public const string GetMatrixData = "FNC003_S312";
    /// <summary>Lấy danh sách chỉ tiêu / dòng của biểu mẫu</summary>
    public const string GetIndicators = "FNC003_P105";
    /// <summary>Lấy dữ liệu chi tiết của báo cáo</summary>
    public const string GetReportData = "FNC003_S202";
    /// <summary>Lưu / cập nhật dữ liệu báo cáo</summary>
    public const string SaveReport = "FNC003_P220";
    /// <summary>Lấy danh mục đơn vị phòng ban</summary>
    public const string GetDepartments = "FNC006_S200";
    /// <summary>Cập nhật trạng thái duyệt báo cáo</summary>
    public const string ApproveReport = "FNC002_P030";
    /// <summary>Gửi báo cáo lên cấp trên</summary>
    public const string SubmitReport = "FNC002_P02";
    /// <summary>Duyệt báo cáo</summary>
    public const string AcceptReport = "FNC002_P03";
    /// <summary>Từ chối duyệt / yêu cầu đính chính</summary>
    public const string RejectReport = "FNC002_P04";
    /// <summary>Hủy gửi báo cáo</summary>
    public const string CancelSubmitReport = "FNC002_P07";
}

/// <summary>
/// Danh sách các mã chức năng phân hệ (Function Codes) của IOC.
/// </summary>
public static class IocFunctionCodes
{
    /// <summary>Phân hệ Quản lý & duyệt báo cáo</summary>
    public const string Fnc002 = "FNC002";
    /// <summary>Phân hệ Nhập liệu & cấu trúc báo cáo biểu mẫu</summary>
    public const string Fnc003 = "FNC003";
    /// <summary>Phân hệ Danh mục & hệ thống</summary>
    public const string Fnc006 = "FNC006";
}

/// <summary>
/// Các hàm thao tác API REST trên máy chủ IOC.
/// </summary>
public static class IocOperations
{
    /// <summary>Gọi thủ tục SP dạng truy vấn danh sách (Query)</summary>
    public const string CallSpQuery = "ajaxCALL_SP_O";
    /// <summary>Gọi thủ tục SP dạng lưu / cập nhật (Save)</summary>
    public const string CallSpSave = "ajaxCALL_SP_S";
    /// <summary>Gọi thủ tục SP dạng thực thi/ghi dữ liệu (Insert/Action)</summary>
    public const string CallSpInsert = "ajaxCALL_SP_I";
    /// <summary>Thực thi câu truy vấn SQL tùy chỉnh</summary>
    public const string ExecuteQuery = "ajaxExecuteQueryO";
}
