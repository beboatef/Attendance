using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Services;
using HrPayroll.Domain.ValueObjects;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>الاختبارات 1-6: قواعد التأخير والإضافي للورديات النهارية والليلية.</summary>
public class CalculationTests
{
    private static ShiftWindow Window(Shift shift, DateTime date) => ShiftWindowResolver.Resolve(shift, date);
    private static readonly DateTime Day = new(2024, 10, 1);

    [Fact] // 1
    public void Late_At_0715_Is_Zero()
    {
        var s = TestDb.FirstShift();
        var w = Window(s, Day);
        Assert.Equal(0, AttendanceCalculator.CalculateLateMinutes(w, s.GraceMinutes, Day.AddHours(7).AddMinutes(15)));
    }

    [Fact] // 2
    public void Late_At_0730_Is_Zero()
    {
        var s = TestDb.FirstShift();
        var w = Window(s, Day);
        Assert.Equal(0, AttendanceCalculator.CalculateLateMinutes(w, s.GraceMinutes, Day.AddHours(7).AddMinutes(30)));
    }

    [Fact] // 3
    public void Late_At_0731_Is_31_Minutes()
    {
        var s = TestDb.FirstShift();
        var w = Window(s, Day);
        Assert.Equal(31, AttendanceCalculator.CalculateLateMinutes(w, s.GraceMinutes, Day.AddHours(7).AddMinutes(31)));
    }

    [Fact] // 4
    public void Late_At_0815_Is_75_Minutes()
    {
        var s = TestDb.FirstShift();
        var w = Window(s, Day);
        Assert.Equal(75, AttendanceCalculator.CalculateLateMinutes(w, s.GraceMinutes, Day.AddHours(8).AddMinutes(15)));
    }

    [Fact]
    public void Late_Is_Never_Negative_When_Arriving_Early()
    {
        var s = TestDb.FirstShift();
        var w = Window(s, Day);
        Assert.Equal(0, AttendanceCalculator.CalculateLateMinutes(w, s.GraceMinutes, Day.AddHours(6).AddMinutes(30)));
    }

    [Fact] // 5
    public void Overtime_DayShift_End1500_Exit1600_Is_60_Minutes()
    {
        var s = TestDb.FirstShift();
        var w = Window(s, Day);
        Assert.Equal(60, AttendanceCalculator.CalculateOvertimeMinutes(w, Day.AddHours(16)));
    }

    [Fact] // 6
    public void Overtime_NightShift_End0700_NextDay_Exit0800_Is_60_Minutes()
    {
        var s = TestDb.NightShift();
        var w = ShiftWindowResolver.Resolve(s, Day); // 2024-10-01 23:00 -> 2024-10-02 07:00
        Assert.Equal(Day.AddHours(23), w.Start);
        Assert.Equal(Day.AddDays(1).AddHours(7), w.End);
        Assert.Equal(60, AttendanceCalculator.CalculateOvertimeMinutes(w, Day.AddDays(1).AddHours(8)));
    }

    [Fact]
    public void Overtime_Is_Zero_When_Leaving_Before_Shift_End()
    {
        var s = TestDb.FirstShift();
        var w = Window(s, Day);
        Assert.Equal(0, AttendanceCalculator.CalculateOvertimeMinutes(w, Day.AddHours(14).AddMinutes(30)));
    }

    [Fact]
    public void ShiftWindow_Overnight_Moves_End_To_Next_Day()
    {
        var w = ShiftWindowResolver.Resolve(TestDb.NightShift(), Day);
        Assert.Equal(Day.AddHours(23), w.Start);
        Assert.Equal(Day.AddDays(1).AddHours(7), w.End);
        Assert.Equal(480, w.DurationMinutes);
    }
}
