using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.ValueObjects;

namespace HrPayroll.Domain.Services;

/// <summary>
/// يحوّل البصمات الخام إلى حركات دخول/خروج مصنفة.
///
/// ملاحظات مهمة:
/// - إذا أرسل الجهاز نوع حركة صريحًا (I/O, IN/OUT, دخول/خروج) يُستخدم بعد التحقق من معناه.
/// - إذا أرسل الجهاز نوع بصمة محايدًا مثل FP فلا يُفترض أن نوع البصمة يحدد الاتجاه.
/// - في هذه الحالة يُعتمد على الوردية وتوقيت العمل وتسلسل البصمات، وتُعلَّم الحالات الغامضة للمراجعة.
/// - بصمة دخول أو خروج مفقودة لا تُعتبر غيابًا مؤكدًا ولا إضافيًا معتمدًا.
/// </summary>
public static class PunchClassifier
{
    private static readonly string[] ExplicitInValues =
        { "i", "in", "0", "checkin", "check-in", "entry", "دخول", "حضور" };

    private static readonly string[] ExplicitOutValues =
        { "o", "out", "1", "checkout", "check-out", "exit", "خروج", "انصراف" };

    /// <summary>يحاول تحويل نوع الحركة القادم من الجهاز إلى اتجاه صريح، أو Unknown إذا كان محايدًا.</summary>
    public static PunchDirection ParseExplicitDirection(string? attType)
    {
        if (string.IsNullOrWhiteSpace(attType)) return PunchDirection.Unknown;
        var v = attType.Trim().ToLowerInvariant();

        // أنواع محايدة صريحة لا تحدد الاتجاه
        if (v is "fp" or "fingerprint" or "بصمة" or "normal" or "attend") return PunchDirection.Unknown;

        if (ExplicitInValues.Contains(v)) return PunchDirection.In;
        if (ExplicitOutValues.Contains(v)) return PunchDirection.Out;
        return PunchDirection.Unknown;
    }

    /// <summary>يصنّف بصمات موظف في يوم عمل محدد حسب ورديته.</summary>
    public static DayClassificationResult Classify(
        IReadOnlyList<RawPunchInput> punches,
        Shift shift,
        DateTime workDate,
        AttendancePolicy policy)
    {
        var window = ShiftWindowResolver.Resolve(shift, workDate);
        var entryGrace = Math.Max(0, shift.EntryGraceMinutes);
        var exitGrace = Math.Max(0, shift.ExitGraceMinutes);

        // إزالة البصمات المكررة (نفس الوقت) وترتيبها زمنيًا
        var relevant = punches
            .Where(p => p.PunchTime >= window.Start.AddMinutes(-policy.EarlyArrivalToleranceMinutes)
                        && p.PunchTime <= window.End.AddMinutes(policy.OvertimeToleranceMinutes))
            .GroupBy(p => p.PunchTime)
            .Select(g => g.OrderBy(x => x.Id).First())
            .OrderBy(p => p.PunchTime)
            .ThenBy(p => p.Id)
            .ToList();

        if (relevant.Count == 0)
        {
            return new DayClassificationResult
            {
                WorkDate = workDate,
                Status = ClassificationStatus.Classified,
                SuggestedStatus = AttendanceStatus.Absent,
                Reason = "لا توجد بصمات لهذا اليوم"
            };
        }

        // 1) محاولة الاعتماد على نوع الحركة الصريح القادم من الجهاز
        var withExplicit = relevant
            .Select(p => new { Punch = p, Dir = ParseExplicitDirection(p.AttType) })
            .Where(x => x.Dir != PunchDirection.Unknown)
            .ToList();

        if (withExplicit.Any(x => x.Dir == PunchDirection.In) && withExplicit.Any(x => x.Dir == PunchDirection.Out))
        {
            return BuildFromExplicit(
                withExplicit.Select(x => (x.Punch, x.Dir)).ToList(),
                window, workDate, policy, entryGrace, exitGrace);
        }

        // 2) التصنيف بالقواعد اعتمادًا على الوردية وتسلسل البصمات
        return BuildFromRules(relevant, window, workDate, policy, entryGrace, exitGrace);
    }

