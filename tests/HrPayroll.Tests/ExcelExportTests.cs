using ClosedXML.Excel;
using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>الاختبار 11: شيتا الدخول والخروج يحتفظان بالربط مع السجلات الأصلية.</summary>
public class ExcelExportTests
{
    private static async Task<(AppDbContext ctx, int rawId)> SeedAndProcessAsync(TestDb db)
    {
        var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee());

        var batch = new ImportBatch { FileName = "att5.xls", DetectedFormat = "SpreadsheetXml", ImportedAt = DateTime.UtcNow, ImportedByName = "tester" };
        ctx.ImportBatches.Add(batch);
        await ctx.SaveChangesAsync();

        var entry = new RawPunch
        {
            ImportBatchId = batch.Id, DeviceId = "1", DeviceEmployeeCode = "1001", RawEmployeeName = "موظف",
            AttType = "FP", PunchTime = new DateTime(2024, 10, 1, 7, 5, 0), RawPunchTimeText = "2024-10-01 07:05:00",
            DedupHash = "H1", EmployeeId = 1
        };
        var exit = new RawPunch
        {
            ImportBatchId = batch.Id, DeviceId = "1", DeviceEmployeeCode = "1001", RawEmployeeName = "موظف",
            AttType = "FP", PunchTime = new DateTime(2024, 10, 1, 16, 0, 0), RawPunchTimeText = "2024-10-01 16:00:00",
            DedupHash = "H2", EmployeeId = 1
        };
        ctx.RawPunches.AddRange(entry, exit);
        await ctx.SaveChangesAsync();

        var processor = new AttendanceProcessingService(ctx, new SettingsService(ctx), NullLogger<AttendanceProcessingService>.Instance);
        await processor.ProcessAsync(new DateTime(2024, 10, 1), new DateTime(2024, 10, 1));

        return (ctx, entry.Id);
    }

    [Fact]
    public async Task Full_Export_Contains_All_Required_Sheets()
    {
        using var db = new TestDb();
        var (ctx, _) = await SeedAndProcessAsync(db);
        var exporter = new ExcelExportService(ctx, new SettingsService(ctx));

        var bytes = await exporter.ExportFullAsync(new ExportFilter
        {
            From = new DateTime(2024, 10, 1),
            To = new DateTime(2024, 10, 31),
            Year = 2024,
            Month = 10
        });

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        foreach (var name in new[] { "Raw Punches", "Entry", "Exit", "Daily Attendance", "Late Report", "Overtime Report", "Payroll", "Exceptions" })
            Assert.True(wb.Worksheets.Contains(name), $"الورقة مفقودة: {name}");
    }

    [Fact]
    public async Task Entry_Sheet_Preserves_Link_To_Raw_Punch()
    {
        using var db = new TestDb();
        var (ctx, entryRawId) = await SeedAndProcessAsync(db);

        var entryEvent = ctx.AttendanceEvents.Single(e => e.EventType == Domain.Enums.AttendanceEventType.Entry);
        Assert.Equal(entryRawId, entryEvent.RawPunchId);

        var exporter = new ExcelExportService(ctx, new SettingsService(ctx));
        var bytes = await exporter.ExportEventsAsync(
            new ExportFilter { From = new DateTime(2024, 10, 1), To = new DateTime(2024, 10, 1) },
            Domain.Enums.AttendanceEventType.Entry);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Entry");

        // العناوين في الصف الثالث (بعد عنوان التقرير والفترة)
        var headerRow = ws.Row(3).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("رقم السجل الأصلي", headerRow);
        Assert.Contains("كود الموظف", headerRow);

        // رقم السجل الأصلي في البيانات يجب أن يطابق البصمة الخام
        var rawIdColumn = headerRow.IndexOf("رقم السجل الأصلي") + 1;
        var dataRow = ws.Row(4);
        Assert.Equal(entryRawId, dataRow.Cell(rawIdColumn).GetValue<int>());
    }
}
