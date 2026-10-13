using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>المتطلبات 5 و6: كشف الوردية اليومية من البصمات وعدم إسناد وردية عشوائية عند الغموض.</summary>
public class ShiftDetectionTests
{
    private static AttendancePolicy Policy() => new()
    {
        WorkDaysPerMonth = 30, WorkHoursPerDay = 8,
        EntryGraceMinutes = 30, ExitGraceMinutes = 30
    };

    private static RawPunchInput Punch(int id, DateTime time, string? type = null)
        => new(id, "1001", "1", type, time, time.ToString("yyyy-MM-dd HH:mm:ss"), id);

    [Fact]
    public void Detects_First_Shift_From_Morning_Punches()
    {
        var day = TestDb.Monday; // 2024-10-07
        var punches = new[]
        {
            Punch(1, day.AddHours(6).AddMinutes(58)),
            Punch(2, day.AddHours(15).AddMinutes(2))
        };

        var result = ShiftDetector.Detect(punches, new[] { TestDb.FirstShift(), TestDb.SecondShift(), TestDb.NightShift() }, day, Policy());

        Assert.False(result.IsAmbiguous);
        Assert.NotNull(result.Shift);
        Assert.Equal("الوردية الأولى", result.Shift!.ShiftName);
        Assert.Equal(ShiftDetectionSource.Detected, result.Source);
    }

    [Fact]
    public void Detects_Second_Shift_From_Afternoon_Punches()
    {
        var day = TestDb.Monday;
        var punches = new[]
        {
            Punch(1, day.AddHours(14).AddMinutes(58)),
            Punch(2, day.AddHours(23).AddMinutes(2))
        };

        var result = ShiftDetector.Detect(punches, new[] { TestDb.FirstShift(), TestDb.SecondShift(), TestDb.NightShift() }, day, Policy());

        Assert.False(result.IsAmbiguous);
        Assert.Equal("الوردية الثانية", result.Shift!.ShiftName);
    }

    [Fact]
    public void Ambiguous_Punches_Are_Flagged_For_Review_Instead_Of_Guessing()
    {
        var day = TestDb.Monday;
        // بصمة واحدة في وقت تتقاطع فيه أكثر من وردية (قرب منتصف ورديتين)
        var shiftA = new Shift
        {
            Id = 10, ShiftName = "وردية أ", StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(16, 0, 0),
            StandardHours = 8m, EntryGraceMinutes = 30, ExitGraceMinutes = 30, IsActive = true
        };
        var shiftB = new Shift
        {
            Id = 11, ShiftName = "وردية ب", StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(17, 0, 0),
            StandardHours = 8m, EntryGraceMinutes = 30, ExitGraceMinutes = 30, IsActive = true
        };

        var punches = new[] { Punch(1, day.AddHours(10)), Punch(2, day.AddHours(16)) };
        var result = ShiftDetector.Detect(punches, new[] { shiftA, shiftB }, day, Policy());

        Assert.True(result.IsAmbiguous);
        Assert.Null(result.Shift);
        Assert.Equal(ShiftDetectionSource.Ambiguous, result.Source);
        Assert.Contains("تعادل", result.Reason);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Assigned_Shift_Wins_When_Punches_Are_Equally_Compatible()
    {
        var day = TestDb.Monday;
        var assigned = TestDb.FirstShift();
        var punches = new[]
        {
            Punch(1, day.AddHours(7)),
            Punch(2, day.AddHours(9)) // بصمتان مطابقتان للوردية الأولى والثانية أيضًا
        };

        var result = ShiftDetector.Detect(punches, new[] { TestDb.FirstShift(), TestDb.SecondShift() }, day, Policy(), assigned);

        Assert.False(result.IsAmbiguous);
        Assert.Equal(ShiftDetectionSource.Assigned, result.Source);
        Assert.Equal("الوردية الأولى", result.Shift!.ShiftName);
    }

    [Fact]
    public void No_Matching_Shift_Returns_None()
    {
        var day = TestDb.Friday; // يوم راحة للوردية الأولى
        var punches = new[] { Punch(1, day.AddHours(10)) };

        var result = ShiftDetector.Detect(punches, new[] { TestDb.FirstShift() }, day, Policy());

        Assert.True(result.IsNone);
        Assert.Null(result.Shift);
    }
}
