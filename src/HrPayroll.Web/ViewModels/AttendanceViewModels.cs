using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HrPayroll.Web.ViewModels;

public class AttendanceIndexViewModel
{
    public AttendanceReportFilter Filter { get; set; } = new();
    public List<DailyAttendanceReportRow> Rows { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public List<ImportBatch> ImportBatches { get; set; } = new();

    public int PresentCount => Rows.Count(r => r.CheckIn != null);
    public int AbsentCount => Rows.Count(r => r.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence);
    public int LateCount => Rows.Count(r => r.LateMinutes > 0);
    public int ReviewCount => Rows.Count(r => r.NeedsReview);
    public int TotalLateMinutes => Rows.Sum(r => r.LateMinutes);
    public decimal TotalRawOvertimeHours => Math.Round(Rows.Sum(r => r.RawOvertimeMinutes) / 60m, 2);

    public SelectList StatusList => new(
        Enum.GetValues<AttendanceStatus>().Select(s => new { Id = (int)s, Name = s.ToString() }), "Id", "Name",
        Filter.Status.HasValue ? (int)Filter.Status.Value : null);
}

public class AttendanceCorrectionViewModel
{
    public DailyAttendance Daily { get; set; } = new();
    public List<AttendanceCorrection> History { get; set; } = new();

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "سبب التصحيح مطلوب")]
    [System.ComponentModel.DataAnnotations.Display(Name = "سبب التصحيح")]
    public string Reason { get; set; } = string.Empty;

    public DateTime? CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }
    public AttendanceStatus? Status { get; set; }
    public int? ApprovedOvertimeMinutes { get; set; }
    public bool Approve { get; set; } = true;

    public SelectList StatusList => new(
        Enum.GetValues<AttendanceStatus>().Select(s => new { Id = (int)s, Name = s.ToString() }), "Id", "Name",
        Status.HasValue ? (int)Status.Value : null);
}

public class ExceptionsViewModel
{
    public AttendanceReportFilter Filter { get; set; } = new();
    public List<DailyAttendanceReportRow> Rows { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
}
