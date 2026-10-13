using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

/// <summary>إدارة الورديات مع ضبط وقت النهاية تلقائيًا من وقت البداية + الساعات القياسية.</summary>
public class ShiftService
{
    private readonly AppDbContext _db;

    public ShiftService(AppDbContext db) => _db = db;

    /// <summary>يضبط وقت النهاية وسمة الوردية الليلية من وقت البداية والمدة القياسية.</summary>
    public static void ApplySchedule(Shift shift)
    {
        if (shift.StandardHours <= 0m) shift.StandardHours = 8m;
        shift.EndTime = shift.ComputeEndTime();
        shift.IsOvernight = shift.ComputeIsOvernight();
        if (shift.OvertimeMultiplier <= 0m) shift.OvertimeMultiplier = 1m;
        if (shift.EntryGraceMinutes < 0) shift.EntryGraceMinutes = 0;
        if (shift.ExitGraceMinutes < 0) shift.ExitGraceMinutes = 0;
    }

    public async Task<List<Shift>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default)
    {
        var q = _db.Shifts.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(s => s.IsActive);
        return await q.OrderBy(s => s.ShiftNumber).ThenBy(s => s.StartTime).ToListAsync(ct);
    }

    /// <summary>أنشئ الورديات الافتراضية الثلاث إن لم توجد أي وردية.</summary>
    public async Task EnsureDefaultShiftsAsync(CancellationToken ct = default)
    {
        if (await _db.Shifts.AnyAsync(ct)) return;

        var defaults = new List<Shift>
        {
            // الوردية الأولى: التكوين المعتمد رسميًا (07:00 حتى 15:00، سماح 30/30).
            new()
            {
                ShiftName = "الوردية الأولى", ShiftNumber = 1,
                StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(15, 0, 0),
                StandardHours = 8m, EntryGraceMinutes = 30, ExitGraceMinutes = 30,
                WorkingDays = "0,1,2,3,4,6", IsOvernight = false, OvertimeMultiplier = 1m, IsActive = true,
                Notes = "الوردية الصباحية الافتراضية 07:00 - 15:00"
            },
            // الورديتان الثانية والثالثة: مواعيدهما غير محددة من الشركة، لذلك تُترك افتراضية وقابلة للتعديل.
            new()
            {
                ShiftName = "الوردية الثانية", ShiftNumber = 2,
                StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(23, 0, 0),
                StandardHours = 8m, EntryGraceMinutes = 30, ExitGraceMinutes = 30,
                WorkingDays = "0,1,2,3,4,6", IsOvernight = false, OvertimeMultiplier = 1m, IsActive = true,
                Notes = "مواعيد مؤقتة — يجب على المسؤول ضبطها حسب سياسة الشركة"
            },
            new()
            {
                ShiftName = "الوردية الثالثة (ليلية)", ShiftNumber = 3,
                StartTime = new TimeSpan(23, 0, 0), EndTime = new TimeSpan(7, 0, 0),
                StandardHours = 8m, EntryGraceMinutes = 30, ExitGraceMinutes = 30,
                WorkingDays = "0,1,2,3,4,6", IsOvernight = true, OvertimeMultiplier = 1m, IsActive = true,
                Notes = "مواعيد مؤقتة — يجب على المسؤول ضبطها حسب سياسة الشركة"
            }
        };

        _db.Shifts.AddRange(defaults);
        await _db.SaveChangesAsync(ct);
    }
}
