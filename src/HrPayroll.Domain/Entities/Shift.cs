using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

/// <summary>وردية عمل.</summary>
public class Shift : BaseEntity
{
    public string ShiftName { get; set; } = string.Empty;

    /// <summary>ترتيب العرض والاستخدام (1، 2، 3 ...).</summary>
    public int ShiftNumber { get; set; }

    /// <summary>وقت البداية (وقت محلي).</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>وقت النهاية (وقت محلي). قد يكون في اليوم التالي إذا كانت الوردية ليلية.</summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>عدد ساعات الوردية القياسية (8 ساعات افتراضيًا). وقت النهاية يُحسب من البداية + هذه المدة.</summary>
    public decimal StandardHours { get; set; } = 8m;

    /// <summary>دقائق السماح عند الدخول قبل احتساب التأخير.</summary>
    public int EntryGraceMinutes { get; set; } = 30;

    /// <summary>دقائق السماح عند الخروج قبل احتساب الإضافي.</summary>
    public int ExitGraceMinutes { get; set; } = 30;

    /// <summary>أيام العمل المطبقة على الوردية (0=الأحد ... 6=السبت)، مثال: "0,1,2,3,4,6".</summary>
    public string? WorkingDays { get; set; }

    /// <summary>هل الوردية تعبر منتصف الليل؟</summary>
    public bool IsOvernight { get; set; }

    /// <summary>معامل الإضافي لهذه الوردية. سياسة الشركة = 1 (بدون مضاعفة).</summary>
    public decimal OvertimeMultiplier { get; set; } = 1m;

    public bool IsActive { get; set; } = true;

    /// <summary>أقل مدة إضافي معتبرة بالدقائق (لتجاهل الدقائق الضئيلة).</summary>
    public int OvertimeMinMinutes { get; set; }

    public string? Notes { get; set; }

    public ICollection<EmployeeShiftAssignment> Assignments { get; set; } = new List<EmployeeShiftAssignment>();
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();

    /// <summary>أيام العمل كمجموعة DayOfWeek (فارغة = كل الأيام).</summary>
    public IReadOnlySet<DayOfWeek> WorkingDaySet()
    {
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(WorkingDays)) return set;
        foreach (var part in WorkingDays.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part, out var d) && d is >= 0 and <= 6) set.Add((DayOfWeek)d);
        return set;
    }

    public bool WorksOn(DayOfWeek day)
    {
        var set = WorkingDaySet();
        return set.Count == 0 || set.Contains(day);
    }

    /// <summary>وقت النهاية المحسوب من وقت البداية + الساعات القياسية (بدون تعديل الكيان).</summary>
    public TimeSpan ComputeEndTime()
    {
        var minutes = (int)Math.Round(StandardHours * 60m, MidpointRounding.AwayFromZero);
        var total = (int)StartTime.TotalMinutes + minutes;
        return TimeSpan.FromMinutes(((total % 1440) + 1440) % 1440);
    }

    /// <summary>هل تتجاوز الوردية منتصف الليل بناءً على البداية والمدة؟</summary>
    public bool ComputeIsOvernight()
        => (int)StartTime.TotalMinutes + (int)Math.Round(StandardHours * 60m, MidpointRounding.AwayFromZero) >= 1440;
}
