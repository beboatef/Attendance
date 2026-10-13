using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

/// <summary>تعديل سطر واحد في كشف المرتبات (الحقول القابلة للتحرير فقط).</summary>
public sealed class PayrollRowEdit
{
    public int PayrollItemId { get; set; }

    /// <summary>الغياب المعتمد بعد المراجعة (يختلف عن الغياب الأصلي من البصمة).</summary>
    public decimal? ApprovedAbsenceDays { get; set; }

    /// <summary>أيام الإجازة السنوية المسجَّلة على السطر (تُخصم من الرصيد ولا تُخصم كغياب).</summary>
    public decimal? AnnualLeaveDays { get; set; }

    /// <summary>أيام إجازات أخرى بدون أجر (تُخصم أيام عمل).</summary>
    public decimal? OtherUnpaidLeaveDays { get; set; }

    /// <summary>ساعات الإضافي المعتمدة يدويًا.</summary>
    public decimal? OvertimeHours { get; set; }

    public decimal? Bonuses { get; set; }
    public decimal? Allowances { get; set; }
    public decimal? Advances { get; set; }
    public decimal? OtherDeductions { get; set; }

    /// <summary>عدد أيام العمل الإضافية المستحقة.</summary>
    public decimal? ExtraDays { get; set; }

    /// <summary>قيمة اليوم المستخدمة في الأيام الإضافية (فارغ = الأجر اليومي للموظف).</summary>
    public decimal? ExtraDaysRate { get; set; }
}

/// <summary>نتيجة حفظ سطر واحد: القيم الجديدة المعاد حسابها على السيرفر.</summary>
public sealed class PayrollRowResult
{
    public int PayrollItemId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public bool Saved { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; } = new();

