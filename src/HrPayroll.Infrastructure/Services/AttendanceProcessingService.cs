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
    public int AmbiguousShiftDays { get; set; }
    public int AbsentDays { get; set; }
    public int LateDays { get; set; }
    public int OvertimeDays { get; set; }
    public int RestDays { get; set; }
    public int HolidayDays { get; set; }
    public int LeaveDays { get; set; }
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// يحوّل البصمات الخام إلى حركات مصنفة وملخص حضور يومي،
/// مع كشف الوردية من البصمات الفعلية وتصنيف أيام الراحة والعطلات والإجازات.
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

    public async Task<ProcessingResult> ProcessAsync(
        DateTime from, DateTime to, int? employeeId = null, string? user = null, CancellationToken ct = default)
    {
        from = from.Date;
        to = to.Date;
        if (to < from) (from, to) = (to, from);

        var policy = await _settings.GetPolicyAsync(ct);
        var restDays = await _settings.GetWeeklyRestDaysAsync(ct);
        var result = new ProcessingResult();

        var employeesQuery = _db.Employees.AsNoTracking();
        if (employeeId.HasValue) employeesQuery = employeesQuery.Where(e => e.Id == employeeId.Value);
        else employeesQuery = employeesQuery.Where(e => e.IsActive);
        var employees = await employeesQuery.ToListAsync(ct);
        var empIds = employees.Select(e => e.Id).ToList();
        if (empIds.Count == 0) return result;

        // كل الورديات النشطة (للانتقاء/الكشف)
        var allShifts = await _db.Shifts.AsNoTracking().ToListAsync(ct);
        var activeShifts = allShifts.Where(s => s.IsActive).ToList();
        var shiftById = allShifts.ToDictionary(s => s.Id);

        var assignments = await _db.EmployeeShiftAssignments
            .Where(a => empIds.Contains(a.EmployeeId))
            .ToListAsync(ct);

        // الإجازات المعتمدة
        var leaves = await _db.LeaveRequests
            .Where(l => empIds.Contains(l.EmployeeId) && l.Status == ApprovalStatus.Approved
                        && l.StartDate <= to.AddDays(1) && l.EndDate >= from.AddDays(-1))
            .ToListAsync(ct);

        // العطلات الرسمية
        var holidays = await _db.PublicHolidays
            .Where(h => h.IsActive && h.HolidayDate >= from.AddDays(-1) && h.HolidayDate <= to.AddDays(1))
            .ToListAsync(ct);
        var holidayByDate = holidays
            .GroupBy(h => h.HolidayDate.Date)
            .ToDictionary(g => g.Key, g => g.First());

        // البصمات (نوسّع النطاق يومين قبل وبعد لمعالجة الورديات الليلية)
        var punchFrom = from.AddDays(-1);
        var punchTo = to.AddDays(2);
        var punches = await _db.RawPunches
            .Where(p => p.EmployeeId != null && empIds.Contains(p.EmployeeId.Value)
                        && p.PunchTime >= punchFrom && p.PunchTime <= punchTo)
            .OrderBy(p => p.PunchTime)
            .ToListAsync(ct);

        // نطاق البيانات المستوردة لكل موظف (لتقرير الغياب)
        var spans = await _db.RawPunches
            .Where(p => p.EmployeeId != null && empIds.Contains(p.EmployeeId.Value))
            .GroupBy(p => p.EmployeeId!.Value)
            .Select(g => new { EmployeeId = g.Key, Min = g.Min(x => x.PunchTime), Max = g.Max(x => x.PunchTime) })
            .ToListAsync(ct);
        var spanByEmp = spans.ToDictionary(x => x.EmployeeId, x => (x.Min, x.Max));

        var existingDaily = await _db.DailyAttendances
            .Where(d => empIds.Contains(d.EmployeeId) && d.WorkDate >= from && d.WorkDate <= to)
            .ToListAsync(ct);
        var dailyByKey = existingDaily.ToDictionary(d => (d.EmployeeId, d.WorkDate.Date));

        var existingEvents = await _db.AttendanceEvents
            .Where(a => empIds.Contains(a.EmployeeId) && a.WorkDate >= from && a.WorkDate <= to)
            .ToListAsync(ct);

        var newEvents = new List<AttendanceEvent>();
        var updatedEvents = new List<AttendanceEvent>();
        var dailyUpdates = new Dictionary<(int, DateTime), DailyAttendance>();

        foreach (var emp in employees)
        {
            var empAssignments = assignments.Where(a => a.EmployeeId == emp.Id).ToList();
            var empPunches = punches.Where(p => p.EmployeeId == emp.Id).ToList();
            var empLeaves = leaves.Where(l => l.EmployeeId == emp.Id).ToList();
            if (empPunches.Count == 0 && !spanByEmp.ContainsKey(emp.Id)) continue;

            var empPunchInputs = empPunches
                .Select(p => new RawPunchInput(p.Id, p.DeviceEmployeeCode, p.DeviceId, p.AttType, p.PunchTime, p.RawPunchTimeText, p.RowNumber))
                .ToList();

            // إسناد كل بصمة إلى يوم عمل واحد فقط (تغطي الورديات الليلية وتنقل البصمة ليوم واحد)
            var assignedShiftCache = new Dictionary<DateTime, int?>();
            int? AssignedShiftIdFor(DateTime d)
            {
                d = d.Date;
                if (!assignedShiftCache.TryGetValue(d, out var id))
                {
                    id = ResolveAssignedShift(emp, empAssignments, shiftById, d)?.Id;
                    assignedShiftCache[d] = id;
                }
                return id;
            }

            var workDates = PunchWorkDateResolver.Assign(empPunchInputs, activeShifts, policy, AssignedShiftIdFor);
            var punchesByDate = empPunchInputs
                .Where(i => workDates.ContainsKey(i.Id))
                .GroupBy(i => workDates[i.Id].WorkDate.Date)
                .ToDictionary(g => g.Key, g => g.ToList());
            var ambiguousPunchIds = workDates.Where(kv => kv.Value.IsAmbiguous)
                .Select(kv => kv.Key).ToHashSet();
            if (ambiguousPunchIds.Count > 0)
                result.Warnings.Add(
                    $"الموظف {emp.EmployeeCode}: {ambiguousPunchIds.Count} بصمة يصعب حسم ورديتها/يومها — تتطلب مراجعة.");

            Shift? previousDayShift = null;
            DateTime? previousDayDate = null;

            result.EmployeesProcessed++;

            for (var date = from; date <= to; date = date.AddDays(1))
            {
                // لا حضور قبل تاريخ التعيين ولا بعد تاريخ إنهاء الخدمة
                if (date.Date < emp.HireDate.Date) continue;
                if (emp.TerminationDate.HasValue && date.Date > emp.TerminationDate.Value.Date) continue;

                var assignedShift = ResolveAssignedShift(emp, empAssignments, shiftById, date);
                // بصمات هذا اليوم فقط حسب الإسناد الموحّد (كل بصمة ليوم عمل واحد)
                var punchInputs = punchesByDate.TryGetValue(date.Date, out var forDate)
                    ? forDate
                    : new List<RawPunchInput>();

                // حقائق اليوم: إجازة معتمدة / عطلة رسمية / يوم راحة أسبوعية
                var leave = empLeaves.FirstOrDefault(l => l.StartDate.Date <= date && l.EndDate.Date >= date);
                holidayByDate.TryGetValue(date, out var holiday);
                var isRestDay = restDays.Contains(date.DayOfWeek);
                var isHoliday = holiday is not null;
                var isNonWorkingDay = isRestDay || isHoliday;

                // ===== تحديد الوردية: من الإسناد أو بالكشف من البصمات =====
                Shift? shift = null;
                var detectionSource = ShiftDetectionSource.None;
                string? detectionReason = null;

                if (punchInputs.Count == 0)
                {
                    shift = assignedShift;
                    if (shift is not null) detectionSource = ShiftDetectionSource.Assigned;
                }
                else
                {
                    // البصمات التي تنتمي فعليًا لهذا التاريخ حسب الورديات المُعدّة
                    var detection = ShiftDetector.Detect(punchInputs, activeShifts, date, policy, assignedShift);
                    if (detection.IsAmbiguous)
                    {
                        // غموض حقيقي: لا نُسند وردية عشوائية
                        if (assignedShift is { } fallback)
                        {
                            shift = fallback;
                            detectionSource = ShiftDetectionSource.Ambiguous;
                            detectionReason = detection.Reason + " — تم استخدام الوردية المُسندة مؤقتًا لحين المراجعة";
                        }
                        else
                        {
                            shift = null;
                            detectionSource = ShiftDetectionSource.Ambiguous;
                            detectionReason = detection.Reason;
                        }
                        result.AmbiguousShiftDays++;
                    }
                    else if (detection.Shift is not null)
                    {
                        shift = detection.Shift;
                        detectionSource = detection.Source != ShiftDetectionSource.None
                            ? detection.Source
                            : ShiftDetectionSource.Assigned;
                        detectionReason = detection.Reason;
                    }
                    else
                    {
                        // محاولة ثانية للأيام غير العاملة (راحة أسبوعية/عطلة رسمية/إجازة):
                        // نتجاهل قيد "أيام عمل الوردية" لالتقاط من عمل فعلًا في ذلك اليوم.
                        if (isNonWorkingDay || leave is not null)
                        {
                            var relaxed = ShiftDetector.Detect(
                                punchInputs, activeShifts, date, policy, assignedShift, ignoreWorkingDays: true);
                            if (relaxed.Shift is not null)
                            {
                                shift = relaxed.Shift;
                                detectionSource = relaxed.Source;
                                detectionReason = relaxed.Reason;
                            }
                        }

                        detectionReason ??= detection.Reason;
                    }
                }

                // لا وردية مُعدّة مؤكدة لهذا اليوم: لا نختار وردية عشوائية.
                // نستخدم وردية مرجعية (المُسندة ثم وردية الموظف ثم أول وردية) لتحديد نافذة اليوم فقط،
                // ويبقى اليوم مُعلَّمًا للمراجعة لأنها ليست وردية مُكتشفة من البصمات.
                if (shift is null)
                {
                    shift = assignedShift
                            ?? (emp.ShiftId.HasValue && shiftById.TryGetValue(emp.ShiftId.Value, out var own) ? own : null)
                            ?? activeShifts.OrderBy(s => s.ShiftNumber).ThenBy(s => s.Id).FirstOrDefault();

                    if (shift is not null)
                    {
                        detectionSource = ShiftDetectionSource.None;
                        detectionReason ??= "لم يتم العثور على وردية مطابقة للبصمات — استُخدمت وردية مرجعية";
                    }
                }

                if (shift is null)
                {
                    // لا توجد أي وردية مُعدّة في النظام: لا يمكن الاحتساب أو التصنيف
                    if (empPunchInputs.Count > 0)
                    {
                        result.ReviewCases++;
                        result.Warnings.Add($"الموظف {emp.EmployeeCode}: بصمات في {date:yyyy-MM-dd} بدون أي وردية مُعدّة.");
                    }
                    continue;
                }

                var window = ShiftWindowResolver.Resolve(shift, date);
                previousDayShift = shift;
                previousDayDate = date;

                // بصمات هذا اليوم هي نفسها المُسندة إليه (لا تُحتسب أي بصمة في يومين)
                var workDateInputs = punchInputs;

                dailyByKey.TryGetValue((emp.Id, date), out var existing);
                var daily = existing ?? new DailyAttendance
                {
                    EmployeeId = emp.Id,
                    WorkDate = date,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = user
                };

                var wasManual = existing?.IsManualOverride ?? false;

                var dayIsAmbiguous = workDateInputs.Any(i => ambiguousPunchIds.Contains(i.Id));
                if (dayIsAmbiguous)
                {
                    detectionReason = string.Join(" | ", new[]
                    {
                        detectionReason,
                        "بصمة محتملة لأكثر من وردية/يوم: تم الإسناد وفق القاعدة الافتراضية وتحتاج مراجعة"
                    }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    result.ReviewCases++;
                }

                // امتداد وردية ليلية بدأت في اليوم السابق: اليوم الحالي ليس يوم عمل مستقلًا
                var isOvernightContinuation = workDateInputs.Count == 0
                    && previousDayShift is { IsOvernight: true }
                    && previousDayDate.HasValue
                    && ShiftWindowResolver.Resolve(previousDayShift, previousDayDate.Value).End > date;

                daily.ShiftId = shift.Id;
                daily.ShiftStart = window.Start;
                daily.ShiftEnd = window.End;
                daily.DetectionSource = detectionSource;
                daily.NeedsReviewReason = detectionReason;
                daily.IsWeeklyRestDay = isRestDay;
                daily.IsHoliday = isHoliday;
                daily.HolidayId = holiday?.Id;
                daily.IsManualOverride = wasManual;

                if (workDateInputs.Count == 0)
                {
                    // لا بصمات: الراحة أو العطلة أو الإجازة أو الغياب
                    daily.CheckInTime = null;
                    daily.CheckOutTime = null;
                    daily.LateMinutes = 0;
                    daily.EarlyLeaveMinutes = 0;
                    daily.RawOvertimeMinutes = 0;
                    daily.ApprovedOvertimeMinutes = 0;
                    daily.PaidOvertimeMinutes = 0;
                    daily.PunchReviewStatus = ClassificationStatus.Classified;
                    daily.HasIncompletePunches = false;
                    daily.LeaveRequestId = null;

                    if (leave is not null)
                    {
                        ApplyLeave(daily, leave);
                        result.LeaveDays++;
                    }
                    else if (isHoliday)
                    {
                        daily.Status = AttendanceStatus.Holiday;
                        daily.Notes = $"عطلة رسمية: {holiday!.Name}";
                        result.HolidayDays++;
                    }
                    else if (isRestDay)
                    {
                        daily.Status = AttendanceStatus.RestDay;
                        daily.Notes = "يوم راحة أسبوعية";
                        result.RestDays++;
                    }
                    else if (isOvernightContinuation)
                    {
                        // متابعة وردية ليلية: لا تُحتسب غيابًا ولا يوم عمل جديد
                        if (existing is null) continue;
                        daily.Status = existing.Status;
                        daily.Notes = "امتداد وردية ليلية بدأت في اليوم السابق";
                    }
                    else if (spanByEmp.TryGetValue(emp.Id, out var span) && date >= span.Item1.Date && date <= span.Item2.Date)
                    {
                        daily.Status = AttendanceStatus.Absent;
                        daily.Notes = "لا توجد بصمات والبيانات مستوردة لهذا اليوم";
                        result.AbsentDays++;
                    }
                    else
                    {
                        // لم تُستورد بيانات هذا اليوم بعد: لا نسجل غيابًا نهائيًا
                        if (existing is null) continue;
                        daily.Status = existing.Status;
                        daily.Notes = "بانتظار استيراد بيانات هذا اليوم";
                    }
                }
                else
                {
                    var classification = PunchClassifier.Classify(workDateInputs, shift, date, policy);

                    if (wasManual)
                    {
                        // يوم يحمل تصحيحًا يدويًا معتمدًا: لا نستبدل القيم المصحّحة بالبصمات الخام.
                        // البصمات الخام تبقى محفوظة، والتصحيح يبقى نافذًا مع تسجيل ملاحظة المراجعة.
                        daily.NeedsReviewReason = string.Join(" | ", new[]
                        {
                            daily.NeedsReviewReason,
                            "يوجد تصحيح يدوي معتمد محفوظ على هذا اليوم ولم تُستبدل قيمه بالبصمات الخام"
                        }.Where(x => !string.IsNullOrWhiteSpace(x)));

                        if (leave is not null) daily.LeaveRequestId = leave.Id;
                        if (classification.Status == ClassificationStatus.NeedsReview) result.ReviewCases++;
                    }
                    else
                    {

                        daily.CheckInTime = classification.CheckIn;
                        daily.CheckOutTime = classification.CheckOut;
                        daily.LateMinutes = classification.LateMinutes;
                        daily.EarlyLeaveMinutes = classification.EarlyLeaveMinutes;
                        daily.RawOvertimeMinutes = classification.CalculatedOvertimeMinutes;
                        // الإضافي المعتمد يُترك للمراجعة: لا يُعتمد تلقائيًا لكون الموظف خرج متأخرًا
                        daily.ApprovedOvertimeMinutes = existing?.ApprovedOvertimeMinutes ?? 0;
                        daily.PaidOvertimeMinutes = 0;
                        daily.PunchReviewStatus = classification.Status;
                        daily.HasIncompletePunches = classification.HasIncompletePunches;
                        daily.Status = classification.SuggestedStatus;
                        daily.Notes = classification.Reason;
                        daily.LeaveRequestId = null;

                        if (isHoliday || isRestDay)
                        {
                            daily.NeedsReviewReason = (isHoliday
                                ? $"عمل في عطلة رسمية ({holiday!.Name})"
                                : "عمل في يوم راحة أسبوعية")
                                + ": يحتاج اعتماد صريح للصرف وفق سياسة منفصلة";
                            if (daily.PunchReviewStatus == ClassificationStatus.Classified)
                            {
                                // نُبقي حالة الحضور الفعلية لكن نرفع الحالة للمراجعة للصرف
                                result.ReviewCases++;
                            }
                        }

                        if (leave is not null && classification.CheckIn is null)
                        {
                            ApplyLeave(daily, leave);
                        }
                        else if (leave is not null)
                        {
                            daily.LeaveRequestId = leave.Id;
                            daily.Notes = (daily.Notes ?? string.Empty) + " | إجازة معتمدة مع بصمات فعلية";
                        }

                        if (classification.Status == ClassificationStatus.NeedsReview) result.ReviewCases++;
                        if (classification.LateMinutes > 0) result.LateDays++;
                        if (classification.CalculatedOvertimeMinutes > 0) result.OvertimeDays++;

                        // إعادة بناء أحداث هذا اليوم (مع الحفاظ على الأحداث اليدوية)
                        var manualPunchIds = existingEvents
                            .Where(e => e.EmployeeId == emp.Id && e.WorkDate.Date == date && e.IsManual)
                            .Select(e => e.RawPunchId).ToHashSet();

                        foreach (var ev in classification.Events)
                        {
                            if (manualPunchIds.Contains(ev.Punch.Id)) continue;

                            // كل بصمة خام لها حدث واحد على الأكثر (علاقة 1:1)،
                            // لذلك نُحدِّث الحدث القائم في مكانه بدل حذفه وإنشاء حدث جديد
                            // حتى لا يتعارض الحذف والإضافة في نفس الدفعة ولا تتغير معرفات الأحداث.
                            var tracked = existingEvents.FirstOrDefault(e => e.RawPunchId == ev.Punch.Id && !e.IsManual);
                            if (tracked is not null)
                            {
                                tracked.EmployeeId = emp.Id;
                                tracked.WorkDate = date;
                                tracked.ShiftId = shift.Id;
                                tracked.EventType = ev.EventType;
                                tracked.EventTime = ev.Punch.PunchTime;
                                tracked.ClassificationStatus = ev.Status;
                                tracked.ClassificationSource = ev.Source;
                                tracked.Confidence = ev.Confidence;
                                tracked.Notes = ev.Note;
                                tracked.UpdatedBy = user;
                                tracked.UpdatedAt = DateTime.UtcNow;
                                updatedEvents.Add(tracked);
                                continue;
                            }

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
                }

                daily.UpdatedAt = DateTime.UtcNow;
                daily.UpdatedBy = user;
                dailyUpdates[(emp.Id, date)] = daily;
                result.DaysProcessed++;
            }
        }

        // حذف الأحداث القديمة غير اليدوية للأيام التي نعيد معالجتها ولم تعد بصماتها ضمن اليوم
        var keys = dailyUpdates.Keys.ToHashSet();
        var keptPunchIds = newEvents.Select(n => n.RawPunchId)
            .Concat(updatedEvents.Select(e => e.RawPunchId))
            .ToHashSet();
        var oldEvents = existingEvents
            .Where(e => !e.IsManual
                        && !updatedEvents.Contains(e)
                        && keys.Contains((e.EmployeeId, e.WorkDate.Date))
                        && !keptPunchIds.Contains(e.RawPunchId))
            .ToList();
        _db.AttendanceEvents.RemoveRange(oldEvents);

        foreach (var ev in newEvents) _db.AttendanceEvents.Add(ev);
        foreach (var kv in dailyUpdates)
            if (!dailyByKey.ContainsKey(kv.Key)) _db.DailyAttendances.Add(kv.Value);

        await _db.SaveChangesAsync(ct);
        result.EventsCreated = newEvents.Count + updatedEvents.Count;

        // ربط أحداث الدخول والخروج بملخص اليوم (مع الإبقاء على الأحداث اليدوية)
        foreach (var kv in dailyUpdates)
        {
            var daily = kv.Value;
            if (daily.IsManualOverride) continue;
            var dayEvents = newEvents.Concat(updatedEvents)
                .Where(e => e.EmployeeId == daily.EmployeeId && e.WorkDate.Date == daily.WorkDate.Date && !e.IsManual)
                .ToList();
            daily.EntryEventId = dayEvents.FirstOrDefault(e => e.EventType == AttendanceEventType.Entry)?.Id;
            daily.ExitEventId = dayEvents.FirstOrDefault(e => e.EventType == AttendanceEventType.Exit)?.Id;
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "معالجة الحضور: {Days} يوم، {Events} حدث، {Review} حالة مراجعة",
            result.DaysProcessed, result.EventsCreated, result.ReviewCases);
        return result;
    }

    private static void ApplyLeave(DailyAttendance daily, LeaveRequest leave)
    {
        daily.LeaveRequestId = leave.Id;
        if (leave.LeaveType == LeaveType.Unpaid || !leave.IsPaid)
        {
            daily.Status = AttendanceStatus.UnpaidLeave;
            daily.Notes = "إجازة بدون أجر معتمدة";
        }
        else if (leave.LeaveType == LeaveType.Sick)
        {
            daily.Status = AttendanceStatus.PaidLeave;
            daily.Notes = "إجازة مرضية مدفوعة";
        }
        else if (leave.LeaveType == LeaveType.Annual)
        {
            daily.Status = AttendanceStatus.PaidLeave;
            daily.Notes = "إجازة سنوية مدفوعة";
        }
        else
        {
            daily.Status = AttendanceStatus.PaidLeave;
            daily.Notes = $"إجازة معتمدة ({leave.LeaveType})";
        }
    }

    /// <summary>الوردية المُسندة للموظف في تاريخ معين (إسناد زمني ثم الوردية الأساسية).</summary>
    private static Shift? ResolveAssignedShift(
        Employee emp, List<EmployeeShiftAssignment> assignments, Dictionary<int, Shift> shiftById, DateTime date)
    {
        var a = assignments
            .Where(x => x.EffectiveFrom.Date <= date && (x.EffectiveTo == null || x.EffectiveTo.Value.Date >= date))
            .OrderByDescending(x => x.IsPrimary)
            .ThenByDescending(x => x.EffectiveFrom)
            .FirstOrDefault();

        if (a is not null && shiftById.TryGetValue(a.ShiftId, out var s) && s.IsActive) return s;
        if (emp.ShiftId.HasValue && shiftById.TryGetValue(emp.ShiftId.Value, out var es) && es.IsActive) return es;
        return null;
    }
}
