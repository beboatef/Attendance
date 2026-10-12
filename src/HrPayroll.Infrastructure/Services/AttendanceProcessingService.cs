using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using HrPayroll.Domain.ValueObjects;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrPayroll.Infrastructure.Services;

public sealed class ProcessingResult
{
    public int EmployeesProcessed { get; set; }
    public int DaysProcessed { get; set; }
    public int EventsCreated { get; set; }
    public int ReviewCases { get; set; }
}

/// <summary>
/// يحوّل البصمات الخام إلى حركات مصنفة وملخص حضور يومي.
/// </summary>
public class AttendanceProcessingService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly ILogger<AttendanceProcessingService> _logger;

    public AttendanceProcessingService(AppDbContext db, SettingsService settings, ILogger<AttendanceProcessingService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ProcessingResult> ProcessAsync(DateTime from, DateTime to, int? employeeId = null, string? user = null, CancellationToken ct = default)
    {
        from = from.Date;
        to = to.Date;
        var policy = await _settings.GetPolicyAsync(ct);
        var result = new ProcessingResult();

        var employeesQuery = _db.Employees.AsNoTracking().Where(e => e.IsActive);
        if (employeeId.HasValue) employeesQuery = employeesQuery.Where(e => e.Id == employeeId.Value);
        var employees = await employeesQuery.ToListAsync(ct);
        var empIds = employees.Select(e => e.Id).ToList();
        if (empIds.Count == 0) return result;

        // الورديات السارية
        var assignments = await _db.EmployeeShiftAssignments
            .Include(a => a.Shift)
            .Where(a => empIds.Contains(a.EmployeeId))
            .ToListAsync(ct);
        var shifts = await _db.Shifts.AsNoTracking().ToListAsync(ct);
        var shiftById = shifts.ToDictionary(s => s.Id);

        // الإجازات المعتمدة
        var leaves = await _db.LeaveRequests
            .Where(l => empIds.Contains(l.EmployeeId) && l.Status == ApprovalStatus.Approved
                        && l.StartDate <= to && l.EndDate >= from)
            .ToListAsync(ct);

        // البصمات (نوسّع النطاق يوماً قبل وبعد لمعالجة الورديات الليلية)
        var punchFrom = from.AddDays(-1);
        var punchTo = to.AddDays(2);
        var punches = await _db.RawPunches
            .Where(p => empIds.Contains(p.EmployeeId!.Value) && p.PunchTime >= punchFrom && p.PunchTime <= punchTo)
            .OrderBy(p => p.PunchTime)
            .ToListAsync(ct);

        // نطاق البيانات المستوردة لكل موظف (لتقرير الغياب)
        var spans = await _db.RawPunches
            .Where(p => empIds.Contains(p.EmployeeId!.Value))
            .GroupBy(p => p.EmployeeId!.Value)
            .Select(g => new { EmployeeId = g.Key, Min = g.Min(x => x.PunchTime), Max = g.Max(x => x.PunchTime) })
            .ToListAsync(ct);
        var spanByEmp = spans.ToDictionary(x => x.EmployeeId, x => (x.Min, x.Max));

        var restDays = await GetRestDaysAsync(ct);

        // الحضور اليومي الموجود
        var existingDaily = await _db.DailyAttendances
            .Where(d => empIds.Contains(d.EmployeeId) && d.WorkDate >= from && d.WorkDate <= to)
            .ToListAsync(ct);
        var dailyByKey = existingDaily.ToDictionary(d => (d.EmployeeId, d.WorkDate.Date));

        // الأحداث الموجودة ضمن النطاق
        var existingEvents = await _db.AttendanceEvents
            .Where(a => empIds.Contains(a.EmployeeId) && a.WorkDate >= from && a.WorkDate <= to)
            .ToListAsync(ct);

        var newEvents = new List<AttendanceEvent>();
        var dailyUpdates = new Dictionary<(int, DateTime), DailyAttendance>();

        foreach (var emp in employees)
        {
            var empAssignments = assignments.Where(a => a.EmployeeId == emp.Id).ToList();
            var empPunches = punches.Where(p => p.EmployeeId == emp.Id).ToList();
            var empLeaves = leaves.Where(l => l.EmployeeId == emp.Id).ToList();
            if (empPunches.Count == 0 && !spanByEmp.ContainsKey(emp.Id)) continue;

            result.EmployeesProcessed++;

            for (var date = from; date <= to; date = date.AddDays(1))
            {
                var shift = ResolveShift(emp, empAssignments, shiftById, date);
                if (shift is null) continue;

                var window = ShiftWindowResolver.Resolve(shift, date);

                var dayPunches = empPunches
                    .Where(p => ShiftResolver.ResolveWorkDate(p.PunchTime, shift, policy.EarlyArrivalToleranceMinutes, policy.OvertimeToleranceMinutes) == date)
                    .ToList();

                var leave = empLeaves.FirstOrDefault(l => l.StartDate.Date <= date && l.EndDate.Date >= date);

                DailyAttendance daily;
                dailyByKey.TryGetValue((emp.Id, date), out var existing);
                daily = existing ?? new DailyAttendance
                {
                    EmployeeId = emp.Id,
                    WorkDate = date,
                    CreatedBy = user,
                    CreatedAt = DateTime.UtcNow
                };

                daily.ShiftId = shift.Id;
                daily.ShiftStart = window.Start;
                daily.ShiftEnd = window.End;
                daily.IsManualOverride = existing?.IsManualOverride ?? false;

                if (dayPunches.Count == 0)
                {
                    daily.CheckInTime = null;
                    daily.CheckOutTime = null;
                    daily.LateMinutes = 0;
                    daily.EarlyLeaveMinutes = 0;
                    daily.CalculatedOvertimeMinutes = 0;
                    daily.PunchReviewStatus = ClassificationStatus.Classified;
                    daily.HasIncompletePunches = false;

                    if (leave is not null)
                    {
                        daily.Status = AttendanceStatus.Leave;
                        daily.Notes = "إجازة معتمدة";
                    }
                    else if (restDays.Contains(date.DayOfWeek))
                    {
                        daily.Status = AttendanceStatus.RestDay;
                        daily.Notes = "يوم راحة";
                    }
                    else if (spanByEmp.TryGetValue(emp.Id, out var span) && date >= span.Item1.Date && date <= span.Item2.Date)
                    {
                        daily.Status = AttendanceStatus.Absent;
                        daily.Notes = "لا توجد بصمات والبيانات مستوردة لهذا اليوم";
                    }
                    else
                    {
                        // لم تُستورد بيانات هذا اليوم بعد: لا نسجل غيابًا نهائيًا
                        if (existing is null) continue;
                        daily.Status = existing.Status;
                        daily.Notes = "بانتظار استيراد بيانات اليوم";
                    }
                }
                else
                {
                    var punchInputs = dayPunches
                        .Select(p => new RawPunchInput(p.Id, p.DeviceEmployeeCode, p.DeviceId, p.AttType, p.PunchTime, p.RawPunchTimeText, p.RowNumber))
                        .ToList();

                    var classification = PunchClassifier.Classify(punchInputs, shift, date, policy);

                    daily.CheckInTime = classification.CheckIn;
                    daily.CheckOutTime = classification.CheckOut;
                    daily.LateMinutes = classification.LateMinutes;
                    daily.EarlyLeaveMinutes = classification.EarlyLeaveMinutes;
                    daily.CalculatedOvertimeMinutes = classification.CalculatedOvertimeMinutes;
                    daily.ApprovedOvertimeMinutes = classification.CalculatedOvertimeMinutes;
                    daily.PunchReviewStatus = classification.Status;
                    daily.HasIncompletePunches = classification.HasIncompletePunches;
                    daily.Status = leave is not null && classification.CheckIn is null
                        ? AttendanceStatus.Leave
                        : classification.SuggestedStatus;
                    daily.Notes = classification.Reason;

                    if (classification.Status == ClassificationStatus.NeedsReview) result.ReviewCases++;

                    // إعادة بناء أحداث هذا اليوم (مع الحفاظ على الأحداث اليدوية)
                    var manualPunchIds = existingEvents
                        .Where(e => e.EmployeeId == emp.Id && e.WorkDate.Date == date && e.IsManual)
                        .Select(e => e.RawPunchId).ToHashSet();

                    foreach (var ev in classification.Events)
                    {
                        if (manualPunchIds.Contains(ev.Punch.Id)) continue;
                        newEvents.Add(new AttendanceEvent
                        {
                            EmployeeId = emp.Id,
                            RawPunchId = ev.Punch.Id,
                            WorkDate = date,
                            ShiftId = shift.Id,
                            EventType = ev.EventType,
                            EventTime = ev.Punch.PunchTime,
                            ClassificationStatus = ev.Status,
                            ClassificationSource = ev.Source,
                            Confidence = ev.Confidence,
                            IsManual = false,
                            Notes = ev.Note,
                            CreatedBy = user,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                daily.UpdatedAt = DateTime.UtcNow;
                daily.UpdatedBy = user;
                dailyUpdates[(emp.Id, date)] = daily;
                result.DaysProcessed++;
            }
        }

        // حذف الأحداث القديمة غير اليدوية للأيام التي نعيد معالجتها
        var keys = dailyUpdates.Keys.ToHashSet();
        var oldEvents = existingEvents
            .Where(e => !e.IsManual && keys.Contains((e.EmployeeId, e.WorkDate.Date))
                        && !newEvents.Any(n => n.RawPunchId == e.RawPunchId))
            .ToList();
        _db.AttendanceEvents.RemoveRange(oldEvents);

        foreach (var ev in newEvents) _db.AttendanceEvents.Add(ev);
        foreach (var kv in dailyUpdates)
        {
            if (!dailyByKey.ContainsKey(kv.Key)) _db.DailyAttendances.Add(kv.Value);
        }
        await _db.SaveChangesAsync(ct);
        result.EventsCreated = newEvents.Count;

        // ربط أحداث الدخول والخروج بملخص اليوم
        var eventByPunch = newEvents
            .Where(e => e.RawPunchId != 0)
            .GroupBy(e => e.RawPunchId)
            .ToDictionary(g => g.Key, g => g.First().Id);

        foreach (var kv in dailyUpdates)
        {
            var daily = kv.Value;
            var dayEvents = newEvents.Where(e => e.EmployeeId == daily.EmployeeId && e.WorkDate.Date == daily.WorkDate.Date).ToList();
            var entry = dayEvents.FirstOrDefault(e => e.EventType == AttendanceEventType.Entry && !e.IsManual);
            var exit = dayEvents.FirstOrDefault(e => e.EventType == AttendanceEventType.Exit && !e.IsManual);
            daily.EntryEventId = entry?.Id;
            daily.ExitEventId = exit?.Id;
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("معالجة الحضور: {Days} يوم، {Events} حدث", result.DaysProcessed, result.EventsCreated);
        return result;
    }

    private static Shift? ResolveShift(Employee emp, List<EmployeeShiftAssignment> assignments, Dictionary<int, Shift> shiftById, DateTime date)
    {
        var a = assignments
            .Where(x => x.EffectiveFrom.Date <= date && (x.EffectiveTo == null || x.EffectiveTo.Value.Date >= date))
            .OrderByDescending(x => x.IsPrimary)
            .ThenByDescending(x => x.EffectiveFrom)
            .FirstOrDefault();
        if (a is not null && shiftById.TryGetValue(a.ShiftId, out var s)) return s;
        if (emp.ShiftId.HasValue && shiftById.TryGetValue(emp.ShiftId.Value, out var es)) return es;
        return null;
    }

    private async Task<HashSet<DayOfWeek>> GetRestDaysAsync(CancellationToken ct)
    {
        var raw = await _settings.GetAsync(SettingKeys.WeeklyRestDays, ct);
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(raw)) { set.Add(DayOfWeek.Friday); return set; }
        foreach (var part in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out var d) && d is >= 0 and <= 6) set.Add((DayOfWeek)d);
        }
        return set;
    }
}
