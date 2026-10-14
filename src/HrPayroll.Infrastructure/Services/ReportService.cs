using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

/// <summary>مرشحات تقرير الحضور اليومي.</summary>
public sealed class AttendanceReportFilter
{
    public DateTime From { get; set; } = DateTime.Today;
    public DateTime To { get; set; } = DateTime.Today;
    public string? Department { get; set; }
    public int? EmployeeId { get; set; }
    public int? ShiftId { get; set; }
    public AttendanceStatus? Status { get; set; }
    public int? ImportBatchId { get; set; }
    public string? Search { get; set; }

    public string PeriodText => From.Date == To.Date
        ? $"يوم {From:yyyy-MM-dd}"
        : $"من {From:yyyy-MM-dd} إلى {To:yyyy-MM-dd}";
}

/// <summary>صف في تقرير الحضور اليومي.</summary>
public sealed class DailyAttendanceReportRow
{
    public int DailyAttendanceId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public DateTime WorkDate { get; set; }
    public string? ShiftName { get; set; }
    public ShiftDetectionSource DetectionSource { get; set; }
    public DateTime ShiftStart { get; set; }
    public DateTime ShiftEnd { get; set; }
    public DateTime? CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public int RawOvertimeMinutes { get; set; }
    public int ApprovedOvertimeMinutes { get; set; }
    public AttendanceStatus Status { get; set; }
    public ClassificationStatus PunchReviewStatus { get; set; }
    public bool HasIncompletePunches { get; set; }
    public bool IsWeeklyRestDay { get; set; }
    public bool IsHoliday { get; set; }
    public string? HolidayName { get; set; }
    public string? LeaveType { get; set; }
    public string? Notes { get; set; }
    public string? AdminNotes { get; set; }
    public decimal WorkedHours => Math.Round(WorkedMinutes / 60m, 2);
    public decimal RawOvertimeHours => Math.Round(RawOvertimeMinutes / 60m, 2);
    public decimal ApprovedOvertimeHours => Math.Round(ApprovedOvertimeMinutes / 60m, 2);
    public bool NeedsReview => PunchReviewStatus == ClassificationStatus.NeedsReview
                               || Status is AttendanceStatus.NeedsReview or AttendanceStatus.MissingCheckIn
                                   or AttendanceStatus.MissingCheckOut;
}

/// <summary>ملخص قسم في تقرير الأقسام.</summary>
public sealed class DepartmentReportRow
{
    public string Department { get; set; } = "غير محدد";
    public int Total { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int Incomplete { get; set; }
    public int OnLeave { get; set; }
    public int RestOrHoliday { get; set; }
    public decimal PresentRate => Total == 0 ? 0m : Math.Round(Present * 100m / Total, 1);
    public List<DailyAttendanceReportRow> Rows { get; set; } = new();
}

/// <summary>صف تقرير الإضافي.</summary>
public sealed class OvertimeReportRow
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public DateTime WorkDate { get; set; }
    public string? ShiftName { get; set; }
    public DateTime ShiftEnd { get; set; }
    public DateTime? CheckOut { get; set; }
    public int RawMinutes { get; set; }
    public int ApprovedMinutes { get; set; }
    public decimal DecimalHours => Math.Round(ApprovedMinutes / 60m, 2);
    public bool IsApproved => ApprovedMinutes > 0;
}

/// <summary>صف تقرير الغياب والتأخير.</summary>
public sealed class AbsenceReportRow
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public DateTime WorkDate { get; set; }
    public AttendanceStatus Status { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public bool HasIncompletePunches { get; set; }
    public string? Notes { get; set; }
}

/// <summary>صف تقرير الرواتب.</summary>
public sealed class PayrollReportRow
{
    public int PayrollItemId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal DailyRate { get; set; }
    public decimal HourlyRate { get; set; }

    /// <summary>سعر الدقيقة (لحساب خصم التأخير) — يُستخدم في المعاينة الفورية على الشاشة.</summary>
    public decimal MinuteRate { get; set; }

