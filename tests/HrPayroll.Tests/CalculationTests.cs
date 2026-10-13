using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Models;
using HrPayroll.Domain.Services;
using HrPayroll.Domain.ValueObjects;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>
/// المتطلبات 1-11 و13: قواعد التأخير وفترة السماح والإضافي وإعداد الوردية الأولى والورديات الليلية.
/// الوردية الأولى المعتمدة: البداية 07:00، النهاية 15:00، سماح دخول 30، سماح خروج 30، 8 ساعات.
/// </summary>
public class CalculationTests
{
    private static readonly DateTime Day = TestDb.Monday;
    private static ShiftWindow Window(Shift shift) => ShiftWindowResolver.Resolve(shift, Day);

    // ===================== 7) الوردية الأولى: 07:00 إلى 15:00 =====================

    [Fact] // 7
    public void FirstShift_Starts_0700_Ends_1500_With_8_Hours()
    {
        var s = TestDb.FirstShift();
        var w = Window(s);
        Assert.Equal(Day.AddHours(7), w.Start);
        Assert.Equal(Day.AddHours(15), w.End);
        Assert.Equal(480, w.DurationMinutes);
        Assert.Equal(8m, s.StandardHours);
        Assert.False(s.IsOvernight);
    }

    [Fact]
    public void Shift_EndTime_Is_Computed_From_Start_And_StandardHours()
    {
        var s = new Shift { StartTime = new TimeSpan(7, 0, 0), StandardHours = 8m };
        Assert.Equal(new TimeSpan(15, 0, 0), s.ComputeEndTime());
        Assert.False(s.ComputeIsOvernight());

        var night = new Shift { StartTime = new TimeSpan(23, 0, 0), StandardHours = 8m };
        Assert.Equal(new TimeSpan(7, 0, 0), night.ComputeEndTime());
        Assert.True(night.ComputeIsOvernight());
    }

    [Fact]
    public void Shift_WorkingDays_Are_Configurable()
    {
        var s = TestDb.FirstShift();
        Assert.True(s.WorksOn(DayOfWeek.Sunday));
        Assert.True(s.WorksOn(DayOfWeek.Saturday));
        Assert.False(s.WorksOn(DayOfWeek.Friday));

        var all = TestDb.FirstShift();
        all.WorkingDays = null;
        Assert.True(all.WorksOn(DayOfWeek.Friday));
    }

    // ===================== 1-3) فترة سماح الدخول عتبة وليست إعفاءً =====================

    [Theory] // 1 و 2
    [InlineData(7, 10, 0)]   // داخل السماح
    [InlineData(7, 25, 0)]   // داخل السماح
    [InlineData(7, 30, 0)]   // عند حد السماح بالضبط
    public void Late_Within_Grace_Is_Zero(int h, int m, int expected)
    {
        var s = TestDb.FirstShift();
        Assert.Equal(expected, AttendanceCalculator.CalculateLateMinutes(Window(s), s.EntryGraceMinutes, Day.AddHours(h).AddMinutes(m)));
    }

    [Theory] // 3
    [InlineData(7, 35, 35)]   // تجاوز السماح: يُحتسب كامل التأخير من 07:00 ولا يُخصم السماح
    [InlineData(7, 40, 40)]
    [InlineData(7, 31, 31)]
    [InlineData(8, 0, 60)]
    [InlineData(8, 15, 75)]
    public void Late_After_Grace_Counts_From_Shift_Start(int h, int m, int expected)
    {
        var s = TestDb.FirstShift();
        Assert.Equal(expected, AttendanceCalculator.CalculateLateMinutes(Window(s), s.EntryGraceMinutes, Day.AddHours(h).AddMinutes(m)));
    }

    [Fact]
    public void Late_Is_Zero_When_No_Entry_Punch()
    {
        var s = TestDb.FirstShift();
        Assert.Equal(0, AttendanceCalculator.CalculateLateMinutes(Window(s), s.EntryGraceMinutes, null));
    }

    [Fact]
    public void Late_Is_Zero_When_Arriving_Before_Shift_Start()
        => Assert.Equal(0, AttendanceCalculator.CalculateLateMinutes(
            Window(TestDb.FirstShift()), 30, Day.AddHours(6).AddMinutes(30)));

    // ===================== 4-6) سماح الخروج عتبة للإضافي =====================

    [Theory] // 4 و 5
    [InlineData(15, 0, 0)]
    [InlineData(15, 15, 0)]
    [InlineData(15, 30, 0)]   // عند حد السماح بالضبط
    [InlineData(14, 30, 0)]   // قبل نهاية الوردية
    public void Overtime_Within_Exit_Grace_Is_Zero(int h, int m, int expected)
    {
        var s = TestDb.FirstShift();
        Assert.Equal(expected, AttendanceCalculator.CalculateOvertimeMinutes(
            Window(s), s.ExitGraceMinutes, Day.AddHours(h).AddMinutes(m)));
    }

    [Theory] // 6
    [InlineData(15, 35, 35)]   // يُحتسب كامل الإضافي من 15:00
    [InlineData(15, 40, 40)]
    [InlineData(16, 0, 60)]
    [InlineData(17, 0, 120)]
    public void Overtime_After_Exit_Grace_Counts_From_Shift_End(int h, int m, int expected)
    {
        var s = TestDb.FirstShift();
        Assert.Equal(expected, AttendanceCalculator.CalculateOvertimeMinutes(
            Window(s), s.ExitGraceMinutes, Day.AddHours(h).AddMinutes(m)));
    }

    [Fact]
    public void Overtime_Is_Zero_When_No_Exit_Punch()
        => Assert.Equal(0, AttendanceCalculator.CalculateOvertimeMinutes(Window(TestDb.FirstShift()), 30, null));

