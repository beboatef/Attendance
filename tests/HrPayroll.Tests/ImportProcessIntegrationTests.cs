using ClosedXML.Excel;
using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>
/// تكامل: استيراد بصمات ثم معالجتها في نفس سياق قاعدة البيانات (نفس الطلب).
/// </summary>

public class ImportProcessIntegrationTests
{
    private static byte[] File(params (string code, string time)[] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "No"; ws.Cell(1, 2).Value = "DeviceId"; ws.Cell(1, 3).Value = "ID";
        ws.Cell(1, 4).Value = "Name"; ws.Cell(1, 5).Value = "AttType"; ws.Cell(1, 6).Value = "Att.Time";
        var r = 2;
        foreach (var (code, time) in rows)
        {
            ws.Cell(r, 1).Value = r - 1; ws.Cell(r, 2).Value = "1"; ws.Cell(r, 3).Value = code;
            ws.Cell(r, 4).Value = "أ"; ws.Cell(r, 5).Value = "FP"; ws.Cell(r, 6).Value = time; r++;
        }
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    [Fact]
    public async Task Import_Then_Process_In_Same_Context()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee(salary: 9000m));
        await ctx.SaveChangesAsync();

        var import = new AttendanceImportService(ctx, NullLogger<AttendanceImportService>.Instance);
        var bytes = File(
            ("1001", "2024-10-07 07:35:00"),
            ("1001", "2024-10-07 16:00:00"),
            ("1001", "2024-10-08 07:00:00"),
            ("1001", "2024-10-08 15:00:00"));
        var analysis = await import.AnalyzeAsync(bytes, "x.xls");
        var commit = await import.CommitAsync(analysis, "x.xls", "u1", "tester");
        Assert.Equal(4, commit.Inserted);

        var processing = new AttendanceProcessingService(ctx, new SettingsService(ctx), NullLogger<AttendanceProcessingService>.Instance);
        await processing.ProcessAsync(TestDb.Monday, TestDb.Monday.AddDays(1));
        Assert.Equal(2, await Task.FromResult(ctx.DailyAttendances.Local.Count));
    }
}