    /// <summary>معامل الإضافي المطبَّق على السطر.</summary>
    public decimal OvertimeMultiplier { get; set; } = 1m;

    public int AttendanceDays { get; set; }
    public int PaidRestDays { get; set; }
    public decimal PaidLeaveDays { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public decimal AbsenceDays { get; set; }

    /// <summary>الغياب الأصلي من البصمة (يبقى محفوظًا ولا يتأثر بالتعديل اليدوي).</summary>
    public decimal OriginalAbsenceDays { get; set; }

    /// <summary>أيام الإجازة السنوية المسجَّلة على السطر.</summary>
    public decimal AnnualLeaveDays { get; set; }

    /// <summary>رصيد الإجازة السنوية المتبقي للموظف (لحظي).</summary>
    public decimal AnnualLeaveBalance { get; set; }

    public decimal AnnualLeaveEntitlement { get; set; }
    public decimal AnnualLeaveCarriedOver { get; set; }
    public decimal AnnualLeaveTotal { get; set; }

    /// <summary>ما خُصم فعليًا من الرصيد لهذا السطر (لمنع الخصم المزدوج).</summary>
    public decimal AnnualLeaveBalanceApplied { get; set; }

    public int LateMinutes { get; set; }
    public decimal RawOvertimeHours { get; set; }
    public decimal ApprovedOvertimeHours { get; set; }
    public decimal PaidOvertimeHours { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal Bonuses { get; set; }
    public decimal Allowances { get; set; }
    public decimal AdditionalDaysCount { get; set; }
    public decimal AdditionalDaysAmount { get; set; }
    public decimal? ExtraDaysRate { get; set; }
    public decimal LateDeduction { get; set; }
    public decimal AbsenceDeduction { get; set; }
    public decimal UnpaidLeaveDeduction { get; set; }
    public decimal LoansTotal { get; set; }
    public decimal DeductionsTotal { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSalary { get; set; }
    public PayrollStatus Status { get; set; }
    public string? CalculationDetails { get; set; }

    /// <summary>هل عُدِّل السطر يدويًا؟</summary>
    public bool IsManuallyAdjusted { get; set; }
    public DateTime? LastEditedAt { get; set; }
    public string? LastEditedBy { get; set; }

    /// <summary>هل السطر بحاجة إلى مراجعة بشرية (بصمات ناقصة/تصنيف غير مؤكد/رصيد غير كافٍ)؟</summary>
    public bool NeedsReview { get; set; }

    /// <summary>حالة المراجعة النصية المعروضة في الكشف.</summary>
    public string ReviewStatusText => NeedsReview
        ? "يحتاج مراجعة"
        : IsManuallyAdjusted ? "مُعدَّل يدويًا" : "محسوب آليًا";
}

public sealed class PayslipData
{
    public int PayrollRunId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public PayrollStatus RunStatus { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public PayrollReportRow Item { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

// ======================= تقرير الأقسام والمسميات الوظيفية =======================

/// <summary>ملخص موظف داخل مجموعة مسمى وظيفي في تقرير الأقسام.</summary>
public sealed class EmployeeAttendanceSummaryRow
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int AttendanceRecords { get; set; }
    public int PresentRecords { get; set; }
    public int AbsentRecords { get; set; }
    public int LateMinutes { get; set; }
    public int ApprovedOvertimeMinutes { get; set; }
    public decimal ApprovedOvertimeHours => Math.Round(ApprovedOvertimeMinutes / 60m, 2);
    public bool NeedsReview => AbsentRecords > 0;
}

/// <summary>مجموعة مسمى وظيفي داخل قسم.</summary>
public sealed class JobTitleGroupRow
{
    public string JobTitle { get; set; } = "غير محدد";
    public int AttendanceRecords { get; set; }
    public int PresentEmployees { get; set; }
    public int AbsentEmployees { get; set; }
    public int LateEmployees { get; set; }
    public int OnLeaveEmployees { get; set; }
    public List<EmployeeAttendanceSummaryRow> Employees { get; set; } = new();
    public int EmployeeCount => Employees.Count;
}

/// <summary>قسم في تقرير الأقسام، مقسّم حسب المسمى الوظيفي مع أسماء الموظفين.</summary>
public sealed class DepartmentJobTitleReportRow
{
    public string Department { get; set; } = "غير محدد";
    public int AttendanceRecords { get; set; }
    public int TotalEmployees { get; set; }
    public int PresentEmployees { get; set; }
    public int AbsentEmployees { get; set; }
    public int LateEmployees { get; set; }
    public int OnLeaveEmployees { get; set; }
    public List<JobTitleGroupRow> JobTitles { get; set; } = new();
    public decimal PresentRate => TotalEmployees == 0 ? 0m : Math.Round(PresentEmployees * 100m / TotalEmployees, 1);
}

/// <summary>رصيد الإجازة السنوية لموظف.</summary>
public sealed class LeaveBalanceRow
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
    public decimal Entitlement { get; set; }
    public decimal CarriedOver { get; set; }
    public decimal Balance { get; set; }
    public decimal Total => Entitlement + CarriedOver;
    public decimal Used => Math.Max(0m, Total - Balance);
}

/// <summary>طلب إجازة في تقرير الإجازات.</summary>
public sealed class LeaveReportRow
{
    public int LeaveRequestId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public LeaveType LeaveType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DaysCount { get; set; }
    public decimal WorkingDays { get; set; }
    public ApprovalStatus Status { get; set; }
    public bool IsPaid { get; set; }
    public bool CreatedFromAbsence { get; set; }
    public string? Reason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public decimal Entitlement { get; set; }
    public decimal CarriedOver { get; set; }
    public decimal Balance { get; set; }
    public decimal Total => Entitlement + CarriedOver;
    public decimal Used => Math.Max(0m, Total - Balance);
}

/// <summary>استعلامات التقارير: كل الأرقام تُقرأ من قاعدة البيانات ولا شيء منها ثابت.</summary>
public class ReportService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public ReportService(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    /// <summary>تقرير الحضور اليومي بالمرشحات المطلوبة.</summary>
    public async Task<List<DailyAttendanceReportRow>> GetDailyAttendanceAsync(
        AttendanceReportFilter filter, CancellationToken ct = default)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        if (to < from) (from, to) = (to, from);

        var q = _db.DailyAttendances.AsNoTracking()
            .Include(d => d.Employee)
            .Include(d => d.Shift)
            .Include(d => d.Holiday)
            .Include(d => d.LeaveRequest)
            .Where(d => d.WorkDate >= from && d.WorkDate <= to);

        if (filter.EmployeeId.HasValue) q = q.Where(d => d.EmployeeId == filter.EmployeeId.Value);
        if (filter.ShiftId.HasValue) q = q.Where(d => d.ShiftId == filter.ShiftId.Value);
        if (filter.Status.HasValue) q = q.Where(d => d.Status == filter.Status.Value);
        if (!string.IsNullOrWhiteSpace(filter.Department))
            q = q.Where(d => d.Employee!.Department == filter.Department);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(d => d.Employee!.FullName.Contains(s)
                             || d.Employee!.EmployeeCode.Contains(s)
                             || (d.Employee!.DeviceEmployeeCode != null && d.Employee!.DeviceEmployeeCode.Contains(s)));
        }

        var rows = await q
            .OrderBy(d => d.WorkDate).ThenBy(d => d.Employee!.EmployeeCode)
            .Select(d => new DailyAttendanceReportRow
            {
                DailyAttendanceId = d.Id,
                EmployeeId = d.EmployeeId,
                EmployeeCode = d.Employee!.EmployeeCode,
                EmployeeName = d.Employee!.FullName,
                Department = d.Employee!.Department,
                JobTitle = d.Employee!.JobTitle,
                WorkDate = d.WorkDate,
                ShiftName = d.Shift != null ? d.Shift.ShiftName : null,
                DetectionSource = d.DetectionSource,
                ShiftStart = d.ShiftStart,
                ShiftEnd = d.ShiftEnd,
                CheckIn = d.CheckInTime,
                CheckOut = d.CheckOutTime,
                LateMinutes = d.LateMinutes,
                EarlyLeaveMinutes = d.EarlyLeaveMinutes,
                RawOvertimeMinutes = d.RawOvertimeMinutes,
                ApprovedOvertimeMinutes = d.ApprovedOvertimeMinutes,
                Status = d.Status,
                PunchReviewStatus = d.PunchReviewStatus,
                HasIncompletePunches = d.HasIncompletePunches,
                IsWeeklyRestDay = d.IsWeeklyRestDay,
                IsHoliday = d.IsHoliday,
                HolidayName = d.Holiday != null ? d.Holiday.Name : null,
                LeaveType = d.LeaveRequest != null ? d.LeaveRequest.LeaveType.ToString() : null,
                Notes = d.Notes,
                AdminNotes = d.AdminNotes
            })
            .ToListAsync(ct);

        foreach (var r in rows)
            r.WorkedMinutes = r.CheckIn.HasValue && r.CheckOut.HasValue
                ? Math.Max(0, (int)Math.Round((r.CheckOut.Value - r.CheckIn.Value).TotalMinutes))
                : 0;

        return rows;
    }

    /// <summary>تقرير الأقسام: من حضر ومن غاب ومن تأخر ومن لديه بصمات ناقصة.</summary>
    public async Task<List<DepartmentReportRow>> GetDepartmentReportAsync(
        AttendanceReportFilter filter, CancellationToken ct = default)
    {
        var rows = await GetDailyAttendanceAsync(filter, ct);
        return rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Department) ? "غير محدد" : r.Department!)
            .Select(g => new DepartmentReportRow
            {
                Department = g.Key,
                Total = g.Select(x => x.EmployeeId).Distinct().Count(),
                Present = g.Count(x => x.Status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.EarlyDeparture),
                Absent = g.Count(x => x.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence),
                Late = g.Count(x => x.LateMinutes > 0),
                Incomplete = g.Count(x => x.HasIncompletePunches),
                OnLeave = g.Count(x => x.Status is AttendanceStatus.Leave or AttendanceStatus.PaidLeave or AttendanceStatus.UnpaidLeave),
                RestOrHoliday = g.Count(x => x.Status is AttendanceStatus.RestDay or AttendanceStatus.Holiday),
                Rows = g.OrderBy(x => x.EmployeeCode).ToList()
            })
            .OrderByDescending(r => r.Total)
            .ToList();
    }

