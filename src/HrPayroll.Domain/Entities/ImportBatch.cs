using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>دفعة استيراد ملف بصمات.</summary>
public class ImportBatch : BaseEntity
{
    public string FileName { get; set; } = string.Empty;

    /// <summary>هاش محتوى الملف لمنع إعادة الاستيراد بالخطأ.</summary>
    public string? FileHash { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public string? ImportedByUserId { get; set; }
    public string? ImportedByName { get; set; }

    /// <summary>نوع الملف المكتشف فعليًا (Biff8/Xlsx/SpreadsheetXml/Csv).</summary>
    public string? DetectedFormat { get; set; }

    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int DuplicateRows { get; set; }
    public int InvalidRows { get; set; }
    public int UnknownEmployeeRows { get; set; }

    public string? Notes { get; set; }

    public ICollection<RawPunch> RawPunches { get; set; } = new List<RawPunch>();
}
