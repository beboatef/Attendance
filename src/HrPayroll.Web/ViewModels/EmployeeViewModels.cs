using System.ComponentModel.DataAnnotations;
using HrPayroll.Domain.Entities;

namespace HrPayroll.Web.ViewModels;

public class EmployeeFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "كود الموظف مطلوب")]
    [Display(Name = "كود الموظف")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Display(Name = "كود الجهاز (اختياري)")]
    public string? DeviceEmployeeCode { get; set; }

    [Required(ErrorMessage = "الاسم مطلوب")]
    [Display(Name = "الاسم الكامل")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "المسمى الوظيفي")]
    public string? JobTitle { get; set; }

    [Display(Name = "القسم")]
    public string? Department { get; set; }

    [Display(Name = "الوردية الأساسية")]
    public int? ShiftId { get; set; }

    [Required(ErrorMessage = "الراتب الأساسي مطلوب")]
    [Range(0.01, 10000000, ErrorMessage = "الراتب يجب أن يكون أكبر من صفر")]
    [Display(Name = "الراتب الأساسي")]
    public decimal BasicSalary { get; set; }

    [Required(ErrorMessage = "تاريخ التعيين مطلوب")]
    [DataType(DataType.Date)]
    [Display(Name = "تاريخ التعيين")]
    public DateTime HireDate { get; set; } = DateTime.Today;

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [EmailAddress(ErrorMessage = "صيغة البريد غير صحيحة")]
    [Display(Name = "البريد الإلكتروني")]
    public string? Email { get; set; }

    [Display(Name = "الهاتف")]
    public string? Phone { get; set; }

    [Display(Name = "الرقم القومي")]
    public string? NationalId { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<Shift> Shifts { get; set; } = new();

    public static EmployeeFormViewModel FromEntity(Employee e) => new()
    {
        Id = e.Id,
        EmployeeCode = e.EmployeeCode,
        DeviceEmployeeCode = e.DeviceEmployeeCode,
        FullName = e.FullName,
        JobTitle = e.JobTitle,
        Department = e.Department,
        ShiftId = e.ShiftId,
        BasicSalary = e.BasicSalary,
        HireDate = e.HireDate,
        IsActive = e.IsActive,
        Email = e.Email,
        Phone = e.Phone,
        NationalId = e.NationalId,
        Notes = e.Notes
    };
}

public class EmployeeIndexViewModel
{
    public List<Employee> Employees { get; set; } = new();
    public string? Search { get; set; }
    public int? Department { get; set; }
    public bool? IsActive { get; set; }
    public string? DepartmentFilter { get; set; }
    public List<string> Departments { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
