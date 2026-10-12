using HrPayroll.Domain.ValueObjects;

namespace HrPayroll.Domain.Services;

/// <summary>
/// حسابات التأخير والانصراف المبكر والإضافي. منطق نقي لا يعتمد على قاعدة البيانات.
/// </summary>
public static class AttendanceCalculator
{
    /// <summary>
    /// يحسب دقائق التأخير للوردية.
    /// القاعدة: إذا حضر الموظف خلال فترة السماح يكون التأخير صفرًا،
    /// وإذا تجاوز فترة السماح يُلغى السماح بالكامل ويُحسب التأخير من بداية الوردية.
    /// مثال (بداية 07:00 وسماح 30): 07:15 => 0 ، 07:30 => 0 ، 07:31 => 31 ، 08:15 => 75.
    /// </summary>
    public static int CalculateLateMinutes(ShiftWindow window, int graceMinutes, DateTime? checkIn)
    {
        if (checkIn is null) return 0;

        var graceEnd = window.Start.AddMinutes(Math.Max(0, graceMinutes));
        if (checkIn.Value <= graceEnd) return 0;

        var minutes = (checkIn.Value - window.Start).TotalMinutes;
        return minutes <= 0 ? 0 : (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// يحسب دقائق الانصراف المبكر (لا تُنتج قيمًا سالبة).
    /// </summary>
    public static int CalculateEarlyLeaveMinutes(ShiftWindow window, DateTime? checkOut)
    {
        if (checkOut is null) return 0;
        var minutes = (window.End - checkOut.Value).TotalMinutes;
        return minutes <= 0 ? 0 : (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// يحسب دقائق الإضافي من وقت نهاية الوردية الرسمية (بدون خصم السماح، وبدون قيم سالبة).
    /// مثال: نهاية 15:00 وانصراف 16:00 => 60. وردية ليلية نهاية 07:00 وانصراف 08:00 => 60.
    /// </summary>
    public static int CalculateOvertimeMinutes(ShiftWindow window, DateTime? checkOut, int minimumOvertimeMinutes = 1)
    {
        if (checkOut is null) return 0;
        var minutes = (checkOut.Value - window.End).TotalMinutes;
        if (minutes <= 0) return 0;
        var rounded = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        return rounded < minimumOvertimeMinutes ? 0 : rounded;
    }
}
