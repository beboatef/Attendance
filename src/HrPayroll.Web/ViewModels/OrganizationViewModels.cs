using System.ComponentModel.DataAnnotations;

namespace HrPayroll.Web.ViewModels;

public class DepartmentFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم القسم مطلوب")]
    [Display(Name = "اسم القسم")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "كود القسم")]
    public string? Code { get; set; }

    [Display(Name = "مدير القسم")]
    public string? ManagerName { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public class JobTitleFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم المسمى الوظيفي مطلوب")]
    [Display(Name = "المسمى الوظيفي")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "كود")]
    public string? Code { get; set; }

    [Display(Name = "القسم")]
    public int? DepartmentId { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<Domain.Entities.Department> Departments { get; set; } = new();
}

public class HolidayFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم العطلة مطلوب")]
    [Display(Name = "اسم العطلة")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "تاريخ العطلة مطلوب")]
    [DataType(DataType.Date)]
    [Display(Name = "التاريخ")]
    public DateTime HolidayDate { get; set; } = DateTime.Today;

    [Display(Name = "مدفوعة الأجر")]
    public bool IsPaid { get; set; } = true;

    [Display(Name = "يُصرف مقابل العمل فيها بسياسة منفصلة")]
    public bool AllowWorkWithExtraPay { get; set; }

    [Display(Name = "نشطة")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}
