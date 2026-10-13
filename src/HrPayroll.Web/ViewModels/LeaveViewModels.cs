using System.ComponentModel.DataAnnotations;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HrPayroll.Web.ViewModels;

public class LeaveFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public int EmployeeId { get; set; }

    [Required(ErrorMessage = "نوع الإجازة مطلوب")]
    [Display(Name = "نوع الإجازة")]
    public LeaveType LeaveType { get; set; } = LeaveType.Annual;

    [Required(ErrorMessage = "تاريخ البداية مطلوب")]
    [DataType(DataType.Date)]
    [Display(Name = "من تاريخ")]
    public DateTime StartDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "تاريخ النهاية مطلوب")]
    [DataType(DataType.Date)]
    [Display(Name = "إلى تاريخ")]
    public DateTime EndDate { get; set; } = DateTime.Today;

    [Display(Name = "مدفوعة الأجر")]
    public bool IsPaid { get; set; } = true;

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [Display(Name = "اعتماد فوري")]
    public bool ApproveImmediately { get; set; }

    public List<Employee> Employees { get; set; } = new();
    public SelectList EmployeeList => new(Employees, "Id", "FullName", EmployeeId);
    public SelectList LeaveTypeList => new(
        Enum.GetValues<LeaveType>().Select(s => new { Id = (int)s, Name = LeaveTypeText(s) }), "Id", "Name", (int)LeaveType);

    public static string LeaveTypeText(LeaveType t) => t switch
    {
        LeaveType.Annual => "سنوية",
        LeaveType.Sick => "مرضية",
        LeaveType.Unpaid => "بدون أجر",
        LeaveType.Emergency => "طارئة",
        LeaveType.Other => "أخرى",
        LeaveType.PublicHoliday => "عطلة رسمية",
        LeaveType.UnexcusedAbsence => "غياب بدون عذر",
        LeaveType.Paid => "مدفوعة",
        LeaveType.WeeklyRest => "راحة أسبوعية",
        _ => t.ToString()
    };

    public static LeaveFormViewModel FromEntity(LeaveRequest l) => new()
    {
        Id = l.Id,
        EmployeeId = l.EmployeeId,
        LeaveType = l.LeaveType,
        StartDate = l.StartDate,
        EndDate = l.EndDate,
        IsPaid = l.IsPaid,
        Reason = l.Reason
    };
}

public class LeaveIndexViewModel
{
    public List<LeaveRequest> Leaves { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public int? EmployeeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public ApprovalStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public int PendingCount => Leaves.Count(l => l.Status == ApprovalStatus.Pending);
    public decimal ApprovedWorkingDays => Leaves.Where(l => l.Status == ApprovalStatus.Approved).Sum(l => l.WorkingDays);

    public SelectList EmployeeList => new(Employees, "Id", "FullName", EmployeeId);
    public SelectList LeaveTypeList => new(
        Enum.GetValues<LeaveType>().Select(s => new { Id = (int)s, Name = LeaveFormViewModel.LeaveTypeText(s) }), "Id", "Name",
        LeaveType.HasValue ? (int)LeaveType.Value : null);
    public SelectList ApprovalList => new(new[]
    {
        new { Id = (int)ApprovalStatus.Pending, Name = "قيد المراجعة" },
        new { Id = (int)ApprovalStatus.Approved, Name = "معتمد" },
        new { Id = (int)ApprovalStatus.Rejected, Name = "مرفوض" }
    }, "Id", "Name", Status.HasValue ? (int)Status.Value : null);
}

public class ConvertAbsenceViewModel
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public int EmployeeId { get; set; }

    [Display(Name = "من تاريخ")]
    [DataType(DataType.Date)]
    public DateTime From { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [Display(Name = "إلى تاريخ")]
    [DataType(DataType.Date)]
    public DateTime To { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "نوع الإجازة مطلوب")]
    [Display(Name = "تحويل إلى")]
    public LeaveType LeaveType { get; set; } = LeaveType.Annual;

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [Display(Name = "الأيام المحددة")]
    public List<DateTime> SelectedDates { get; set; } = new();

    public List<Employee> Employees { get; set; } = new();
    public List<DailyAttendance> AbsenceDays { get; set; } = new();
    public SelectList EmployeeList => new(Employees, "Id", "FullName", EmployeeId);
    public SelectList LeaveTypeList => new(
        new[]
        {
            new { Id = (int)LeaveType.Annual, Name = "إجازة سنوية" },
            new { Id = (int)LeaveType.Sick, Name = "إجازة مرضية" },
            new { Id = (int)LeaveType.Paid, Name = "إجازة مدفوعة" },
            new { Id = (int)LeaveType.Unpaid, Name = "إجازة بدون أجر" },
            new { Id = (int)LeaveType.UnexcusedAbsence, Name = "غياب بدون عذر (بدون خصم إجازة)" }
        }, "Id", "Name", (int)LeaveType);
}
