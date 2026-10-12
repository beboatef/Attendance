using HrPayroll.Domain.Entities;

namespace HrPayroll.Domain.ValueObjects;

/// <summary>نافذة زمنية للوردية (بداية ونهاية فعلية بحساب عبور منتصف الليل).</summary>
public sealed record ShiftWindow(DateTime Start, DateTime End)
{
    public int DurationMinutes => (int)Math.Round((End - Start).TotalMinutes);
    public DateTime Midpoint => Start.AddMinutes(DurationMinutes / 2.0);
    public bool Contains(DateTime t) => t >= Start && t <= End;
}

public static class ShiftWindowResolver
{
    /// <summary>
    /// يحسب بداية ونهاية الوردية لتاريخ عمل محدد.
    /// إذا كانت الوردية ليلية أو كان وقت النهاية أصغر/يساوي البداية، تنتقل النهاية لليوم التالي.
    /// </summary>
    public static ShiftWindow Resolve(Shift shift, DateTime workDate)
    {
        var start = workDate.Date + shift.StartTime;
        var end = workDate.Date + shift.EndTime;
        if (shift.IsOvernight || end <= start)
        {
            end = end.AddDays(1);
        }
        return new ShiftWindow(start, end);
    }
}
