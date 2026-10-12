using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
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
    public int Version { get; set; }
}

/// <summary>توليد وإدارة كشوف الرواتب الشهرية.</summary>
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

    public async Task<PayrollGenerationResult> GenerateAsync(int year, int month, string? user = null, string? userId = null, CancellationToken ct = default)
    {
        var policy = await _settings.GetPolicyAsync(ct);
        var requireApproval = await GetBoolAsync(SettingKeys.RequireOvertimeApproval, true, ct);
        var first = new DateTime(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var existing = await _db.PayrollRuns.Where(r => r.Year == year && r.Month == month).ToListAsync(ct);
        if (existing.Any(r => r.Status == PayrollStatus.Approved))
            throw new InvalidOperationException("يوجد كشف رواتب معتمد لهذا الشهر. يجب إلغاء الاعتماد قبل إعادة الاحتساب.");

        var previousVersion = existing.Count == 0 ? 0 : existing.Max(r => r.Version);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // أرشفة الكشوف السابقة غير المعتمدة وفصل ارتباطاتها حتى لا تُخصم السلف/الخصومات مرتين
        foreach (var oldRun in existing.Where(r => r.Status != PayrollStatus.Cancelled))
        {
            var oldItemIds = await _db.PayrollItems.Where(i => i.PayrollRunId == oldRun.Id).Select(i => i.Id).ToListAsync(ct);
            foreach (var l in await _db.Loans.Where(x => x.PayrollItemId != null && oldItemIds.Contains(x.PayrollItemId.Value)).ToListAsync(ct))
                l.PayrollItemId = null;
            foreach (var d in await _db.Deductions.Where(x => x.PayrollItemId != null && oldItemIds.Contains(x.PayrollItemId.Value)).ToListAsync(ct))
                d.PayrollItemId = null;
            foreach (var o in await _db.OvertimeApprovals.Where(x => x.PayrollItemId != null && oldItemIds.Contains(x.PayrollItemId.Value)).ToListAsync(ct))
                o.PayrollItemId = null;

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

        var employees = await _db.Employees.Where(e => e.IsActive).ToListAsync(ct);
        var employeeIds = employees.Select(e => e.Id).ToList();

        var lateByEmp = await _db.DailyAttendances
            .Where(d => employeeIds.Contains(d.EmployeeId) && d.WorkDate >= first && d.WorkDate <= last)
            .GroupBy(d => d.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Late = g.Sum(x => x.LateMinutes) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Late, ct);

        // تُجمع الساعات في الذاكرة للحفاظ على دقة النوع decimal (ولا نعتمد على تحويل في قاعدة البيانات)
        var approvedOtRows = await _db.OvertimeApprovals
            .AsNoTracking()
            .Where(o => employeeIds.Contains(o.EmployeeId) && o.Status == ApprovalStatus.Approved
                        && o.PeriodStart >= first && o.PeriodEnd <= last && o.PayrollItemId == null)
            .Select(o => new { o.EmployeeId, o.ApprovedHours })
            .ToListAsync(ct);
        var approvedOtHours = approvedOtRows
            .GroupBy(o => o.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.ApprovedHours));

        var computedOtMinutes = await _db.DailyAttendances
            .Where(d => employeeIds.Contains(d.EmployeeId) && d.WorkDate >= first && d.WorkDate <= last)
            .GroupBy(d => d.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Minutes = g.Sum(x => x.ApprovedOvertimeMinutes) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Minutes, ct);

        var loans = await _db.Loans
            .Where(l => employeeIds.Contains(l.EmployeeId) && l.DeductionYear == year && l.DeductionMonth == month
                        && l.Status == ApprovalStatus.Approved && l.PayrollItemId == null)
            .ToListAsync(ct);

        var deductions = await _db.Deductions
            .Where(d => employeeIds.Contains(d.EmployeeId) && d.Year == year && d.Month == month
                        && d.Status == ApprovalStatus.Approved && d.PayrollItemId == null)
            .ToListAsync(ct);

        var approvals = await _db.OvertimeApprovals
            .Where(o => employeeIds.Contains(o.EmployeeId) && o.Status == ApprovalStatus.Approved
                        && o.PeriodStart >= first && o.PeriodEnd <= last && o.PayrollItemId == null)
            .ToListAsync(ct);

        var items = new List<PayrollItem>();
        foreach (var emp in employees)
        {
            var lateMinutes = lateByEmp.TryGetValue(emp.Id, out var lm) ? lm : 0;

            int otMinutes;
            if (requireApproval)
                otMinutes = approvedOtHours.TryGetValue(emp.Id, out var ah)
                    ? (int)Math.Round(ah * 60m, MidpointRounding.AwayFromZero)
                    : 0;
            else
                otMinutes = computedOtMinutes.TryGetValue(emp.Id, out var cm) ? cm : 0;

            var otHours = Math.Round(otMinutes / 60m, 2, MidpointRounding.AwayFromZero);

            // معامل الإضافي الفعلي (مرجّح حسب الاعتمادات إن وجدت)
            var empApprovals = approvals.Where(a => a.EmployeeId == emp.Id).ToList();
            decimal multiplier = policy.DefaultOvertimeMultiplier;
            if (empApprovals.Count > 0 && empApprovals.Sum(a => a.ApprovedHours) > 0)
            {
                var weighted = empApprovals.Sum(a => a.ApprovedHours * a.OvertimeMultiplier) / empApprovals.Sum(a => a.ApprovedHours);
                multiplier = Math.Round(weighted, 4, MidpointRounding.AwayFromZero);
            }

            var empLoans = loans.Where(l => l.EmployeeId == emp.Id).ToList();
            var empDeductions = deductions.Where(d => d.EmployeeId == emp.Id).ToList();

            var computation = PayrollCalculator.Compute(
                emp.BasicSalary, lateMinutes, otHours, multiplier,
                empLoans.Sum(l => l.Amount), empDeductions.Sum(d => d.Amount), 0m, policy);

            var item = new PayrollItem
            {
                PayrollRunId = run.Id,
                EmployeeId = emp.Id,
                EmployeeCode = emp.EmployeeCode,
                EmployeeName = emp.FullName,
                BasicSalary = computation.BasicSalary,
                LateMinutes = computation.LateMinutes,
                MinuteRate = computation.MinuteRate,
                LateDeduction = computation.LateDeduction,
                ApprovedOvertimeHours = computation.ApprovedOvertimeHours,
                OvertimeMultiplier = computation.OvertimeMultiplier,
                OvertimeValue = computation.OvertimeValue,
                LoansTotal = computation.LoansTotal,
                DeductionsTotal = computation.DeductionsTotal,
                OtherDeductions = computation.OtherDeductions,
                GrossSalary = computation.GrossSalary,
                NetSalary = computation.NetSalary,
                Status = PayrollStatus.Computed,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = user
            };
            _db.PayrollItems.Add(item);
            items.Add(item);

            // ربط السلف والخصومات والاعتمادات بهذا الكشف لمنع الخصم المزدوج
            foreach (var l in empLoans) l.PayrollItem = item;
            foreach (var d in empDeductions) d.PayrollItem = item;
            if (requireApproval)
            {
                foreach (var a in empApprovals) a.PayrollItem = item;
            }
        }

        await _db.SaveChangesAsync(ct);

        run.TotalNet = items.Sum(i => i.NetSalary);
        run.TotalOvertime = items.Sum(i => i.OvertimeValue);
        run.TotalDeductions = items.Sum(i => i.LateDeduction + i.LoansTotal + i.DeductionsTotal + i.OtherDeductions);
        await _db.SaveChangesAsync(ct);

        _audit.Log("توليد كشف رواتب", nameof(PayrollRun), run.Id.ToString(),
            null, $"{year}-{month} إصدار {run.Version}: {items.Count} موظف، صافي {run.TotalNet}", user, userId);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        _logger.LogInformation("تم توليد كشف الرواتب {Year}-{Month} v{Version}", year, month, run.Version);

        return new PayrollGenerationResult
        {
            PayrollRunId = run.Id,
            ItemCount = items.Count,
            TotalNet = run.TotalNet,
            Version = run.Version
        };
    }

    public async Task ApproveAsync(int payrollRunId, string? user, string? userId, CancellationToken ct = default)
    {
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == payrollRunId, ct)
                  ?? throw new InvalidOperationException("الكشف غير موجود");
        if (run.Status == PayrollStatus.Approved) return;

        run.Status = PayrollStatus.Approved;
        run.ApprovedAt = DateTime.UtcNow;
        run.ApprovedBy = user;

        var items = await _db.PayrollItems.Where(i => i.PayrollRunId == payrollRunId).ToListAsync(ct);
        foreach (var i in items) i.Status = PayrollStatus.Approved;

        _audit.Log("اعتماد كشف رواتب", nameof(PayrollRun), run.Id.ToString(), "Computed", "Approved", user, userId);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>إلغاء الاعتماد مع فصل السلف والخصومات لإتاحة إعادة الاحتساب.</summary>
    public async Task CancelAsync(int payrollRunId, string? user, string? userId, CancellationToken ct = default)
    {
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == payrollRunId, ct)
                  ?? throw new InvalidOperationException("الكشف غير موجود");

        var itemIds = await _db.PayrollItems.Where(i => i.PayrollRunId == payrollRunId).Select(i => i.Id).ToListAsync(ct);

        foreach (var l in await _db.Loans.Where(l => l.PayrollItemId != null && itemIds.Contains(l.PayrollItemId.Value)).ToListAsync(ct))
            l.PayrollItemId = null;
        foreach (var d in await _db.Deductions.Where(d => d.PayrollItemId != null && itemIds.Contains(d.PayrollItemId.Value)).ToListAsync(ct))
            d.PayrollItemId = null;
        foreach (var o in await _db.OvertimeApprovals.Where(o => o.PayrollItemId != null && itemIds.Contains(o.PayrollItemId.Value)).ToListAsync(ct))
            o.PayrollItemId = null;

        run.Status = PayrollStatus.Cancelled;
        foreach (var i in await _db.PayrollItems.Where(i => i.PayrollRunId == payrollRunId).ToListAsync(ct))
            i.Status = PayrollStatus.Cancelled;

        _audit.Log("إلغاء اعتماد كشف رواتب", nameof(PayrollRun), run.Id.ToString(), run.Status.ToString(), "Cancelled", user, userId);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<bool> GetBoolAsync(string key, bool def, CancellationToken ct)
    {
        var v = await _settings.GetAsync(key, ct);
        return v is null ? def : bool.TryParse(v, out var b) ? b : def;
    }
}
