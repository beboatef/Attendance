using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>المتطلبات 13-15: تصنيف البصمات، البصمات الناقصة، والورديات الليلية.</summary>
public class ClassificationTests
{
    private static AttendancePolicy Policy() => new()
    {
        WorkDaysPerMonth = 30,
        WorkHoursPerDay = 8,
        EntryGraceMinutes = 30,
        ExitGraceMinutes = 30,
        MinimumOvertimeMinutes = 1
    };

    private static RawPunchInput Punch(int id, DateTime time, string? type = null)
        => new(id, "1001", "1", type, time, time.ToString("yyyy-MM-dd HH:mm:ss"), id);

    // ===================== 14) بصمة دخول مفقودة =====================

    [Fact] // 14
    public void Missing_Entry_Punch_Is_Flagged_Not_Absent_And_Not_Overtime()
    {
        var day = TestDb.Monday;
        // بصمة خروج فقط في آخر الوردية
        var punches = new[] { Punch(1, day.AddHours(15).AddMinutes(40)) };

        var result = PunchClassifier.Classify(punches, TestDb.FirstShift(), day, Policy());

        Assert.True(result.HasIncompletePunches);
        Assert.Equal(ClassificationStatus.NeedsReview, result.Status);
        Assert.Null(result.CheckIn);
        Assert.NotNull(result.CheckOut);
        Assert.Equal(AttendanceStatus.MissingCheckIn, result.SuggestedStatus);
        Assert.NotEqual(AttendanceStatus.Absent, result.SuggestedStatus);
        Assert.Equal(0, result.LateMinutes);
    }

    // ===================== 15) بصمة خروج مفقودة =====================

    [Fact] // 15
    public void Missing_Exit_Punch_Is_Flagged_Not_Absent()
    {
        var day = TestDb.Monday;
        var punches = new[] { Punch(1, day.AddHours(6).AddMinutes(50)) };

        var result = PunchClassifier.Classify(punches, TestDb.FirstShift(), day, Policy());

        Assert.True(result.HasIncompletePunches);
        Assert.Equal(ClassificationStatus.NeedsReview, result.Status);
        Assert.NotNull(result.CheckIn);
        Assert.Null(result.CheckOut);
        Assert.Equal(AttendanceStatus.MissingCheckOut, result.SuggestedStatus);
        Assert.Equal(0, result.CalculatedOvertimeMinutes);
    }

    [Fact]
    public void No_Punches_Means_Absent_Candidate()
    {
        var result = PunchClassifier.Classify(Array.Empty<RawPunchInput>(), TestDb.FirstShift(), TestDb.Monday, Policy());
        Assert.Equal(AttendanceStatus.Absent, result.SuggestedStatus);
        Assert.False(result.HasIncompletePunches);
    }

    [Fact]
    public void Full_Day_Punches_Are_Classified_With_Late_And_Overtime()
    {
        var day = TestDb.Monday;
        var punches = new[]
        {
            Punch(1, day.AddHours(7).AddMinutes(35)),   // دخول متأخر (35)
            Punch(2, day.AddHours(16))                  // خروج (60 دقيقة إضافي)
        };

        var result = PunchClassifier.Classify(punches, TestDb.FirstShift(), day, Policy());

        Assert.Equal(AttendanceStatus.Late, result.SuggestedStatus);
        Assert.Equal(35, result.LateMinutes);
        Assert.Equal(60, result.CalculatedOvertimeMinutes);
        Assert.Equal(day.AddHours(7).AddMinutes(35), result.CheckIn);
        Assert.Equal(day.AddHours(16), result.CheckOut);
    }

    [Fact]
    public void Explicit_Device_Punch_Types_Are_Honoured()
    {
        var day = TestDb.Monday;
        // بصمات خارج ترتيب الوقت لكن نوع الحركة صريح
        var punches = new[]
        {
            Punch(1, day.AddHours(15).AddMinutes(5), "O"),
            Punch(2, day.AddHours(6).AddMinutes(55), "I")
        };

        var result = PunchClassifier.Classify(punches, TestDb.FirstShift(), day, Policy());

        Assert.Equal(day.AddHours(6).AddMinutes(55), result.CheckIn);
        Assert.Equal(day.AddHours(15).AddMinutes(5), result.CheckOut);
        Assert.Equal(0, result.CalculatedOvertimeMinutes); // داخل سماح الخروج
        Assert.Equal(0, result.LateMinutes);
    }

    [Fact]
    public void Neutral_Punch_Type_Does_Not_Decide_Direction()
    {
        Assert.Equal(PunchDirection.Unknown, PunchClassifier.ParseExplicitDirection("FP"));
        Assert.Equal(PunchDirection.Unknown, PunchClassifier.ParseExplicitDirection(null));
        Assert.Equal(PunchDirection.In, PunchClassifier.ParseExplicitDirection("I"));
        Assert.Equal(PunchDirection.Out, PunchClassifier.ParseExplicitDirection("خروج"));
    }

    [Fact]
    public void Duplicate_Punches_At_Same_Time_Are_Deduplicated()
    {
        var day = TestDb.Monday;
        var punches = new[]
        {
            Punch(1, day.AddHours(7)),
            Punch(2, day.AddHours(7)),   // نفس الوقت بالضبط
            Punch(3, day.AddHours(15))
        };

        var result = PunchClassifier.Classify(punches, TestDb.FirstShift(), day, Policy());
        Assert.Equal(AttendanceStatus.Present, result.SuggestedStatus);
        Assert.Equal(2, result.Events.Count); // البصمتان المكررتان تُدمجان ثم تُصنّفان كدخول وخروج
    }

    // ===================== 13) الوردية الليلية عبر منتصف الليل =====================

    [Fact] // 13
    public void Overnight_Shift_Punches_Are_Classified_On_The_Next_Day()
    {
        var workDate = TestDb.Monday;                       // 2024-10-07
        var entry = workDate.AddHours(22).AddMinutes(50);   // 22:50 (بداية 23:00)
        var exit = workDate.AddDays(1).AddHours(7).AddMinutes(40); // 07:40 اليوم التالي

        var punches = new[] { Punch(1, entry), Punch(2, exit) };
        var result = PunchClassifier.Classify(punches, TestDb.NightShift(), workDate, Policy());

        Assert.Equal(entry, result.CheckIn);
        Assert.Equal(exit, result.CheckOut);
        Assert.Equal(0, result.LateMinutes);
        Assert.Equal(40, result.CalculatedOvertimeMinutes); // من 07:00 بعد تجاوز سماح 30
    }

    [Fact]
    public void Overnight_Punch_Is_Assigned_To_The_Shift_Start_Date()
    {
        var workDate = TestDb.Monday;
        var afterMidnight = workDate.AddDays(1).AddMinutes(30); // 00:30 اليوم التالي

        var resolved = HrPayroll.Infrastructure.Services.ShiftResolver.ResolveWorkDate(
            afterMidnight, TestDb.NightShift(), 120, 240);

        Assert.Equal(workDate, resolved);
    }
}