    private static DayClassificationResult BuildFromExplicit(
        List<(RawPunchInput Punch, PunchDirection Dir)> items,
        ShiftWindow window, DateTime workDate, AttendancePolicy policy, int entryGrace, int exitGrace)
    {
        var events = new List<ClassifiedEvent>();
        DateTime? entry = null, exit = null;
        int? entryId = null, exitId = null;
        var review = false;

        foreach (var (punch, dir) in items.OrderBy(x => x.Punch.PunchTime))
        {
            var type = dir == PunchDirection.In ? AttendanceEventType.Entry : AttendanceEventType.Exit;
            var inWindow = punch.PunchTime >= window.Start.AddMinutes(-policy.EarlyArrivalToleranceMinutes)
                           && punch.PunchTime <= window.End.AddMinutes(policy.OvertimeToleranceMinutes);
            var status = inWindow ? ClassificationStatus.Classified : ClassificationStatus.NeedsReview;
            if (!inWindow) review = true;

            events.Add(new ClassifiedEvent(punch, type, status, ClassificationSource.ExplicitDeviceType,
                inWindow ? 1.0m : 0.5m, inWindow ? null : "الحركة خارج نطاق الوردية"));

            if (type == AttendanceEventType.Entry && (entry == null || punch.PunchTime < entry.Value))
            {
                entry = punch.PunchTime; entryId = punch.Id;
            }
            if (type == AttendanceEventType.Exit && (exit == null || punch.PunchTime > exit.Value))
            {
                exit = punch.PunchTime; exitId = punch.Id;
            }
        }

        return Finalize(workDate, window, policy, events, entry, exit, entryId, exitId, review,
            "تصنيف حسب نوع الحركة الصريح من الجهاز", entryGrace, exitGrace);
    }

    private static DayClassificationResult BuildFromRules(
        List<RawPunchInput> punches, ShiftWindow window, DateTime workDate,
        AttendancePolicy policy, int entryGrace, int exitGrace)
    {
        var events = new List<ClassifiedEvent>();
        var entryZoneEnd = window.Start.AddMinutes(window.DurationMinutes * policy.EntryZoneRatio);
        var first = punches[0];
        var last = punches[^1];

        DateTime? entry = null, exit = null;
        int? entryId = null, exitId = null;
        var review = false;
        string reason;

        if (punches.Count == 1)
        {
            // بصمة واحدة: لا يمكن الجزم بالاتجاه
            if (first.PunchTime <= entryZoneEnd)
            {
                entry = first.PunchTime; entryId = first.Id;
                events.Add(new ClassifiedEvent(first, AttendanceEventType.Entry, ClassificationStatus.NeedsReview,
                    ClassificationSource.Rule, 0.4m, "بصمة واحدة فقط، يُرجّح أنها دخول بدون خروج"));
                reason = "بصمة واحدة: دخول بلا خروج";
            }
            else
            {
                exit = first.PunchTime; exitId = first.Id;
                events.Add(new ClassifiedEvent(first, AttendanceEventType.Exit, ClassificationStatus.NeedsReview,
                    ClassificationSource.Rule, 0.4m, "بصمة واحدة فقط، يُرجّح أنها خروج بدون دخول"));
                reason = "بصمة واحدة: خروج بلا دخول";
            }
            review = true;
        }
        else
        {
            var firstIsEntry = first.PunchTime <= entryZoneEnd;
            var lastIsExit = last.PunchTime >= entryZoneEnd;

            if (firstIsEntry && lastIsExit)
            {
                entry = first.PunchTime; entryId = first.Id;
                exit = last.PunchTime; exitId = last.Id;
                events.Add(new ClassifiedEvent(first, AttendanceEventType.Entry, ClassificationStatus.Classified,
                    ClassificationSource.Rule, 0.9m, null));
                events.Add(new ClassifiedEvent(last, AttendanceEventType.Exit, ClassificationStatus.Classified,
                    ClassificationSource.Rule, 0.9m, null));

                // البصمات الداخلية: تُقترح بالتناوب لكن تُعلَّم للمراجعة (قد تكون استراحة/خروج مؤقت)
                var alternation = AttendanceEventType.Exit;
                foreach (var mid in punches.Skip(1).Take(punches.Count - 2))
                {
                    events.Add(new ClassifiedEvent(mid, alternation, ClassificationStatus.NeedsReview,
                        ClassificationSource.Rule, 0.3m, "بصمة إضافية داخل الوردية تحتاج مراجعة"));
                    alternation = alternation == AttendanceEventType.Exit ? AttendanceEventType.Entry : AttendanceEventType.Exit;
                    review = true;
                }
                reason = "تصنيف آلي: أول بصمة دخول وآخر بصمة خروج بعد التحقق من نافذة الوردية";
            }
            else if (firstIsEntry && !lastIsExit)
            {
                entry = first.PunchTime; entryId = first.Id;
                events.Add(new ClassifiedEvent(first, AttendanceEventType.Entry, ClassificationStatus.NeedsReview,
                    ClassificationSource.Rule, 0.4m, "خروج مفقود"));
                foreach (var mid in punches.Skip(1))
                    events.Add(new ClassifiedEvent(mid, AttendanceEventType.Unknown, ClassificationStatus.NeedsReview,
                        ClassificationSource.Rule, 0.2m, "بصمة غير مصنفة"));
                review = true;
                reason = "بصمة خروج مفقودة: تحتاج مراجعة ولا تُعتبر غيابًا ولا إضافيًا معتمدًا";
            }
            else if (!firstIsEntry && lastIsExit)
            {
                exit = last.PunchTime; exitId = last.Id;
                events.Add(new ClassifiedEvent(last, AttendanceEventType.Exit, ClassificationStatus.NeedsReview,
                    ClassificationSource.Rule, 0.4m, "دخول مفقود"));
                foreach (var mid in punches.Take(punches.Count - 1))
                    events.Add(new ClassifiedEvent(mid, AttendanceEventType.Unknown, ClassificationStatus.NeedsReview,
                        ClassificationSource.Rule, 0.2m, "بصمة غير مصنفة"));
                review = true;
                reason = "بصمة دخول مفقودة: تحتاج مراجعة ولا تُعتبر غيابًا";
            }
            else
            {
                foreach (var p in punches)
                    events.Add(new ClassifiedEvent(p, AttendanceEventType.Unknown, ClassificationStatus.NeedsReview,
                        ClassificationSource.Rule, 0.1m, "تعذّر تحديد الاتجاه"));
                review = true;
                reason = "بصمات لا يمكن تصنيفها بثقة";
            }
        }

        return Finalize(workDate, window, policy, events, entry, exit, entryId, exitId, review, reason,
            entryGrace, exitGrace);
    }