    /// <summary>تقرير الإضافي: الانصراف، الإضافي الخام، المعتمد، وسعر الساعة والقيمة.</summary>
    public async Task<List<OvertimeReportRow>> GetOvertimeAsync(
        AttendanceReportFilter filter, bool onlyWithOvertime = true, CancellationToken ct = default)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        if (to < from) (from, to) = (to, from);

        var rows = await _db.DailyAttendances.AsNoTracking()
            .Include(d => d.Employee).Include(d => d.Shift)
            .Where(d => d.WorkDate >= from && d.WorkDate <= to)
            .Where(d => d.RawOvertimeMinutes > 0 || d.ApprovedOvertimeMinutes > 0)
            .Where(d => !filter.EmployeeId.HasValue || d.EmployeeId == filter.EmployeeId.Value)
            .Where(d => string.IsNullOrEmpty(filter.Department) || d.Employee!.Department == filter.Department)
            .Where(d => !filter.ShiftId.HasValue || d.ShiftId == filter.ShiftId.Value)
            .OrderBy(d => d.WorkDate).ThenBy(d => d.Employee!.EmployeeCode)
            .Select(d => new OvertimeReportRow
            {
                EmployeeId = d.EmployeeId,
                EmployeeCode = d.Employee!.EmployeeCode,
                EmployeeName = d.Employee!.FullName,
                Department = d.Employee!.Department,
                WorkDate = d.WorkDate,
                ShiftName = d.Shift != null ? d.Shift.ShiftName : null,
                ShiftEnd = d.ShiftEnd,
                CheckOut = d.CheckOutTime,
                RawMinutes = d.RawOvertimeMinutes,
                ApprovedMinutes = d.ApprovedOvertimeMinutes
            })
            .ToListAsync(ct);

