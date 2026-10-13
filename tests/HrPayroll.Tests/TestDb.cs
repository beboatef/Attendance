using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Tests;

/// <summary>قاعدة بيانات SQLite في الذاكرة لاختبار الخدمات مع السلوك العلائقي.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>الوردية الأولى بالتكوين المعتمد: 07:00 - 15:00 بسماح 30 دقيقة دخولًا وخروجًا.</summary>
    public static Shift FirstShift(int id = 1) => new()
    {
        Id = id,
        ShiftName = "الوردية الأولى",
        ShiftNumber = 1,
        StartTime = new TimeSpan(7, 0, 0),
        EndTime = new TimeSpan(15, 0, 0),
        StandardHours = 8m,
        EntryGraceMinutes = 30,
        ExitGraceMinutes = 30,
        WorkingDays = "0,1,2,3,4,6",
        IsOvernight = false,
        OvertimeMultiplier = 1m,
        IsActive = true
    };

    /// <summary>وردية ليلية: 23:00 حتى 07:00 من اليوم التالي.</summary>
    public static Shift NightShift(int id = 2) => new()
    {
        Id = id,
        ShiftName = "الوردية الثالثة (ليلية)",
        ShiftNumber = 3,
        StartTime = new TimeSpan(23, 0, 0),
        EndTime = new TimeSpan(7, 0, 0),
        StandardHours = 8m,
        EntryGraceMinutes = 30,
        ExitGraceMinutes = 30,
        WorkingDays = "0,1,2,3,4,6",
        IsOvernight = true,
        OvertimeMultiplier = 1m,
        IsActive = true
    };

    /// <summary>وردية ثانية: 15:00 حتى 23:00.</summary>
    public static Shift SecondShift(int id = 3) => new()
    {
        Id = id,
        ShiftName = "الوردية الثانية",
        ShiftNumber = 2,
        StartTime = new TimeSpan(15, 0, 0),
        EndTime = new TimeSpan(23, 0, 0),
        StandardHours = 8m,
        EntryGraceMinutes = 30,
        ExitGraceMinutes = 30,
        WorkingDays = "0,1,2,3,4,6",
        IsOvernight = false,
        OvertimeMultiplier = 1m,
        IsActive = true
    };

    public static Employee Employee(int id = 1, string code = "1001", decimal salary = 9000m, int? shiftId = null) => new()
    {
        Id = id,
        EmployeeCode = code,
        DeviceEmployeeCode = code,
        FullName = "موظف اختبار " + code,
        BasicSalary = salary,
        StandardWorkHours = 8m,
        HireDate = new DateTime(2022, 1, 1),
        ShiftId = shiftId,
        EmploymentStatus = EmploymentStatus.Active,
        IsActive = true
    };

    /// <summary>رأس السنة: 2024-10-06 هو يوم أحد، و2024-10-11 يوم جمعة.</summary>
    public static readonly DateTime Sunday = new(2024, 10, 6);
    public static readonly DateTime Monday = new(2024, 10, 7);
    public static readonly DateTime Friday = new(2024, 10, 11);
}