    private static DayClassificationResult Finalize(
        DateTime workDate, ShiftWindow window, AttendancePolicy policy,
        List<ClassifiedEvent> events, DateTime? entry, DateTime? exit,
        int? entryId, int? exitId, bool review, string? reason, int entryGrace, int exitGrace)
    {
        var late = AttendanceCalculator.CalculateLateMinutes(window, entryGrace, entry);
        var early = AttendanceCalculator.CalculateEarlyLeaveMinutes(window, exit);
        var overtime = AttendanceCalculator.CalculateOvertimeMinutes(
            window, exitGrace, exit, policy.MinimumOvertimeMinutes);

        AttendanceStatus status;
        if (entry is not null && exit is not null)
        {
            if (late > 0) status = AttendanceStatus.Late;
            else if (early > 0) status = AttendanceStatus.EarlyDeparture;
            else status = AttendanceStatus.Present;
        }
        else if (entry is not null) status = AttendanceStatus.MissingCheckOut;
        else if (exit is not null) status = AttendanceStatus.MissingCheckIn;
        else status = AttendanceStatus.NeedsReview;

        if (review && entry is not null && exit is not null) status = AttendanceStatus.NeedsReview;

        return new DayClassificationResult
        {
            WorkDate = workDate,
            Events = events,
            CheckIn = entry,
            CheckOut = exit,
            EntryPunchId = entryId,
            ExitPunchId = exitId,
            Status = review ? ClassificationStatus.NeedsReview : ClassificationStatus.Classified,
            SuggestedStatus = status,
            HasIncompletePunches = entry is null || exit is null,
            Reason = reason,
            LateMinutes = late,
            EarlyLeaveMinutes = early,
            CalculatedOvertimeMinutes = overtime
        };
    }
}
