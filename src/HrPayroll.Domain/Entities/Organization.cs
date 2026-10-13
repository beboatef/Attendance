namespace HrPayroll.Domain.Entities;

/// <summary>قسم (بيانات مرجعية قابلة لإدارتها).</summary>
public class Department : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? ManagerName { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }

    public ICollection<JobTitle> JobTitles { get; set; } = new List<JobTitle>();
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}

/// <summary>مسمى وظيفي (بيانات مرجعية قابلة لإدارتها).</summary>
public class JobTitle : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}

/// <summary>عطلة رسمية.</summary>
public class PublicHoliday : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public DateTime HolidayDate { get; set; }

    /// <summary>هل العطلة مدفوعة الأجر (مشمولة في الراتب الشهري)؟</summary>
    public bool IsPaid { get; set; } = true;

    /// <summary>هل تُحتسب قيمتها كيوم إضافي مستحق إذا عمل الموظف فيها؟</summary>
    public bool AllowWorkWithExtraPay { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
