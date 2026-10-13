using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrPayroll.Infrastructure.Services;

public sealed class PayrollGenerationResult
{
    public int PayrollRunId { get; set; }
    public int ItemCount { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public int Version { get; set; }
    public List<string> Warnings { get; } = new();
}

/// <summary>جاهزية الفترة للاحتساب: عدد الأيام الناقصة وحالات المراجعة.</summary>
public sealed class PayrollReadiness
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int ActiveEmployees { get; set; }
    public int DaysWithoutAttendance { get; set; }
    public int NeedsReviewCount { get; set; }
    public int IncompletePunchCount { get; set; }
    public int PendingOvertimeDays { get; set; }
    public int PendingLeaves { get; set; }
    public int PendingAdjustments { get; set; }
    public bool IsReady => DaysWithoutAttendance == 0 && PendingLeaves == 0 && PendingAdjustments == 0;
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// توليد وإدارة كشوف الرواتب الشهرية.
/// يعتمد على الحضور النهائي + الإضافي المعتمد + الإجازات + الفرائض والعطلات + التسويات.
/// كل مكوّن يُحسب مرة واحدة فقط ويُربط بالكشف لمنع الازدواج بين الفترات.
/// </summary>
public class PayrollService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly ILogger<PayrollService> _logger;

    public PayrollService(AppDbContext db, SettingsService settings, AuditService audit, ILogger<PayrollService> logger)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
        _logger = logger;
    }

    private static (DateTime First, DateTime Last) Period(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>الراتب الشهري المستخدم للموظف في الفترة (حسب تاريخ التعيين/إنهاء الخدمة).</summary>
    private static bool EligibleForPeriod(Employee e, DateTime first, DateTime last)
    {
        if (e.HireDate.Date > last) return false;
        if (e.TerminationDate.HasValue && e.TerminationDate.Value.Date < first) return false;
        return e.EmploymentStatus != EmploymentStatus.Terminated || e.TerminationDate.HasValue;
    }

    /// <summary>يفحص جاهزية الفترة قبل الاحتساب.</summary>
    public async Task<PayrollReadiness> GetReadinessAsync(int year, int month, CancellationToken ct = default)
    {
        var (first, last) = Period(year, month);
        var readiness = new PayrollReadiness { Year = year, Month = month };

        var employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct);
        readiness.ActiveEmployees = employees.Count;

        var rows = await _db.DailyAttendances.AsNoTracking()
            .Where(d => d.WorkDate >= first && d.WorkDate <= last)
            .Select(d => new { d.Status, d.PunchReviewStatus, d.HasIncompletePunches, d.EmployeeId })
            .ToListAsync(ct);

        readiness.NeedsReviewCount = rows.Count(r => r.PunchReviewStatus == ClassificationStatus.NeedsReview
                                                     || r.Status == AttendanceStatus.NeedsReview);
        readiness.IncompletePunchCount = rows.Count(r => r.HasIncompletePunches);
        readiness.PendingOvertimeDays = rows.Count(r => r.Status == AttendanceStatus.NeedsReview);

        var attended = rows.Where(r => r.Status is AttendanceStatus.Present or AttendanceStatus.Late
                                        or AttendanceStatus.EarlyDeparture or AttendanceStatus.NeedsReview
                                        or AttendanceStatus.MissingCheckIn or AttendanceStatus.MissingCheckOut)
            .Select(r => r.EmployeeId).Distinct().Count();

        readiness.DaysWithoutAttendance = attended == 0 ? 1 : 0;
        if (rows.Count == 0)
            readiness.Warnings.Add("لا توجد أي بيانات حضور لهذه الفترة. استورد ملفات البصمة واعالج الحضور أولًا.");
        if (readiness.IncompletePunchCount > 0)
            readiness.Warnings.Add($"يوجد {readiness.IncompletePunchCount} يوم ببصمة ناقصة (دخول أو خروج مفقود) — تحتاج مراجعة قبل الاحتساب.");
        if (readiness.NeedsReviewCount > 0)
            readiness.Warnings.Add($"يوجد {readiness.NeedsReviewCount} يوم بحالة \"يحتاج مراجعة\".");

        readiness.PendingLeaves = await _db.LeaveRequests.CountAsync(
            l => l.Status == ApprovalStatus.Pending && l.StartDate <= last && l.EndDate >= first, ct);
        readiness.PendingAdjustments = await _db.PayrollAdjustments.CountAsync(
            a => a.Status == ApprovalStatus.Pending && a.Year == year && a.Month == month, ct);

        if (readiness.PendingLeaves > 0)
            readiness.Warnings.Add($"يوجد {readiness.PendingLeaves} طلب إجازة قيد الاعتماد داخل الفترة.");
        if (readiness.PendingAdjustments > 0)
            readiness.Warnings.Add($"يوجد {readiness.PendingAdjustments} تسوية مالية قيد الاعتماد داخل الفترة.");

        return readiness;
    }

    /// <summary>يولد/يعيد احتساب كشف رواتب الشهر داخل معاملة واحدة.</summary>
    public async Task<PayrollGenerationResult> GenerateAsync(
        int year, int month, string? user = null, string? userId = null, CancellationToken ct = default)
    {
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        var policy = await _settings.GetPolicyAsync(ct);
        var requireApproval = await _settings.GetBoolAsync(SettingKeys.RequireOvertimeApproval, true, ct);
        var (first, last) = Period(year, month);
        var result = new PayrollGenerationResult();

        var existing = await _db.PayrollRuns.Where(r => r.Year == year && r.Month == month).ToListAsync(ct);

        var locked = existing.FirstOrDefault(r => r.Status is PayrollStatus.Approved or PayrollStatus.Locked);
        if (locked is not null)
            throw new InvalidOperationException(
                $"كشف رواتب {year}-{month:00} مقفل (الحالة: {locked.Status}). " +
                "لا يمكن إعادة الاحتساب تلقائيًا. استخدم \"إعادة الفتح\" مع تسجيل السبب أولًا.");

        // حماية: لا يُحتسب كشف لفترة لم تُعالَج فيها أي بيانات حضور، لأن ذلك كان
        // يُنتج رواتب كاملة بدون أي خصم رغم غياب البيانات (نتيجة صامتة غير صحيحة).
        // يمكن للأدمن السماح بذلك صراحةً من الإعدادات عند الحاجة.
        if (!await _settings.GetBoolAsync(SettingKeys.AllowPayrollWithoutAttendance, false, ct))
        {
            var hasAttendance = await _db.DailyAttendances.AnyAsync(d => d.WorkDate >= first && d.WorkDate <= last, ct);
            if (!hasAttendance)
                throw new InvalidOperationException(
                    $"لا توجد أي بيانات حضور معالجة للفترة {year}-{month:00}. " +
                    "استورد ملفات البصمة ثم عالج الحضور لهذه الفترة قبل احتساب الكشف.");
        }

        var previousVersion = existing.Count == 0 ? 0 : existing.Max(r => r.Version);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // أرشفة الكشوف السابقة وفصل ارتباطاتها حتى لا تُخصم/تُضاف المكوّنات مرتين
        foreach (var oldRun in existing.Where(r => r.Status != PayrollStatus.Cancelled))
        {
            var oldItemIds = await _db.PayrollItems.Where(i => i.PayrollRunId == oldRun.Id).Select(i => i.Id).ToListAsync(ct);
            await UnlinkAsync(oldItemIds, ct);
            oldRun.Status = PayrollStatus.Cancelled;
            oldRun.UpdatedAt = DateTime.UtcNow;
            oldRun.UpdatedBy = user;
        }

        var run = new PayrollRun
        {
            Year = year,
            Month = month,
            Status = PayrollStatus.Computed,
            Version = previousVersion + 1,
            ComputedAt = DateTime.UtcNow,
            ComputedBy = user,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var allEmployees = await _db.Employees.ToListAsync(ct);
        var employees = allEmployees.Where(e => EligibleForPeriod(e, first, last)).ToList();
        var employeeIds = employees.Select(e => e.Id).ToList();

        // ===== أيام الفترة =====
        var daily = await _db.DailyAttendances.AsNoTracking()
            .Where(d => employeeIds.Contains(d.EmployeeId) && d.WorkDate >= first && d.WorkDate <= last)
            .ToListAsync(ct);

        // ===== الإضافي المعتمد =====
        var approvedApprovals = await _db.OvertimeApprovals.AsNoTracking()
            .Where(o => employeeIds.Contains(o.EmployeeId) && o.Status == ApprovalStatus.Approved
                        && o.PeriodStart >= first && o.PeriodEnd <= last && o.PayrollItemId == null)
            .Select(o => new { o.EmployeeId, o.ApprovedHours })
            .ToListAsync(ct);
        var approvalHoursByEmp = approvedApprovals
            .GroupBy(o => o.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.ApprovedHours));

        // ===== السلف والخصومات والتسويات والإجازات المعتمدة =====
        var loans = await _db.Loans
            .Where(l => employeeIds.Contains(l.EmployeeId) && l.DeductionYear == year && l.DeductionMonth == month
                        && l.Status == ApprovalStatus.Approved && l.PayrollItemId == null)
            .ToListAsync(ct);

        var deductions = await _db.Deductions
            .Where(d => employeeIds.Contains(d.EmployeeId) && d.Year == year && d.Month == month
                        && d.Status == ApprovalStatus.Approved && d.PayrollItemId == null)
            .ToListAsync(ct);

        var adjustments = await _db.PayrollAdjustments
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.Year == year && a.Month == month
                        && a.Status == ApprovalStatus.Approved && a.PayrollItemId == null)
            .ToListAsync(ct);

        // الإجازات المعتمدة التي تغطي أيامًا داخل الفترة ولم تُستهلك في كشف سابق
        var leaves = await _db.LeaveRequests
            .Where(l => employeeIds.Contains(l.EmployeeId) && l.Status == ApprovalStatus.Approved
                        && l.StartDate <= last && l.EndDate >= first && l.PayrollItemId == null)
            .ToListAsync(ct);

        var lockedAt = DateTime.UtcNow;

        foreach (var emp in employees)
        {
            var empDays = daily.Where(d => d.EmployeeId == emp.Id).ToList();
            var empLeaves = leaves.Where(l => l.EmployeeId == emp.Id).ToList();

            // أيام الحضور الفعلي
            var attendanceDays = empDays.Count(d => d.CheckInTime.HasValue);

            // أيام الراحة والعطلات والغياب
            var paidRestDays = empDays.Count(d => d.Status == AttendanceStatus.RestDay && !d.CheckInTime.HasValue);
            var paidHolidayDays = empDays.Count(d => d.Status == AttendanceStatus.Holiday && !d.CheckInTime.HasValue);

            // تصنيف الإجازات: نعتمد على أيام الحضور المُصنَّفة، مع الرجوع لطلبات الإجازة عند عدم وجود أيام
            var paidLeaveDays = empDays.Count(d => d.Status == AttendanceStatus.PaidLeave) / 1m;
            var unpaidLeaveDays = empDays.Count(d => d.Status == AttendanceStatus.UnpaidLeave) / 1m;

            if (empDays.Count == 0 && empLeaves.Count > 0)
            {
                // لم تتم معالجة الحضور بعد: نستخدم أيام العمل من طلبات الإجازة
                foreach (var l in empLeaves)
                {
                    var days = l.WorkingDays > 0 ? l.WorkingDays : l.DaysCount;
                    if (l.IsPaid && l.LeaveType != LeaveType.Unpaid) paidLeaveDays += days;
                    else unpaidLeaveDays += days;
                }
            }

            var absenceDays = empDays.Count(d => d.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence) / 1m;

            var lateMinutes = empDays
                .Where(d => d.Status is not (AttendanceStatus.RestDay or AttendanceStatus.Holiday
                            or AttendanceStatus.PaidLeave or AttendanceStatus.UnpaidLeave or AttendanceStatus.Leave))
                .Sum(d => d.LateMinutes);

            var rawOvertimeMinutes = empDays.Sum(d => d.RawOvertimeMinutes);
            var dayApprovedMinutes = empDays.Sum(d => d.ApprovedOvertimeMinutes);

            int approvedOvertimeMinutes;
            if (dayApprovedMinutes > 0)
            {
                approvedOvertimeMinutes = dayApprovedMinutes;
            }
            else if (requireApproval && approvalHoursByEmp.TryGetValue(emp.Id, out var hours))
            {
                approvedOvertimeMinutes = (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
            }
            else if (!requireApproval)
            {
                approvedOvertimeMinutes = rawOvertimeMinutes;
            }
            else
            {
                approvedOvertimeMinutes = 0;
            }

            // التقريب يُطبَّق مرة واحدة على إجمالي الشهر (وليس على كل بصمة)
            var paidOvertimeMinutes = requireApproval
                ? policy.RoundOvertimeMinutes(approvedOvertimeMinutes)
                : policy.RoundOvertimeMinutes(rawOvertimeMinutes);

            var empLoans = loans.Where(l => l.EmployeeId == emp.Id).ToList();
            var empDeductions = deductions.Where(d => d.EmployeeId == emp.Id).ToList();
            var empAdjustments = adjustments.Where(a => a.EmployeeId == emp.Id).ToList();

            var bonuses = empAdjustments.Where(a => a.Type == AdjustmentType.Bonus).Sum(a => a.Amount);
            var allowances = empAdjustments.Where(a => a.Type == AdjustmentType.Allowance).Sum(a => a.Amount);
            var otherDeductions = empAdjustments.Where(a => a.Type == AdjustmentType.OtherDeduction).Sum(a => a.Amount);
            var additionalDays = empAdjustments.Where(a => a.Type == AdjustmentType.AdditionalDays).Sum(a => a.Days ?? 0m);
            var additionalRate = empAdjustments
                .Where(a => a.Type == AdjustmentType.AdditionalDays && a.DailyRateOverride is > 0m)
                .Select(a => a.DailyRateOverride)
                .FirstOrDefault();

            // معامل الإضافي: سياسة الشركة = 1 (بدون مضاعفة). الأفضلية لمعامل التسويات إن وُجد.
            var multiplier = policy.DefaultOvertimeMultiplier <= 0m ? 1m : policy.DefaultOvertimeMultiplier;

            var inputs = new PayrollInputs
            {
                BasicSalary = emp.BasicSalary,
                DailyRateOverride = emp.DailyRate,
                StandardWorkHours = emp.StandardWorkHours,
                LateMinutes = lateMinutes,
                RawOvertimeMinutes = rawOvertimeMinutes,
                ApprovedOvertimeMinutes = approvedOvertimeMinutes,
                PaidOvertimeMinutes = paidOvertimeMinutes,
                AttendanceDays = attendanceDays,
                PaidRestDays = paidRestDays,
                PaidLeaveDays = paidLeaveDays,
                UnpaidLeaveDays = unpaidLeaveDays,
                AbsenceDays = absenceDays,
                Advances = empLoans.Sum(l => l.Amount),
                DeductionsTotal = empDeductions.Sum(d => d.Amount),
                OtherDeductions = otherDeductions,
                Bonuses = bonuses,
                Allowances = allowances,
                AdditionalDays = additionalDays,
                AdditionalDaysRate = additionalRate,
                OvertimeMultiplier = multiplier
            };

            var computation = PayrollCalculator.Compute(inputs, policy);

            var item = new PayrollItem
            {
                PayrollRunId = run.Id,
                EmployeeId = emp.Id,
                EmployeeCode = emp.EmployeeCode,
                EmployeeName = emp.FullName,
                DepartmentName = emp.Department,
                JobTitleName = emp.JobTitle,
                BasicSalary = computation.BasicSalary,
                DailyRate = computation.DailyRate,
                HourlyRate = computation.HourlyRate,
                MinuteRate = computation.MinuteRate,
                DailyRateBasisDays = computation.DailyRateBasisDays,
                WorkHoursPerDay = computation.WorkHoursPerDay,
                AttendanceDays = computation.AttendanceDays,
                PaidRestDays = computation.PaidRestDays,
                PaidLeaveDays = computation.PaidLeaveDays,
                UnpaidLeaveDays = computation.UnpaidLeaveDays,
                AbsenceDays = computation.AbsenceDays,
                LateMinutes = computation.LateMinutes,
                LateDeduction = computation.LateDeduction,
                RawOvertimeMinutes = computation.RawOvertimeMinutes,
                RawOvertimeHours = computation.RawOvertimeHours,
                ApprovedOvertimeHours = computation.ApprovedOvertimeHours,
                PaidOvertimeHours = computation.PaidOvertimeHours,
                OvertimeMultiplier = computation.OvertimeMultiplier,
                OvertimeAmount = computation.OvertimeAmount,
                AbsenceDeduction = computation.AbsenceDeduction,
                UnpaidLeaveDeduction = computation.UnpaidLeaveDeduction,
                LoansTotal = computation.Advances,
                DeductionsTotal = computation.DeductionsTotal,
                OtherDeductions = computation.OtherDeductions,
                Bonuses = computation.Bonuses,
                Allowances = computation.Allowances,
                AdditionalDaysCount = computation.AdditionalDaysCount,
                AdditionalDaysAmount = computation.AdditionalDaysAmount,
                GrossEarnings = computation.GrossEarnings,
                TotalDeductions = computation.TotalDeductions,
                GrossSalary = computation.GrossSalary,
                NetSalary = computation.NetSalary,
                Status = PayrollStatus.Computed,
                CalculationDetails = computation.CalculationDetails,
                Notes = $"أيام راحة مدفوعة: {paidRestDays} | عطلات رسمية مدفوعة: {paidHolidayDays} (مشمولة في الراتب الشهري)",
                CreatedAt = lockedAt,
                CreatedBy = user
            };

            _db.PayrollItems.Add(item);
            await _db.SaveChangesAsync(ct);

            // ربط المكوّنات لمنع إعادة استخدامها في كشف آخر
            foreach (var l in empLoans) l.PayrollItemId = item.Id;
            foreach (var d in empDeductions) d.PayrollItemId = item.Id;
            foreach (var a in empAdjustments) a.PayrollItemId = item.Id;

            // ربط الإجازات التي دخلت في الاحتساب
            if (empDays.Count > 0)
            {
                var usedLeaves = empDays.Where(d => d.LeaveRequestId.HasValue).Select(d => d.LeaveRequestId!.Value).ToHashSet();
                foreach (var l in empLeaves.Where(l => usedLeaves.Contains(l.Id))) l.PayrollItemId = item.Id;
            }
            else
            {
                foreach (var l in empLeaves) l.PayrollItemId = item.Id;
            }

            result.ItemCount++;
        }

        await _db.SaveChangesAsync(ct);

        run.TotalNet = _db.PayrollItems.Local.Where(i => i.PayrollRunId == run.Id).Sum(i => i.NetSalary);
        run.TotalGross = _db.PayrollItems.Local.Where(i => i.PayrollRunId == run.Id).Sum(i => i.GrossEarnings);
        run.TotalOvertime = _db.PayrollItems.Local.Where(i => i.PayrollRunId == run.Id).Sum(i => i.OvertimeAmount);
        run.TotalDeductions = _db.PayrollItems.Local.Where(i => i.PayrollRunId == run.Id).Sum(i => i.TotalDeductions);
        run.TotalBonuses = _db.PayrollItems.Local.Where(i => i.PayrollRunId == run.Id).Sum(i => i.Bonuses + i.Allowances);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("توليد كشف رواتب", nameof(PayrollRun), run.Id.ToString(),
            null, $"{year}-{month:00} إصدار {run.Version}: {result.ItemCount} موظف، صافي {run.TotalNet}",
            user, userId, ct);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        result.PayrollRunId = run.Id;
        result.TotalNet = run.TotalNet;
        result.TotalGross = run.TotalGross;
        result.TotalDeductions = run.TotalDeductions;
        result.Version = run.Version;

        _logger.LogInformation("تم توليد كشف الرواتب {Year}-{Month} إصدار {Version}", year, month, run.Version);
        return result;
    }

    /// <summary>يفصل ارتباط المكوّنات بأسطر كشف (يُستخدم عند الأرشفة/الإلغاء/إعادة الفتح).</summary>
    private async Task UnlinkAsync(List<int> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return;
        foreach (var l in await _db.Loans.Where(x => x.PayrollItemId != null && itemIds.Contains(x.PayrollItemId.Value)).ToListAsync(ct))
            l.PayrollItemId = null;
        foreach (var d in await _db.Deductions.Where(x => x.PayrollItemId != null && itemIds.Contains(x.PayrollItemId.Value)).ToListAsync(ct))
            d.PayrollItemId = null;
        foreach (var o in await _db.OvertimeApprovals.Where(x => x.PayrollItemId != null && itemIds.Contains(x.PayrollItemId.Value)).ToListAsync(ct))
            o.PayrollItemId = null;
        foreach (var a in await _db.PayrollAdjustments.Where(x => x.PayrollItemId != null && itemIds.Contains(x.PayrollItemId.Value)).ToListAsync(ct))
            a.PayrollItemId = null;
        foreach (var lv in await _db.LeaveRequests.Where(x => x.PayrollItemId != null && itemIds.Contains(x.PayrollItemId.Value)).ToListAsync(ct))
            lv.PayrollItemId = null;
    }

    private async Task<PayrollRun> GetRunAsync(int id, CancellationToken ct)
        => await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == id, ct)
           ?? throw new InvalidOperationException("كشف الرواتب غير موجود");

    /// <summary>نقل الكشف إلى حالة "قيد المراجعة".</summary>
    public async Task MarkPendingReviewAsync(int payrollRunId, string? user, string? userId, CancellationToken ct = default)
    {
        var run = await GetRunAsync(payrollRunId, ct);
        if (run.IsLocked) throw new InvalidOperationException("الكشف مقفل ولا يمكن تغيير حالته.");
        run.Status = PayrollStatus.PendingReview;
        await _audit.LogAsync("إرسال كشف الرواتب للمراجعة", nameof(PayrollRun), run.Id.ToString(), null, "PendingReview", user, userId, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>اعتماد الكشف (يبقى قابلًا للقفل).</summary>
    public async Task ApproveAsync(int payrollRunId, string? user, string? userId, CancellationToken ct = default)
    {
        var run = await GetRunAsync(payrollRunId, ct);
        if (run.Status == PayrollStatus.Approved) return;

        run.Status = PayrollStatus.Approved;
        run.ApprovedAt = DateTime.UtcNow;
        run.ApprovedBy = user;

        foreach (var i in await _db.PayrollItems.Where(i => i.PayrollRunId == payrollRunId).ToListAsync(ct))
            i.Status = PayrollStatus.Approved;

        await _audit.LogAsync("اعتماد كشف رواتب", nameof(PayrollRun), run.Id.ToString(), "Computed", "Approved", user, userId, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>قفل الكشف نهائيًا (يمنع أي إعادة احتساب تلقائية).</summary>
    public async Task LockAsync(int payrollRunId, string? user, string? userId, CancellationToken ct = default)
    {
        var run = await GetRunAsync(payrollRunId, ct);
        if (run.Status == PayrollStatus.Locked) return;
        if (run.Status != PayrollStatus.Approved)
            throw new InvalidOperationException("لا يمكن قفل الكشف قبل اعتماده.");

        run.Status = PayrollStatus.Locked;
        run.LockedAt = DateTime.UtcNow;
        run.LockedBy = user;

        foreach (var i in await _db.PayrollItems.Where(i => i.PayrollRunId == payrollRunId).ToListAsync(ct))
            i.Status = PayrollStatus.Locked;

        await _audit.LogAsync("قفل كشف رواتب", nameof(PayrollRun), run.Id.ToString(), "Approved", "Locked", user, userId, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>إعادة فتح كشف مقفل بسبب مُسجَّل (تصحيح معتمد فقط).</summary>
    public async Task ReopenAsync(int payrollRunId, string reason, string? user, string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("سبب إعادة الفتح مطلوب.");

        var run = await GetRunAsync(payrollRunId, ct);
        if (run.Status is not (PayrollStatus.Approved or PayrollStatus.Locked))
            throw new InvalidOperationException("إعادة الفتح متاحة فقط للكشوف المعتمدة أو المقفلة.");

        var previous = run.Status.ToString();
        run.Status = PayrollStatus.Reopened;
        run.ReopenedAt = DateTime.UtcNow;
        run.ReopenedBy = user;
        run.ReopenReason = reason;

        await _audit.LogAsync("إعادة فتح كشف رواتب", nameof(PayrollRun), run.Id.ToString(), previous,
            $"Reopened: {reason}", user, userId, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>إلغاء الكشف مع فصل المكوّنات لإتاحة إعادة الاحتساب.</summary>
    public async Task CancelAsync(int payrollRunId, string? user, string? userId, CancellationToken ct = default)
    {
        var run = await GetRunAsync(payrollRunId, ct);
        if (run.Status == PayrollStatus.Locked)
            throw new InvalidOperationException("لا يمكن إلغاء كشف مقفل. أعد فتحه أولًا بسبب مُسجَّل.");

        var itemIds = await _db.PayrollItems.Where(i => i.PayrollRunId == payrollRunId).Select(i => i.Id).ToListAsync(ct);
        await UnlinkAsync(itemIds, ct);

        var previous = run.Status.ToString();
        run.Status = PayrollStatus.Cancelled;
        foreach (var i in await _db.PayrollItems.Where(i => i.PayrollRunId == payrollRunId).ToListAsync(ct))
            i.Status = PayrollStatus.Cancelled;

        await _audit.LogAsync("إلغاء كشف رواتب", nameof(PayrollRun), run.Id.ToString(), previous, "Cancelled", user, userId, ct);
        await _db.SaveChangesAsync(ct);
    }
}
