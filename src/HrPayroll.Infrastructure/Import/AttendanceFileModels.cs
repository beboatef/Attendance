namespace HrPayroll.Infrastructure.Import;

/// <summary>نوع الملف المكتشف فعليًا من محتواه (وليس من امتداده).</summary>
public enum AttendanceFileFormat
{
    Unknown = 0,
    Xlsx = 1,          // ملف Excel حديث (ZIP/OOXML)
    Biff8 = 2,         // ملف Excel ثنائي قديم BIFF (.xls)
    SpreadsheetXml = 3,// Excel 2003 XML (يصدّره كثير من أجهزة البصمة بامتداد .xls)
    HtmlTable = 4,     // جدول HTML بامتداد .xls
    Csv = 5            // نص مفصول بفواصل
}

/// <summary>ورقة مستخرجة (صفوف نصية).</summary>
public sealed class ParsedSheet
{
    public required AttendanceFileFormat Format { get; init; }
    public required List<string[]> Rows { get; init; }
    public int ColumnCount { get; init; }
    public string? SheetName { get; init; }
}

/// <summary>صف بصمة خام بعد التحليل.</summary>
public sealed class ParsedPunchRow
{
    public int RowNumber { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceEmployeeCode { get; set; }
    public string? Name { get; set; }
    public string? AttType { get; set; }
    public string? RawPunchTime { get; set; }
    public DateTime? PunchTime { get; set; }
    public bool IsTimeValid { get; set; }
    public string? ValidationMessage { get; set; }
}

/// <summary>خريطة الأعمدة المكتشفة.</summary>
public sealed class ColumnMapping
{
    public int? No { get; set; }
    public int? DeviceId { get; set; }
    public int? EmployeeCode { get; set; }
    public int? Name { get; set; }
    public int? AttType { get; set; }
    public int? AttTime { get; set; }
    public int HeaderRowIndex { get; set; }

    public bool IsUsable => EmployeeCode.HasValue && AttTime.HasValue;
}
