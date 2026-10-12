using HrPayroll.Domain.Enums;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>الاختبارات 7-8: تكرار البصمات، والبصمات الناقصة، وعدم افتراض الاتجاه.</summary>
public class ClassificationTests
{
    private static readonly DateTime Day = new(2024, 10, 1);
    private static AttendancePolicy Policy() => new();

    private static RawPunchInput P(int id, DateTime time, string? type = "FP")
        => new(id, "1001", "1", type, time, time.ToString("yyyy-MM-dd HH:mm:ss"), id);

    [Fact] // 7 - بصمة مكررة لا تُحتسب مرتين
    public void Duplicate_Punch_At_Same_Time_Counted_Once()
    {
        var shift = TestDb.FirstShift();
        var punches = new[]
        {
            P(1, Day.AddHours(7)),
            P(2, Day.AddHours(7)) // نفس الوقت تمامًا
        };

        var result = PunchClassifier.Classify(punches, shift, Day, Policy());

        Assert.Single(result.Events); // بصمة واحدة فقط بعد إزالة التكرار
    }

    [Fact] // 8 - دخول بدون خروج: حالة استثنائية ولا يُخترع وقت خروج
    public void Entry_Without_Exit_Produces_MissingCheckOut_And_No_Invented_Exit()
    {
        var shift = TestDb.FirstShift();
        var punches = new[]
        {
            P(1, Day.AddHours(7).AddMinutes(10)),
            P(2, Day.AddHours(8)) // لا يزال قبل منتصف الوردية -> ليست خروجًا
        };

        var result = PunchClassifier.Classify(punches, shift, Day, Policy());

        Assert.Null(result.CheckOut);
        Assert.True(result.HasIncompletePunches);
        Assert.Equal(AttendanceStatus.MissingCheckOut, result.SuggestedStatus);
        Assert.Equal(ClassificationStatus.NeedsReview, result.Status);
        Assert.DoesNotContain(result.Events, e => e.EventType == AttendanceEventType.Exit);
    }

    [Fact] // خروج بدون دخول
    public void Exit_Without_Entry_Produces_MissingCheckIn()
    {
        var shift = TestDb.FirstShift();
        var punches = new[]
        {
            P(1, Day.AddHours(12)),
            P(2, Day.AddHours(13))
        };

        var result = PunchClassifier.Classify(punches, shift, Day, Policy());

        Assert.Null(result.CheckIn);
        Assert.Equal(AttendanceStatus.MissingCheckIn, result.SuggestedStatus);
    }

    [Fact] // أول وآخر بصمة لا تتحول تلقائيًا لدخول وخروج دون فحص الوردية
    public void First_And_Last_Are_Not_Blindly_Entry_And_Exit()
    {
        var shift = TestDb.FirstShift();
        var punches = new[]
        {
            P(1, Day.AddHours(13)), // كلا البصمتين بعد منتصف الوردية
            P(2, Day.AddHours(14))
        };

        var result = PunchClassifier.Classify(punches, shift, Day, Policy());

        Assert.Null(result.CheckIn); // لا نخترع دخولًا
    }

    [Fact] // نوع البصمة FP لا يحدد الاتجاه بل تُستخدم القواعد
    public void Neutral_Punch_Type_Is_Classified_By_Rules_Not_By_Type()
    {
        var shift = TestDb.FirstShift();
        var punches = new[]
        {
            P(1, Day.AddHours(7).AddMinutes(5), "FP"),
            P(2, Day.AddHours(16), "FP")
        };

        var result = PunchClassifier.Classify(punches, shift, Day, Policy());

        Assert.Equal(ClassificationStatus.Classified, result.Status);
        Assert.All(result.Events, e => Assert.Equal(ClassificationSource.Rule, e.Source));
        Assert.Equal(Day.AddHours(7).AddMinutes(5), result.CheckIn);
        Assert.Equal(Day.AddHours(16), result.CheckOut);
    }

    [Fact] // نوع الحركة الصريح من الجهاز يُستخدم بعد التحقق
    public void Explicit_Device_Direction_Is_Honored()
    {
        var shift = TestDb.FirstShift();
        var punches = new[]
        {
            P(1, Day.AddHours(7).AddMinutes(2), "I"),
            P(2, Day.AddHours(15).AddMinutes(30), "O")
        };

        var result = PunchClassifier.Classify(punches, shift, Day, Policy());

        Assert.Equal(ClassificationSource.ExplicitDeviceType, result.Events[0].Source);
        Assert.Equal(Day.AddHours(7).AddMinutes(2), result.CheckIn);
        Assert.Equal(Day.AddHours(15).AddMinutes(30), result.CheckOut);
        Assert.Equal(30, result.CalculatedOvertimeMinutes);
    }

    [Fact]
    public void ParseExplicitDirection_Recognizes_Common_Values()
    {
        Assert.Equal(PunchDirection.In, PunchClassifier.ParseExplicitDirection("I"));
        Assert.Equal(PunchDirection.Out, PunchClassifier.ParseExplicitDirection("O"));
        Assert.Equal(PunchDirection.In, PunchClassifier.ParseExplicitDirection("دخول"));
        Assert.Equal(PunchDirection.Unknown, PunchClassifier.ParseExplicitDirection("FP"));
        Assert.Equal(PunchDirection.Unknown, PunchClassifier.ParseExplicitDirection(null));
    }
}