    // ===================== 13) الورديات الليلية =====================

    [Fact] // 13
    public void NightShift_Window_And_Overtime()
    {
        var s = TestDb.NightShift();
        var w = ShiftWindowResolver.Resolve(s, Day);
        Assert.Equal(Day.AddHours(23), w.Start);
        Assert.Equal(Day.AddDays(1).AddHours(7), w.End);
        Assert.Equal(480, w.DurationMinutes);

        // سماح 30: الخروج 07:30 = صفر إضافي، والخروج 08:00 = 60 دقيقة من 07:00
        Assert.Equal(0, AttendanceCalculator.CalculateOvertimeMinutes(w, 30, Day.AddDays(1).AddHours(7).AddMinutes(30)));
        Assert.Equal(60, AttendanceCalculator.CalculateOvertimeMinutes(w, 30, Day.AddDays(1).AddHours(8)));
    }

    [Fact]
    public void NightShift_Late_Is_Measured_From_2300()
    {
        var s = TestDb.NightShift();
        var w = ShiftWindowResolver.Resolve(s, Day);
        // 23:20 داخل فترة السماح (30 دقيقة) => لا تأخير
        Assert.Equal(0, AttendanceCalculator.CalculateLateMinutes(w, 30, Day.AddHours(23).AddMinutes(20)));
        // 23:50 => 50 دقيقة تأخير (تُحسب من بداية الوردية 23:00 لا من نهاية السماح)
        Assert.Equal(50, AttendanceCalculator.CalculateLateMinutes(w, 30, Day.AddHours(23).AddMinutes(50)));
        // 00:50 من اليوم التالي => 110 دقيقة من 23:00 (الوردية الليلية تعبر منتصف الليل)
        Assert.Equal(110, AttendanceCalculator.CalculateLateMinutes(w, 30, Day.AddDays(1).AddMinutes(50)));
    }

    [Fact]
    public void Early_Leave_Minutes_Are_Computed()
    {
        var s = TestDb.FirstShift();
        var w = Window(s);
        Assert.Equal(0, AttendanceCalculator.CalculateEarlyLeaveMinutes(w, Day.AddHours(15)));
        Assert.Equal(45, AttendanceCalculator.CalculateEarlyLeaveMinutes(w, Day.AddHours(14).AddMinutes(15)));
    }

    // ===================== 8-11) أجر الإضافي بدون أي مضاعف =====================

    private static AttendancePolicy Policy() => new()
    {
        WorkDaysPerMonth = 30,
        WorkHoursPerDay = 8,
        DefaultOvertimeMultiplier = 1m,
        LateDeductionEnabled = true,
        AbsenceDeductionEnabled = true
    };

    [Fact] // 8
    public void Daily_And_Hourly_Rates_For_9000_Salary()
    {
        var p = Policy();
        Assert.Equal(300m, p.DailyRateFor(9000m));       // 9000 / 30
        Assert.Equal(37.50m, p.HourlyRateFor(300m));     // 300 / 8
        Assert.Equal(0.625m, p.MinuteRateFor(300m));     // 37.50 / 60
    }

    [Fact] // 9
    public void Ten_Approved_Overtime_Hours_Are_375()
    {
        var p = Policy();
        var result = PayrollCalculator.Compute(
            new PayrollInputs
            {
                BasicSalary = 9000m,
                ApprovedOvertimeMinutes = 600,
                PaidOvertimeMinutes = 600,
                RawOvertimeMinutes = 600
            }, p);

        Assert.Equal(37.50m, result.HourlyRate);
        Assert.Equal(10m, result.PaidOvertimeHours);
        Assert.Equal(375m, result.OvertimeAmount);
    }

    [Fact] // 10
    public void Fifty_Approved_Overtime_Hours_Are_1875()
    {
        var result = PayrollCalculator.Compute(
            new PayrollInputs
            {
                BasicSalary = 9000m,
                ApprovedOvertimeMinutes = 3000,
                PaidOvertimeMinutes = 3000,
                RawOvertimeMinutes = 3000
            }, Policy());

        Assert.Equal(50m, result.PaidOvertimeHours);
        Assert.Equal(1875m, result.OvertimeAmount);
        Assert.Equal(10875m, result.GrossEarnings);
    }

    [Fact] // 11
    public void No_Overtime_Multiplier_Is_Applied_By_Default()
    {
        var p = Policy();
        Assert.Equal(1m, p.DefaultOvertimeMultiplier);

        // ساعة واحدة = أجر الساعة بالضبط بدون أي زيادة
        var one = PayrollCalculator.Compute(
            new PayrollInputs { BasicSalary = 9000m, ApprovedOvertimeMinutes = 60, PaidOvertimeMinutes = 60, RawOvertimeMinutes = 60 }, p);
        Assert.Equal(37.50m, one.OvertimeAmount);
        Assert.Equal(1m, one.OvertimeMultiplier);

        // 1.5 مضاعفة غير مطبقة: القيمة تظل 37.50 وليست 56.25
        Assert.NotEqual(56.25m, one.OvertimeAmount);
    }

    [Fact]
    public void Overtime_Rounding_Is_Applied_Once_To_The_Month_Total()
    {
        var p = new AttendancePolicy { OvertimeRoundingStepMinutes = 15, RoundOvertimeDown = true };
        Assert.Equal(0, p.RoundOvertimeMinutes(14));
        Assert.Equal(15, p.RoundOvertimeMinutes(20));
        Assert.Equal(45, p.RoundOvertimeMinutes(59));
        Assert.Equal(60, p.RoundOvertimeMinutes(60));
    }
}
