using HrPayroll.Domain.Entities;
using HrPayroll.Domain.ValueObjects;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

/// <summary>يحدد الوردية السارية لموظف في تاريخ معين (يدعم الورديات المتغيرة).</summary>
public class ShiftResolver
{
    private readonly AppDbContext _db;

    public ShiftResolver(AppDbContext db) => _db = db;

    public async Task<Shift?> ResolveAsync(int employeeId, DateTime workDate, CancellationToken ct = default)
    {
        var date = workDate.Date;
        var assignment = await _db.EmployeeShiftAssignments
            .Include(a => a.Shift)
            .Where(a => a.EmployeeId == employeeId
                        && a.EffectiveFrom <= date
                        && (a.EffectiveTo == null || a.EffectiveTo >= date))
            .OrderByDescending(a => a.IsPrimary)
            .ThenByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (assignment?.Shift is not null) return assignment.Shift;

        return await _db.Employees
            .Where(e => e.Id == employeeId)
            .Select(e => e.Shift)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// يحدد تاريخ العمل الذي تنتمي إليه بصمة زمنية، بناءً على نوافذ الوردية.
    /// يعالج انتقال التاريخ في الورديات الليلية.
    /// </summary>
    public static DateTime? ResolveWorkDate(DateTime punchTime, Shift shift, int earlyToleranceMinutes = 120, int overtimeToleranceMinutes = 240)
    {
        var sameDay = ShiftWindowResolver.Resolve(shift, punchTime.Date);
        if (punchTime >= sameDay.Start.AddMinutes(-earlyToleranceMinutes)
            && punchTime <= sameDay.End.AddMinutes(overtimeToleranceMinutes))
            return punchTime.Date;

        var prevDay = ShiftWindowResolver.Resolve(shift, punchTime.Date.AddDays(-1));
        if (punchTime >= prevDay.Start.AddMinutes(-earlyToleranceMinutes)
            && punchTime <= prevDay.End.AddMinutes(overtimeToleranceMinutes))
            return punchTime.Date.AddDays(-1);

        var nextDay = ShiftWindowResolver.Resolve(shift, punchTime.Date.AddDays(1));
        if (punchTime >= nextDay.Start.AddMinutes(-earlyToleranceMinutes)
            && punchTime <= nextDay.End.AddMinutes(overtimeToleranceMinutes))
            return punchTime.Date.AddDays(1);

        return null;
    }
}
