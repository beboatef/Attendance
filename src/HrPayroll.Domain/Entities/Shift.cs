namespace HrPayroll.Domain.Entities;

/// <summary>وردية عمل.</summary>
public class Shift : BaseEntity
{
    public string ShiftName { get; set; } = string.Empty;

    /// <summary>وقت البداية (وقت محلي).</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>وقت النهاية (وقت محلي). قد يكون في اليوم التالي إذا كانت ليلية.</summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>دقائق السماح قبل احتساب التأخير.</summary>
    public int GraceMinutes { get; set; }

    /// <summary>هل الوردية تعبر منتصف الليل؟</summary>
    public bool IsOvernight { get; set; }

    /// <summary>معامل الإضافي لهذه الوردية.</summary>
    public decimal OvertimeMultiplier { get; set; } = 1m;

    public bool IsActive { get; set; } = true;

    /// <summary>أقل مدة إضافي معتبرة بالدقائق (لتجاهل الدقائق الضئيلة).</summary>
    public int OvertimeMinMinutes { get; set; }

    public string? Notes { get; set; }

    public ICollection<EmployeeShiftAssignment> Assignments { get; set; } = new List<EmployeeShiftAssignment>();
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