        if (onlyWithOvertime) rows = rows.Where(r => r.RawMinutes > 0 || r.ApprovedMinutes > 0).ToList();
        return rows;
    }

    /// <summary>تقرير الغياب والتأخير والانصراف المبكر والبصمات الناقصة.</summary>
    public async Task<List<AbsenceReportRow>> GetAbsenceAndLatenessAsync(
        AttendanceReportFilter filter, CancellationToken ct = default)
    {
        var rows = await GetDailyAttendanceAsync(filter, ct);
        return rows
            .Where(r => r.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence
                        or AttendanceStatus.MissingCheckIn or AttendanceStatus.MissingCheckOut
                        or AttendanceStatus.EarlyDeparture
                        || r.LateMinutes > 0 || r.EarlyLeaveMinutes > 0 || r.HasIncompletePunches)
            .Select(r => new AbsenceReportRow
            {
                EmployeeId = r.EmployeeId,
                EmployeeCode = r.EmployeeCode,
                EmployeeName = r.EmployeeName,
                Department = r.Department,
                WorkDate = r.WorkDate,
                Status = r.Status,
                LateMinutes = r.LateMinutes,
                EarlyLeaveMinutes = r.EarlyLeaveMinutes,
                HasIncompletePunches = r.HasIncompletePunches,
                Notes = r.Notes
            })
            .OrderBy(r => r.Department).ThenBy(r => r.EmployeeCode).ThenBy(r => r.WorkDate)
            .ToList();
    }

    /// <summary>صفوف تقرير الرواتب من كشف محسوب فعليًا.</summary>
    public async Task<List<PayrollReportRow>> GetPayrollRowsAsync(
        int payrollRunId, string? department = null, int? employeeId = null, PayrollStatus? status = null,
        CancellationToken ct = default)
    {
        var q = _db.PayrollItems.AsNoTracking().Include(i => i.Employee).Where(i => i.PayrollRunId == payrollRunId);
        if (!string.IsNullOrWhiteSpace(department)) q = q.Where(i => i.DepartmentName == department);
        if (employeeId.HasValue) q = q.Where(i => i.EmployeeId == employeeId.Value);
        if (status.HasValue) q = q.Where(i => i.Status == status.Value);

        var rows = await q.OrderBy(i => i.EmployeeCode).Select(i => new PayrollReportRow
        {
            PayrollItemId = i.Id,
            EmployeeId = i.EmployeeId,
            EmployeeCode = i.EmployeeCode,
            EmployeeName = i.EmployeeName,
            Department = i.DepartmentName,
            JobTitle = i.JobTitleName,
            BasicSalary = i.BasicSalary,
            DailyRate = i.DailyRate,
            HourlyRate = i.HourlyRate,
            MinuteRate = i.MinuteRate,
            OvertimeMultiplier = i.OvertimeMultiplier,
            AttendanceDays = i.AttendanceDays,
            PaidRestDays = i.PaidRestDays,
            PaidLeaveDays = i.PaidLeaveDays,
            UnpaidLeaveDays = i.UnpaidLeaveDays,
            AbsenceDays = i.AbsenceDays,
            OriginalAbsenceDays = i.OriginalAbsenceDays,
            AnnualLeaveDays = i.AnnualLeaveDays,
            AnnualLeaveBalanceApplied = i.AnnualLeaveBalanceApplied,
            AnnualLeaveBalance = i.Employee != null ? i.Employee.AnnualLeaveBalance : 0m,
            AnnualLeaveEntitlement = i.Employee != null ? i.Employee.AnnualLeaveEntitlement : 0m,
            AnnualLeaveCarriedOver = i.Employee != null ? i.Employee.AnnualLeaveCarriedOver : 0m,
            AnnualLeaveTotal = i.Employee != null
                ? i.Employee.AnnualLeaveEntitlement + i.Employee.AnnualLeaveCarriedOver
                : 0m,
            LateMinutes = i.LateMinutes,
            RawOvertimeHours = i.RawOvertimeHours,
            ApprovedOvertimeHours = i.ApprovedOvertimeHours,
            PaidOvertimeHours = i.PaidOvertimeHours,
            OvertimeAmount = i.OvertimeAmount,
            Bonuses = i.Bonuses,
            Allowances = i.Allowances,
            AdditionalDaysCount = i.AdditionalDaysCount,
            AdditionalDaysAmount = i.AdditionalDaysAmount,
            ExtraDaysRate = i.AdditionalDaysRate,
            LateDeduction = i.LateDeduction,
            AbsenceDeduction = i.AbsenceDeduction,
            UnpaidLeaveDeduction = i.UnpaidLeaveDeduction,
            LoansTotal = i.LoansTotal,
            DeductionsTotal = i.DeductionsTotal,
            OtherDeductions = i.OtherDeductions,
            GrossEarnings = i.GrossEarnings,
            TotalDeductions = i.TotalDeductions,
            NetSalary = i.NetSalary,
            Status = i.Status,
            CalculationDetails = i.CalculationDetails,
            IsManuallyAdjusted = i.IsManuallyAdjusted,
            LastEditedAt = i.LastEditedAt,
            LastEditedBy = i.LastEditedBy
        }).ToListAsync(ct);

        await MarkNeedsReviewAsync(payrollRunId, rows, ct);
        return rows;
    }

    /// <summary>
    /// يحدّد الأسطر التي تحتاج مراجعة بشرية: أيام ببصمة ناقصة/تصنيف غير مؤكد داخل فترة الكشف،
    /// أو غياب أصلي لم يُعالج ولم يُحوَّل إلى إجازة، أو رصيد إجازة سنوية سالب.
    /// </summary>
    private async Task MarkNeedsReviewAsync(int payrollRunId, List<PayrollReportRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        var run = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Id == payrollRunId)
            .Select(r => new { r.Year, r.Month })
            .FirstOrDefaultAsync(ct);
        if (run is null) return;

        var first = new DateTime(run.Year, run.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var flagged = await _db.DailyAttendances.AsNoTracking()
            .Where(d => d.WorkDate >= first && d.WorkDate <= last
                        && (d.HasIncompletePunches
                            || d.PunchReviewStatus == ClassificationStatus.NeedsReview
                            || d.Status == AttendanceStatus.NeedsReview))
            .Select(d => d.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        var flaggedSet = flagged.ToHashSet();

        foreach (var r in rows)
        {
            r.NeedsReview = flaggedSet.Contains(r.EmployeeId)
                            || (r.OriginalAbsenceDays > 0 && r.AbsenceDays <= 0 && r.AnnualLeaveDays <= 0)
                            || r.AnnualLeaveBalance < 0;
        }
    }

    /// <summary>بيانات كشف راتب موظف واحد.</summary>
    public async Task<PayslipData?> GetPayslipAsync(int payrollItemId, CancellationToken ct = default)
    {
        var item = await _db.PayrollItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == payrollItemId, ct);
        if (item is null) return null;

        var run = await _db.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == item.PayrollRunId, ct);
        var all = await GetPayrollRowsAsync(run.Id, ct: ct);
        var row = all.First(r => r.PayrollItemId == payrollItemId);

        var settings = await _settings.GetAllAsync(ct);
        var slip = new PayslipData
        {
            PayrollRunId = run.Id,
            Year = run.Year,
            Month = run.Month,
            RunStatus = run.Status,
            CompanyName = settings.TryGetValue(SettingKeys.CompanyName, out var cn) ? cn : "HR & Payroll",
            Currency = settings.TryGetValue(SettingKeys.Currency, out var cur) ? cur : "EGP",
            Item = row
        };

        if (item.ApprovedOvertimeHours > 0)
            slip.Notes.Add($"الإضافي بسعر الساعة {row.HourlyRate:0.##} (بدون أي مضاعف).");
        if (item.PaidRestDays > 0)
            slip.Notes.Add($"{item.PaidRestDays} يوم راحة مدفوع مشمول أصلًا في الراتب الشهري ولم يُضَف مرة أخرى.");
        if (item.AdditionalDaysCount > 0)
            slip.Notes.Add($"{item.AdditionalDaysCount} يوم عمل إضافي مستحق خارج فترة الراتب الأساسية.");
        return slip;
    }

    // ======================= تقرير الأقسام والمسميات الوظيفية =======================

    /// <summary>
    /// تقرير الأقسام للتجميع حسب القسم ثم المسمى الوظيفي مع أسماء الموظفين الفعلية.
    /// كل الأعداد مشتقة من سجلات الحضور المطابقة للمرشحات، ولا شيء منها ثابت في الكود.
    /// </summary>
    public async Task<List<DepartmentJobTitleReportRow>> GetDepartmentJobTitleReportAsync(
        AttendanceReportFilter filter, string? jobTitle = null, CancellationToken ct = default)
    {
        var rows = await GetDailyAttendanceAsync(filter, ct);
        if (!string.IsNullOrWhiteSpace(jobTitle))
        {
            var jt = jobTitle.Trim();
            rows = rows.Where(r => string.Equals(r.JobTitle?.Trim(), jt, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Department) ? "غير محدد" : r.Department!.Trim())
            .Select(dept => new DepartmentJobTitleReportRow
            {
                Department = dept.Key,
                AttendanceRecords = dept.Count(),
                TotalEmployees = dept.Select(r => r.EmployeeId).Distinct().Count(),
                PresentEmployees = dept
                    .Where(r => r.Status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.EarlyDeparture)
                    .Select(r => r.EmployeeId).Distinct().Count(),
                AbsentEmployees = dept
                    .Where(r => r.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence)
                    .Select(r => r.EmployeeId).Distinct().Count(),
                LateEmployees = dept.Where(r => r.LateMinutes > 0).Select(r => r.EmployeeId).Distinct().Count(),
                OnLeaveEmployees = dept
                    .Where(r => r.Status is AttendanceStatus.Leave or AttendanceStatus.PaidLeave or AttendanceStatus.UnpaidLeave)
                    .Select(r => r.EmployeeId).Distinct().Count(),
                JobTitles = dept
                    .GroupBy(r => string.IsNullOrWhiteSpace(r.JobTitle) ? "غير محدد" : r.JobTitle!.Trim())
                    .Select(jg => new JobTitleGroupRow
                    {
                        JobTitle = jg.Key,
                        AttendanceRecords = jg.Count(),
                        PresentEmployees = jg
                            .Where(r => r.Status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.EarlyDeparture)
                            .Select(r => r.EmployeeId).Distinct().Count(),
                        AbsentEmployees = jg
                            .Where(r => r.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence)
                            .Select(r => r.EmployeeId).Distinct().Count(),
                        LateEmployees = jg.Where(r => r.LateMinutes > 0).Select(r => r.EmployeeId).Distinct().Count(),
                        OnLeaveEmployees = jg
                            .Where(r => r.Status is AttendanceStatus.Leave or AttendanceStatus.PaidLeave or AttendanceStatus.UnpaidLeave)
                            .Select(r => r.EmployeeId).Distinct().Count(),
                        Employees = jg
                            .GroupBy(r => new { r.EmployeeId, r.EmployeeCode, r.EmployeeName })
                            .Select(eg => new EmployeeAttendanceSummaryRow
                            {
                                EmployeeId = eg.Key.EmployeeId,
                                EmployeeCode = eg.Key.EmployeeCode,
                                EmployeeName = eg.Key.EmployeeName,
                                AttendanceRecords = eg.Count(),
                                PresentRecords = eg.Count(x => x.Status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.EarlyDeparture),
                                AbsentRecords = eg.Count(x => x.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence),
                                LateMinutes = eg.Sum(x => x.LateMinutes),
                                ApprovedOvertimeMinutes = eg.Sum(x => x.ApprovedOvertimeMinutes)
                            })
                            .OrderBy(e => e.EmployeeCode)
                            .ToList()
                    })
                    .OrderByDescending(j => j.Employees.Count).ThenBy(j => j.JobTitle)
                    .ToList()
            })
            .OrderByDescending(d => d.TotalEmployees).ThenBy(d => d.Department)
            .ToList();
    }

    /// <summary>أرصدة الإجازة السنوية للموظفين (تُستخدم في تقرير الإجازات والأرصدة).</summary>
    public async Task<List<LeaveBalanceRow>> GetLeaveBalancesAsync(
        AttendanceReportFilter filter, CancellationToken ct = default)
    {
        var q = _db.Employees.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Department))
            q = q.Where(e => e.Department == filter.Department);
        if (filter.EmployeeId.HasValue) q = q.Where(e => e.Id == filter.EmployeeId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(e => e.FullName.Contains(s) || e.EmployeeCode.Contains(s));
        }

        return await q
            .Select(e => new LeaveBalanceRow
            {
                EmployeeId = e.Id,
                EmployeeCode = e.EmployeeCode,
                EmployeeName = e.FullName,
                Department = e.Department,
                JobTitle = e.JobTitle,
                EmploymentStatus = e.EmploymentStatus,
                Entitlement = e.AnnualLeaveEntitlement,
                CarriedOver = e.AnnualLeaveCarriedOver,
                Balance = e.AnnualLeaveBalance
            })
            .OrderBy(r => r.Department).ThenBy(r => r.EmployeeCode)
            .ToListAsync(ct);
    }

    /// <summary>طلبات الإجازات داخل الفترة مع حالة كل طلب ورصيد الموظف.</summary>
    public async Task<List<LeaveReportRow>> GetLeavesAsync(
        AttendanceReportFilter filter, LeaveType? leaveType = null, ApprovalStatus? status = null,
        CancellationToken ct = default)
    {
        var from = filter.From.Date;
        var to = filter.To.Date;
        if (to < from) (from, to) = (to, from);

        var q = _db.LeaveRequests.AsNoTracking()
            .Include(l => l.Employee)
            .Where(l => l.StartDate <= to && l.EndDate >= from);

        if (!string.IsNullOrWhiteSpace(filter.Department))
            q = q.Where(l => l.Employee!.Department == filter.Department);
        if (filter.EmployeeId.HasValue) q = q.Where(l => l.EmployeeId == filter.EmployeeId.Value);
        if (leaveType.HasValue) q = q.Where(l => l.LeaveType == leaveType.Value);
        if (status.HasValue) q = q.Where(l => l.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(l => l.Employee!.FullName.Contains(s) || l.Employee!.EmployeeCode.Contains(s));
        }

        return await q
            .OrderBy(l => l.Employee!.EmployeeCode).ThenBy(l => l.StartDate)
            .Select(l => new LeaveReportRow
            {
                LeaveRequestId = l.Id,
                EmployeeId = l.EmployeeId,
                EmployeeCode = l.Employee!.EmployeeCode,
                EmployeeName = l.Employee!.FullName,
                Department = l.Employee!.Department,
                JobTitle = l.Employee!.JobTitle,
                LeaveType = l.LeaveType,
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                DaysCount = l.DaysCount,
                WorkingDays = l.WorkingDays,
                Status = l.Status,
                IsPaid = l.IsPaid,
                CreatedFromAbsence = l.CreatedFromAbsence,
                Reason = l.Reason,
                ApprovedBy = l.ApprovedBy,
                ApprovedAt = l.ApprovedAt,
                Entitlement = l.Employee!.AnnualLeaveEntitlement,
                CarriedOver = l.Employee!.AnnualLeaveCarriedOver,
                Balance = l.Employee!.AnnualLeaveBalance
            })
            .ToListAsync(ct);
    }
}
