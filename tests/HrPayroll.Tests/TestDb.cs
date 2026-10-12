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

    public static Shift FirstShift() => new()
    {
        Id = 1,
        ShiftName = "الوردية الأولى",
        StartTime = new TimeSpan(7, 0, 0),
        EndTime = new TimeSpan(15, 0, 0),
        GraceMinutes = 30,
        IsOvernight = false,
        OvertimeMultiplier = 1m,
        IsActive = true
    };

    public static Shift NightShift() => new()
    {
        Id = 2,
        ShiftName = "الوردية الثالثة",
        StartTime = new TimeSpan(23, 0, 0),
        EndTime = new TimeSpan(7, 0, 0),
        GraceMinutes = 0,
        IsOvernight = true,
        OvertimeMultiplier = 1m,
        IsActive = true
    };

    public static Employee Employee(int id = 1, string code = "1001", decimal salary = 4800m, int shiftId = 1) => new()
    {
        Id = id,
        EmployeeCode = code,
        DeviceEmployeeCode = code,
        FullName = "موظف اختبار " + code,
        BasicSalary = salary,
        HireDate = new DateTime(2022, 1, 1),
        ShiftId = shiftId,
        IsActive = true
    };
}
