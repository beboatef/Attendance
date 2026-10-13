using System.ComponentModel.DataAnnotations;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HrPayroll.Web.ViewModels;

public class EmployeeFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "كود الموظف مطلوب")]
    [Display(Name = "كود الموظف")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Display(Name = "كود البصمة / الجهاز (اختياري)")]
    public string? DeviceEmployeeCode { get; set; }

    [Required(ErrorMessage = "الاسم مطلوب")]
    [Display(Name = "الاسم الكامل")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "المسمى الوظيفي")]
    public string? JobTitle { get; set; }

    [Display(Name = "القسم")]
    public string? Department { get; set; }

    [Display(Name = "القسم (بيانات مرجعية)")]
    public int? DepartmentId { get; set; }

    [Display(Name = "المسمى الوظيفي (بيانات مرجعية)")]
    public int? JobTitleId { get; set; }

    [Display(Name = "الوردية الأساسية")]
    public int? ShiftId { get; set; }

    [Required(ErrorMessage = "الراتب الأساسي مطلوب")]
    [Range(0.01, 100000000, ErrorMessage = "الراتب يجب أن يكون أكبر من صفر")]
    [Display(Name = "الراتب الأساسي الشهري")]
    public decimal BasicSalary { get; set; }

    [Range(0, 100000000, ErrorMessage = "الأجر اليومي غير صالح")]
    [Display(Name = "الأجر اليومي (اتركه فارغًا لاستخدام سياسة الشركة)")]
    public decimal? DailyRate { get; set; }

    [Range(0.5, 24, ErrorMessage = "ساعات العمل اليومية يجب أن تكون بين 0.5 و 24")]
    [Display(Name = "ساعات العمل القياسية يوميًا")]
    public decimal StandardWorkHours { get; set; } = 8m;

    [Range(0, 365, ErrorMessage = "رصيد الإجازة غير صالح")]
    [Display(Name = "استحقاق الإجازة السنوية (يوم)")]
    public decimal AnnualLeaveEntitlement { get; set; }

    [Range(0, 365, ErrorMessage = "الرصيد المرحَّل غير صالح")]
    [Display(Name = "الرصيد المرحَّل من السنة السابقة (يوم)")]
    public decimal AnnualLeaveCarriedOver { get; set; }

    [Range(0, 365, ErrorMessage = "الرصيد المتبقي غير صالح")]
    [Display(Name = "الرصيد المتبقي من الإجازة")]
    public decimal AnnualLeaveBalance { get; set; }

    /// <summary>إجمالي الرصيد المتاح = المستحق + المرحَّل.</summary>
    public decimal AnnualLeaveTotal => AnnualLeaveEntitlement + AnnualLeaveCarriedOver;

    /// <summary>الأيام المستهلكة = المتاح − المتبقي.</summary>
    public decimal AnnualLeaveUsed => Math.Max(0m, AnnualLeaveTotal - AnnualLeaveBalance);

    /// <summary>رصيد مقترح لموظف جديد = المستحق + المرحَّل.</summary>
    public decimal SuggestedBalance => AnnualLeaveTotal;

    [Required(ErrorMessage = "تاريخ التعيين مطلوب")]
    [DataType(DataType.Date)]
    [Display(Name = "تاريخ التعيين")]
    public DateTime HireDate { get; set; } = DateTime.Today;

    [Display(Name = "الحالة الوظيفية")]
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Active;

    [Display(Name = "تاريخ انتهاء الخدمة")]
    [DataType(DataType.Date)]
    public DateTime? TerminationDate { get; set; }

    [EmailAddress(ErrorMessage = "صيغة البريد غير صحيحة")]
    [Display(Name = "البريد الإلكتروني")]
    public string? Email { get; set; }

    [Display(Name = "الهاتف")]
    public string? Phone { get; set; }

    [Display(Name = "بيانات تواصل إضافية")]
    public string? ContactInfo { get; set; }

    [Display(Name = "الرقم القومي")]
    public string? NationalId { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<Shift> Shifts { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<JobTitle> JobTitles { get; set; } = new();

    public SelectList ShiftList => new(Shifts, "Id", "ShiftName", ShiftId);
    public SelectList DepartmentList => new(Departments, "Id", "Name", DepartmentId);
    public SelectList JobTitleList => new(JobTitles, "Id", "Name", JobTitleId);
    public SelectList EmploymentStatusList => new(Enum.GetValues<EmploymentStatus>().Select(s => new { Id = (int)s, Name = s.ToString() }), "Id", "Name", (int)EmploymentStatus);

    public static EmployeeFormViewModel FromEntity(Employee e) => new()
    {
        Id = e.Id,
        EmployeeCode = e.EmployeeCode,
        DeviceEmployeeCode = e.DeviceEmployeeCode,
        FullName = e.FullName,
        JobTitle = e.JobTitle,
        Department = e.Department,
        DepartmentId = e.DepartmentId,
        JobTitleId = e.JobTitleId,
        ShiftId = e.ShiftId,
        BasicSalary = e.BasicSalary,
        DailyRate = e.DailyRate,
        StandardWorkHours = e.StandardWorkHours <= 0 ? 8m : e.StandardWorkHours,
        AnnualLeaveEntitlement = e.AnnualLeaveEntitlement,
        AnnualLeaveCarriedOver = e.AnnualLeaveCarriedOver,
        AnnualLeaveBalance = e.AnnualLeaveBalance,
        HireDate = e.HireDate,
        EmploymentStatus = e.EmploymentStatus,
        TerminationDate = e.TerminationDate,
        Email = e.Email,
        Phone = e.Phone,
        ContactInfo = e.ContactInfo,
        NationalId = e.NationalId,
        Notes = e.Notes
    };
}

public class EmployeeIndexViewModel
{
    public List<Employee> Employees { get; set; } = new();
    public string? Search { get; set; }
    public string? DepartmentFilter { get; set; }
    public int? DepartmentId { get; set; }
    public int? ShiftId { get; set; }
    public EmploymentStatus? EmploymentStatus { get; set; }
    public bool? IsActive { get; set; }
    public List<string> Departments { get; set; } = new();
    public List<Department> DepartmentRefs { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
