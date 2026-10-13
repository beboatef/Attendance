using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.ValueObjects;

namespace HrPayroll.Domain.Services;

/// <summary>مرشح وردية واحدة مع عدد البصمات المطابقة لها.</summary>
public sealed record ShiftCandidate(
    int ShiftId,
    string ShiftName,
    int MatchedPunches,
    bool HasEntry,
    bool HasExit,
    DateTime WindowStart,
    DateTime WindowEnd);

/// <summary>نتيجة تحديد وردية يوم عمل.</summary>
public sealed class ShiftDetectionResult
{
    public Shift? Shift { get; init; }
    public ShiftDetectionSource Source { get; init; } = ShiftDetectionSource.None;
    public bool IsAmbiguous { get; init; }
    public bool IsNone => Shift is null && !IsAmbiguous;
    public string? Reason { get; init; }
    public List<ShiftCandidate> Candidates { get; init; } = new();

    public static ShiftDetectionResult None(string reason) =>
        new() { Shift = null, Source = ShiftDetectionSource.None, Reason = reason };

    public static ShiftDetectionResult Ambiguous(string reason, List<ShiftCandidate> candidates) =>
        new() { Shift = null, Source = ShiftDetectionSource.Ambiguous, IsAmbiguous = true, Reason = reason, Candidates = candidates };
}

/// <summary>
/// يحدد الوردية الفعلية لليوم من البصمات الفعلية + الورديات المُعدّة،
/// بدلًا من إلزام كل موظف بوردية ثابتة.
/// إذا تعذّر الحسم أو تعددت الاحتمالات تُعلَّم الحالة للمراجعة بدل اختيار وردية عشوائية.
/// </summary>
public static class ShiftDetector
{
    /// <param name="punches">بصمات الموظف في نافذة زمنية واسعة حول التاريخ.</param>
    /// <param name="shifts">كل الورديات النشطة.</param>
    /// <param name="workDate">تاريخ العمل المطلوب.</param>
    /// <param name="assigned">الوردية المُسندة للموظف (إن وُجدت) كأفضلية عند التعادل.</param>
    /// <param name="ignoreWorkingDays">
    /// عند <c>true</c> تُتجاهل قيود "أيام عمل الوردية"، ويُستخدم هذا في محاولة ثانية
    /// للأيام غير العاملة (راحة أسبوعية/عطلة رسمية) حين يعمل الموظف فعلًا فيها.
    /// </param>
    public static ShiftDetectionResult Detect(
        IReadOnlyList<RawPunchInput> punches,
        IReadOnlyList<Shift> shifts,
        DateTime workDate,
        AttendancePolicy policy,
        Shift? assigned = null,
        bool ignoreWorkingDays = false)
    {
        workDate = workDate.Date;
        if (punches.Count == 0) return ShiftDetectionResult.None("لا توجد بصمات في هذا اليوم");

        var candidates = new List<ShiftCandidate>();

        foreach (var shift in shifts.Where(s => s.IsActive))
        {
            // الوردية تُطبَّق فقط في أيام عملها (إن حُددت)
            if (!ignoreWorkingDays && !shift.WorksOn(workDate.DayOfWeek)) continue;

            var window = ShiftWindowResolver.Resolve(shift, workDate);
            var from = window.Start.AddMinutes(-policy.EarlyArrivalToleranceMinutes);
            var to = window.End.AddMinutes(policy.OvertimeToleranceMinutes);

            var matched = punches.Where(p => p.PunchTime >= from && p.PunchTime <= to).ToList();
            if (matched.Count == 0) continue;

            var entryZoneEnd = window.Start.AddMinutes(window.DurationMinutes * policy.EntryZoneRatio);
            candidates.Add(new ShiftCandidate(
                shift.Id, shift.ShiftName, matched.Count,
                HasEntry: matched.Any(p => p.PunchTime <= entryZoneEnd),
                HasExit: matched.Any(p => p.PunchTime >= entryZoneEnd),
                WindowStart: window.Start,
                WindowEnd: window.End));
        }

        if (candidates.Count == 0)
            return ShiftDetectionResult.None("لا توجد وردية مُعدّة تطابق بصمات هذا اليوم");

        var best = candidates.Max(c => c.MatchedPunches);

        // الوردية المُسندة تُرجَّح عند التعادل أو عند فارق بصمة واحدة
        if (assigned is not null)
        {
            var assignedCandidate = candidates.FirstOrDefault(c => c.ShiftId == assigned.Id);
            if (assignedCandidate is not null && assignedCandidate.MatchedPunches >= best - 1)
            {
                return new ShiftDetectionResult
                {
                    Shift = assigned,
                    Source = ShiftDetectionSource.Assigned,
                    Reason = "وردية مُسندة للموظف ومطابقة للبصمات",
                    Candidates = candidates
                };
            }
        }

        var top = candidates.Where(c => c.MatchedPunches == best).ToList();

        if (top.Count > 1)
        {
            // تعادل بين ورديتين مستقلتين: غموض حقيقي يحتاج مراجعة بشرية
            return ShiftDetectionResult.Ambiguous(
                "تعادل في مطابقة البصمات بين أكثر من وردية: " +
                string.Join(" / ", top.Select(t => t.ShiftName)),
                candidates);
        }

        var winner = top[0];
        var shift2 = shifts.First(s => s.Id == winner.ShiftId);

        // إذا كانت أفضل وردية لا تحتوي على بصمة دخول ولا خروج فهي غير موثوقة
        if (!winner.HasEntry && !winner.HasExit)
            return ShiftDetectionResult.Ambiguous("البصمات تقع داخل نافذة الوردية لكن دون وضوح اتجاه", candidates);

        return new ShiftDetectionResult
        {
            Shift = shift2,
            Source = ShiftDetectionSource.Detected,
            Reason = $"وردية مُكتشفة من البصمات ({winner.MatchedPunches} بصمة)",
            Candidates = candidates
        };
    }
}
