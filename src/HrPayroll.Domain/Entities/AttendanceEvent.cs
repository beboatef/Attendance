using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>البصمة بعد تصنيفها كدخول أو خروج.</summary>
public class AttendanceEvent : BaseEntity
{
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>البصمة الخام المرتبطة (ربط صارم يحافظ على الأصل).</summary>
    public int RawPunchId { get; set; }
    public RawPunch? RawPunch { get; set; }

    /// <summary>تاريخ الوردية الذي تتبعه هذه الحركة (قد يختلف عن تاريخ البصمة في الورديات الليلية).</summary>
    public DateTime WorkDate { get; set; }

    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public AttendanceEventType EventType { get; set; }
    public DateTime EventTime { get; set; }

    public ClassificationStatus ClassificationStatus { get; set; }
    public ClassificationSource ClassificationSource { get; set; }

    /// <summary>درجة الثقة 0..1 عند التصنيف الآلي.</summary>
    public decimal? Confidence { get; set; }

    public bool IsManual { get; set; }
    public string? Notes { get; set; }
}
