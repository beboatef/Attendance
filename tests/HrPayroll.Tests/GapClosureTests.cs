using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Xunit;

namespace HrPayroll.Tests;

public sealed class GapClosureTests
{
    [Fact]
    public void EmploymentStatusText_IsArabic()
    {
        Assert.Equal("على العمل", ExcelExportService.EmploymentStatusText(EmploymentStatus.Active));
        Assert.Equal("في إجازة طويلة", ExcelExportService.EmploymentStatusText(EmploymentStatus.OnLeave));
        Assert.Equal("موقوف", ExcelExportService.EmploymentStatusText(EmploymentStatus.Suspended));
        Assert.Equal("منتهي الخدمة", ExcelExportService.EmploymentStatusText(EmploymentStatus.Terminated));
    }

    [Fact]
    public void AttendanceStatusText_MatchesAttendanceText()
    {
        foreach (var s in Enum.GetValues<AttendanceStatus>())
            Assert.Equal(ExcelExportService.AttendanceText(s), ExcelExportService.AttendanceStatusText(s));
    }

    [Fact]
    public void MonthlyRow_CountsStatuses()
    {
        var row = new MonthlyRow { EmployeeCode = "1", EmployeeName = "Test" };
        var day = new DateTime(2026, 1, 1);
        row.Cells.AddRange(new[]
        {
            new MonthlyCell { Date = day, Status = AttendanceStatus.Present, HasRecord = true },
            new MonthlyCell { Date = day.AddDays(1), Status = AttendanceStatus.Late, HasRecord = true },
            new MonthlyCell { Date = day.AddDays(2), Status = AttendanceStatus.Absent, HasRecord = true },
            new MonthlyCell { Date = day.AddDays(3), Status = AttendanceStatus.PaidLeave, HasRecord = true },
            new MonthlyCell { Date = day.AddDays(4), Status = null, HasRecord = false },
        });
        Assert.Equal(2, row.Present);
        Assert.Equal(1, row.Absent);
        Assert.Equal(1, row.Leave);
    }

    [Fact]
    public void PayrollTrendPoint_LabelFormat()
    {
        var p = new PayrollTrendPoint { Year = 2026, Month = 3, TotalNet = 100m };
        Assert.Equal("2026-03", p.Label);
    }

    [Fact]
    public void OrgStructure_GroupsByDepartmentAndTitle()
    {
        var employees = new List<HrPayroll.Domain.Entities.Employee>
        {
            new() { FullName = "A", EmployeeCode = "1", Department = "اللف", JobTitle = "صنايعي" },
            new() { FullName = "B", EmployeeCode = "2", Department = "اللف", JobTitle = "مساعد" },
            new() { FullName = "C", EmployeeCode = "3", Department = "القص", JobTitle = "مشرف" },
        };
        var byDept = employees.GroupBy(e => e.Department).ToDictionary(g => g.Key!, g => g.ToList());
        Assert.Equal(2, byDept.Count);
        Assert.Equal(2, byDept["اللف"].Count);
        Assert.Equal(2, byDept["اللف"].Select(e => e.JobTitle).Distinct().Count());
    }
}