    public decimal AbsenceDays { get; set; }
    public decimal OriginalAbsenceDays { get; set; }
    public decimal AnnualLeaveDays { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public decimal AnnualLeaveBalance { get; set; }
    public decimal ApprovedOvertimeHours { get; set; }
    public decimal PaidOvertimeHours { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal Bonuses { get; set; }
    public decimal Allowances { get; set; }
    public decimal AdditionalDaysCount { get; set; }
    public decimal AdditionalDaysAmount { get; set; }
    public decimal Advances { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal LateDeduction { get; set; }
    public decimal AbsenceDeduction { get; set; }
    public decimal UnpaidLeaveDeduction { get; set; }
    public decimal GrossEarnings { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSalary { get; set; }
    public bool NeedsReview { get; set; }
    public string ReviewStatusText { get; set; } = string.Empty;
}

/// <summary>نتيجة حفظ مجموعة أسطر مع إجماليات الكشف المحدَّثة.</summary>
public sealed class PayrollSheetSaveResult
{
    public List<PayrollRowResult> Rows { get; init; } = new();
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalOvertime { get; set; }
    public int SavedCount { get; set; }
    public bool AnyError => Rows.Any(r => !r.Saved);
}

/// <summary>
/// تعديل كشف المرتبات مباشرة من الجدول (شبيه Excel).
/// كل القيم يُعاد التحقق منها ويُعاد حسابها على السيرفر بالمعادلات نفسها المستخدمة في الاحتساب،
/// ولا تُقبل أي قيمة من الواجهة كحقيقة مالية نهائية.
/// </summary>
public class PayrollSheetService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;

    public PayrollSheetService(AppDbContext db, SettingsService settings, AuditService audit)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
    }

    /// <summary>هل يمكن تعديل الكشف في حالته الحالية؟ (يُمنع بعد الاعتماد أو القفل)</summary>
    public static bool IsEditable(PayrollStatus status)
        => status is PayrollStatus.Draft or PayrollStatus.Computed
            or PayrollStatus.PendingReview or PayrollStatus.Reopened;

    /// <summary>يحفظ تعديلات مجموعة أسطر داخل الكشف ويُعيد حسابها.</summary>
    public async Task<PayrollSheetSaveResult> SaveAsync(
        int payrollRunId, IReadOnlyList<PayrollRowEdit> edits, bool allowOverdraft,
        string? user, string? userId, CancellationToken ct = default)
    {
        var result = new PayrollSheetSaveResult();

        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == payrollRunId, ct)
                  ?? throw new InvalidOperationException("كشف الرواتب غير موجود.");

        if (!IsEditable(run.Status))
            throw new InvalidOperationException(
                $"لا يمكن تعديل كشف بحالة \"{ExcelExportService.PayrollStatusText(run.Status)}\". " +
                "أعد فتح الكشف بسبب مُسجَّل (أو احتسبه) قبل التعديل.");

        var policy = await _settings.GetPolicyAsync(ct);
        var workingDaysInPeriod = await CountWorkingDaysAsync(run.Year, run.Month, ct);

        var ids = edits.Select(e => e.PayrollItemId).Distinct().ToList();
        var items = await _db.PayrollItems
            .Include(i => i.Employee)
            .Where(i => i.PayrollRunId == payrollRunId && ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        if (items.Count != ids.Count)
            throw new InvalidOperationException("يوجد سطر لا ينتمي إلى هذا الكشف.");

        foreach (var edit in edits)
        {
            var item = items[edit.PayrollItemId];
            var row = ApplyEdit(item, run, edit, policy, workingDaysInPeriod, allowOverdraft, user, userId);
            result.Rows.Add(row);
        }

        result.SavedCount = result.Rows.Count(r => r.Saved);

        if (result.SavedCount > 0)
        {
            await _db.SaveChangesAsync(ct);

            var all = await _db.PayrollItems.Where(i => i.PayrollRunId == payrollRunId).ToListAsync(ct);
            run.TotalNet = all.Sum(i => i.NetSalary);
            run.TotalGross = all.Sum(i => i.GrossEarnings);
            run.TotalOvertime = all.Sum(i => i.OvertimeAmount);
            run.TotalDeductions = all.Sum(i => i.TotalDeductions);
            run.TotalBonuses = all.Sum(i => i.Bonuses + i.Allowances);
            run.UpdatedAt = DateTime.UtcNow;
            run.UpdatedBy = user;
            await _db.SaveChangesAsync(ct);

            result.TotalGross = run.TotalGross;
            result.TotalDeductions = run.TotalDeductions;
            result.TotalNet = run.TotalNet;
            result.TotalOvertime = run.TotalOvertime;
        }

        return result;
    }

    private PayrollRowResult ApplyEdit(
        PayrollItem item, PayrollRun run, PayrollRowEdit edit, AttendancePolicy policy,
        decimal workingDaysInPeriod, bool allowOverdraft, string? user, string? userId)
    {
        var row = new PayrollRowResult
        {
            PayrollItemId = item.Id,
            EmployeeCode = item.EmployeeCode,
            EmployeeName = item.EmployeeName
        };

        // لقطة القيم قبل التعديل (تُسجَّل في سجل المراجعة: القيمة الأصلية والقيمة المعدلة)
        var before = new
        {
            AbsenceDays = item.AbsenceDays,
            AnnualLeaveDays = item.AnnualLeaveDays,
            UnpaidLeaveDays = item.UnpaidLeaveDays,
            OvertimeHours = item.ApprovedOvertimeHours,
            Bonuses = item.Bonuses,
            Allowances = item.Allowances,
            Advances = item.LoansTotal,
            OtherDeductions = item.OtherDeductions,
            ExtraDays = item.AdditionalDaysCount,
            Gross = item.GrossEarnings,
            Deductions = item.TotalDeductions,
            Net = item.NetSalary
        };

        // ---------- 1) قراءة القيم المقترحة والتحقق من صحتها ----------
        var absenceDays = edit.ApprovedAbsenceDays ?? item.AbsenceDays;
        var annualLeaveDays = edit.AnnualLeaveDays ?? item.AnnualLeaveDays;
        var unpaidLeaveDays = edit.OtherUnpaidLeaveDays ?? item.UnpaidLeaveDays;
        var overtimeHours = edit.OvertimeHours ?? item.ManualOvertimeHours ?? item.ApprovedOvertimeHours;
        var bonuses = edit.Bonuses ?? item.Bonuses;
        var allowances = edit.Allowances ?? item.Allowances;
        var advances = edit.Advances ?? item.LoansTotal;
        var otherDeductions = edit.OtherDeductions ?? item.OtherDeductions;
        var extraDays = edit.ExtraDays ?? item.AdditionalDaysCount;
        var extraDaysRate = edit.ExtraDaysRate ?? item.AdditionalDaysRate;

        var numeric = new (string Label, decimal Value)[]
        {
            ("الغياب المعتمد", absenceDays),
            ("الإجازة السنوية", annualLeaveDays),
            ("الإجازات الأخرى", unpaidLeaveDays),
            ("ساعات الإضافي", overtimeHours),
            ("المكافآت", bonuses),
            ("البدلات", allowances),
            ("السلف", advances),
            ("الخصومات الأخرى", otherDeductions),
            ("الأيام الإضافية", extraDays)
        };

        foreach (var (label, value) in numeric)
            if (value < 0)
            {
                row.Saved = false;
                row.Error = $"{label}: لا يُسمح بقيمة سالبة ({value:0.##}).";
                return row;
            }

        if (extraDaysRate is < 0)
        {
            row.Saved = false;
            row.Error = "قيمة اليوم للأيام الإضافية لا يمكن أن تكون سالبة.";
            return row;
        }

        var totalLeaveDays = absenceDays + annualLeaveDays + unpaidLeaveDays;
        if (totalLeaveDays > workingDaysInPeriod)
        {
            row.Saved = false;
            row.Error = $"مجموع (الغياب المعتمد + الإجازة السنوية + الإجازات الأخرى) = {totalLeaveDays:0.##} يوم، " +
                        $"وهو أكبر من أيام العمل في الفترة ({workingDaysInPeriod:0.##} يوم).";
            return row;
        }

        if (overtimeHours > 744m)
        {
            row.Saved = false;
            row.Error = "ساعات الإضافي أكبر من الحد المنطقي لشهر واحد (744 ساعة).";
            return row;
        }

        // ---------- 2) رصيد الإجازة السنوية (منع الخصم المزدوج) ----------
        var employee = item.Employee;
        var alreadyApplied = item.AnnualLeaveBalanceApplied;
        var availableForThisRow = (employee?.AnnualLeaveBalance ?? 0m) + alreadyApplied;
        var additionalNeeded = annualLeaveDays - alreadyApplied;

        if (additionalNeeded > availableForThisRow + 0.0001m)
        {
            var warning = $"رصيد الإجازة السنوية المتاح للموظف {availableForThisRow:0.##} يوم فقط، " +
                          $"والمطلوب خصمه لهذا السطر {annualLeaveDays:0.##} يوم (فرق {additionalNeeded:0.##} يوم).";
            row.Warnings.Add(warning);
            if (!allowOverdraft)
            {
                row.Saved = false;
                row.RequiresConfirmation = true;
                row.Error = warning + " أكِّد تجاوز الرصيد للمتابعة.";
                return row;
            }
        }

        // ---------- 3) إعادة الحساب على السيرفر بالمعادلات المعتمدة ----------
        var approvedMinutes = (int)Math.Round(overtimeHours * 60m, MidpointRounding.AwayFromZero);
        var paidMinutes = item.ManualOvertimeHours.HasValue || edit.OvertimeHours.HasValue
            ? approvedMinutes
            : (int)Math.Round(item.PaidOvertimeHours * 60m, MidpointRounding.AwayFromZero);

        var inputs = new PayrollInputs
        {
            BasicSalary = item.BasicSalary,
            DailyRateOverride = item.DailyRate > 0 ? item.DailyRate : null,
            StandardWorkHours = item.WorkHoursPerDay,
            LateMinutes = item.LateMinutes,
            // القيمة الخام لا تُنقص الإضافي المعتمد يدويًا: نرفع الخام إلى المعتمد عند التعديل اليدوي.
            RawOvertimeMinutes = Math.Max(item.RawOvertimeMinutes, approvedMinutes),
            ApprovedOvertimeMinutes = approvedMinutes,
            PaidOvertimeMinutes = paidMinutes,
            AttendanceDays = item.AttendanceDays,
            PaidRestDays = item.PaidRestDays,
            PaidLeaveDays = item.PaidLeaveDays,
            UnpaidLeaveDays = unpaidLeaveDays,
            AbsenceDays = absenceDays,
            Advances = advances,
            DeductionsTotal = item.DeductionsTotal,
            OtherDeductions = otherDeductions,
            Bonuses = bonuses,
            Allowances = allowances,
            AdditionalDays = extraDays,
            AdditionalDaysRate = extraDaysRate,
            OvertimeMultiplier = item.OvertimeMultiplier <= 0 ? 1m : item.OvertimeMultiplier
        };

        var c = PayrollCalculator.Compute(inputs, policy);

        // ---------- 4) تثبيت القيم على السطر ----------
        item.AbsenceDays = c.AbsenceDays;
        item.AnnualLeaveDays = annualLeaveDays;
        item.UnpaidLeaveDays = c.UnpaidLeaveDays;
        item.ManualOvertimeHours = overtimeHours;
        item.ApprovedOvertimeHours = c.ApprovedOvertimeHours;
        item.PaidOvertimeHours = c.PaidOvertimeHours;
        item.OvertimeAmount = c.OvertimeAmount;
        item.LateDeduction = c.LateDeduction;
        item.AbsenceDeduction = c.AbsenceDeduction;
        item.UnpaidLeaveDeduction = c.UnpaidLeaveDeduction;
        item.Bonuses = c.Bonuses;
        item.Allowances = c.Allowances;
        item.AdditionalDaysCount = c.AdditionalDaysCount;
        item.AdditionalDaysAmount = c.AdditionalDaysAmount;
        item.AdditionalDaysRate = extraDaysRate;
        item.LoansTotal = c.Advances;
        item.OtherDeductions = c.OtherDeductions;
        item.GrossEarnings = c.GrossEarnings;
        item.GrossSalary = c.GrossSalary;
        item.TotalDeductions = c.TotalDeductions;
        item.NetSalary = c.NetSalary;
        item.CalculationDetails = c.CalculationDetails;
        item.IsManuallyAdjusted = true;
        item.LastEditedAt = DateTime.UtcNow;
        item.LastEditedBy = user;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = user;

        // خصم رصيد الإجازة السنوية بالفرق فقط (لا خصم مزدوج عند إعادة الحفظ أو إعادة الفتح)
        var delta = annualLeaveDays - alreadyApplied;
        if (delta != 0m && employee is not null)
        {
            employee.AnnualLeaveBalance = PayrollCalculator.Round2(employee.AnnualLeaveBalance - delta);
            employee.UpdatedAt = DateTime.UtcNow;
            employee.UpdatedBy = user;
        }
        item.AnnualLeaveBalanceApplied = annualLeaveDays;

        // ---------- 5) سجل المراجعة (القيمة الأصلية والقيمة المعدلة) ----------
        var oldSummary = $"غياب أصلي {item.OriginalAbsenceDays:0.##}، غياب معتمد {before.AbsenceDays:0.##}، " +
                         $"سنوية {before.AnnualLeaveDays:0.##}، إجازات أخرى {before.UnpaidLeaveDays:0.##}، " +
                         $"إضافي {before.OvertimeHours:0.##}س، مكافآت {before.Bonuses:0.##}، " +
                         $"بدلات {before.Allowances:0.##}، سلف {before.Advances:0.##}، " +
                         $"خصومات أخرى {before.OtherDeductions:0.##}، أيام إضافية {before.ExtraDays:0.##}، " +
                         $"استحقاقات {before.Gross:0.##}، خصومات {before.Deductions:0.##}، صافي {before.Net:0.##}";
        var newSummary = $"غياب أصلي {item.OriginalAbsenceDays:0.##} → معتمد {c.AbsenceDays:0.##}، " +
                         $"سنوية {annualLeaveDays:0.##}، إجازات أخرى {c.UnpaidLeaveDays:0.##}، " +
                         $"إضافي {c.ApprovedOvertimeHours:0.##}س ({c.OvertimeAmount:0.##})، " +
                         $"مكافآت {c.Bonuses:0.##}، بدلات {c.Allowances:0.##}، سلف {c.Advances:0.##}، " +
                         $"خصومات أخرى {c.OtherDeductions:0.##}، أيام إضافية {c.AdditionalDaysCount:0.##} " +
                         $"({c.AdditionalDaysAmount:0.##})، استحقاقات {c.GrossEarnings:0.##}، " +
                         $"خصومات {c.TotalDeductions:0.##}، صافي {c.NetSalary:0.##}";
        _audit.Log($"تعديل سطر كشف المرتبات {run.Year}-{run.Month:00}",
            nameof(PayrollItem), item.Id.ToString(), oldSummary, newSummary, user, userId);

        row.Saved = true;
        row.AbsenceDays = c.AbsenceDays;
        row.OriginalAbsenceDays = item.OriginalAbsenceDays;
        row.AnnualLeaveDays = annualLeaveDays;
        row.UnpaidLeaveDays = c.UnpaidLeaveDays;
        row.AnnualLeaveBalance = employee?.AnnualLeaveBalance ?? 0m;
        row.ApprovedOvertimeHours = c.ApprovedOvertimeHours;
        row.PaidOvertimeHours = c.PaidOvertimeHours;
        row.OvertimeAmount = c.OvertimeAmount;
        row.Bonuses = c.Bonuses;
        row.Allowances = c.Allowances;
        row.AdditionalDaysCount = c.AdditionalDaysCount;
        row.AdditionalDaysAmount = c.AdditionalDaysAmount;
        row.Advances = c.Advances;
        row.OtherDeductions = c.OtherDeductions;
        row.LateDeduction = c.LateDeduction;
        row.AbsenceDeduction = c.AbsenceDeduction;
        row.UnpaidLeaveDeduction = c.UnpaidLeaveDeduction;
        row.GrossEarnings = c.GrossEarnings;
        row.TotalDeductions = c.TotalDeductions;
        row.NetSalary = c.NetSalary;
        row.NeedsReview = annualLeaveDays > 0 && row.AnnualLeaveBalance < 0;
        row.ReviewStatusText = row.NeedsReview ? "يحتاج مراجعة" : "مُعدَّل يدويًا";

        if (annualLeaveDays > 0)
            row.Warnings.Add($"تم خصم {annualLeaveDays:0.##} يوم من الرصيد السنوي، ولم تُخصم كغياب.");
        if (overtimeHours > item.RawOvertimeHours && item.RawOvertimeMinutes > 0)
            row.Warnings.Add($"الإضافي المعتمد ({overtimeHours:0.##}س) أكبر من المستخرج من البصمة ({item.RawOvertimeHours:0.##}س).");

        return row;
    }

    /// <summary>أيام العمل في الشهر (باستثناء أيام الراحة الأسبوعية) — سقف قواعد التحقق.</summary>
    private async Task<decimal> CountWorkingDaysAsync(int year, int month, CancellationToken ct)
    {
        var rest = await _settings.GetWeeklyRestDaysAsync(ct);
        var days = DateTime.DaysInMonth(year, month);
        var count = 0m;
        for (var d = 1; d <= days; d++)
            if (!rest.Contains(new DateTime(year, month, d).DayOfWeek)) count += 1m;
        return Math.Max(1m, count);
    }
}
