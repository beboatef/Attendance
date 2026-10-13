using HrPayroll.Domain.ValueObjects;

namespace HrPayroll.Domain.Services;

/// <summary>
/// حسابات التأخير والانصراف المبكر والإضافي. منطق نقي لا يعتمد على قاعدة البيانات.
///
/// قواعد الشركة المطبقة:
/// - التأخير: فترة السماح عتبة وليست إعفاءً. من حضر داخل السماح لا تأخير عليه،
///   ومن تجاوزها يُحسب تأخيره كاملًا من وقت بداية الوردية.
/// - الإضافي: فترة السماح عتبة أيضًا. من خرج داخل سماح الخروج لا إضافي له،
///   ومن تجاوزها يُحسب إضافيه كاملًا من وقت نهاية الوردية الرسمية.
/// </summary>
public static class AttendanceCalculator
{
    /// <summary>
    /// يحسب دقائق التأخير للوردية.
    /// مثال (بداية 07:00 وسماح 30): 07:10 => 0 ، 07:25 => 0 ، 07:30 => 0 ، 07:35 => 35 ، 08:00 => 60.
    /// </summary>
    public static int CalculateLateMinutes(ShiftWindow window, int entryGraceMinutes, DateTime? checkIn)
    {
        if (checkIn is null) return 0;

        var graceEnd = window.Start.AddMinutes(Math.Max(0, entryGraceMinutes));
        if (checkIn.Value <= graceEnd) return 0;

        var minutes = (checkIn.Value - window.Start).TotalMinutes;
        return minutes <= 0 ? 0 : (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>يحسب دقائق الانصراف المبكر (لا تُنتج قيمًا سالبة).</summary>
    public static int CalculateEarlyLeaveMinutes(ShiftWindow window, DateTime? checkOut)
    {
        if (checkOut is null) return 0;
        var minutes = (window.End - checkOut.Value).TotalMinutes;
        return minutes <= 0 ? 0 : (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// يحسب دقائق الإضافي الخام.
    /// مثال (نهاية 15:00 وسماح خروج 30): 15:00 => 0 ، 15:15 => 0 ، 15:30 => 0 ، 15:35 => 35 ،
    /// 15:40 => 40 ، 16:00 => 60 ، 17:00 => 120.
    /// ويعمل بنفس المنطق للورديات الليلية (نهاية 07:00 وسماح 30 وخروج 08:00 => 60).
    /// </summary>
    public static int CalculateOvertimeMinutes(
        ShiftWindow window, int exitGraceMinutes, DateTime? checkOut, int minimumOvertimeMinutes = 1)
    {
        if (checkOut is null) return 0;

        var graceEnd = window.End.AddMinutes(Math.Max(0, exitGraceMinutes));
        if (checkOut.Value <= graceEnd) return 0;

        var minutes = (checkOut.Value - window.End).TotalMinutes;
        if (minutes <= 0) return 0;

        var rounded = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        return rounded < Math.Max(1, minimumOvertimeMinutes) ? 0 : rounded;
    }
}
