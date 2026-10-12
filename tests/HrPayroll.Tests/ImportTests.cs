using System.Text;
using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrPayroll.Tests;

/// <summary>الاختبارات 9-10: الموظف غير المسجل، ومنع الاستيراد المزدوج، واكتشاف نوع الملف.</summary>
public class ImportTests
{
    private const string Csv =
        "No.,Device ID,ID.,Name,Att.Type,Att.Time\r\n" +
        "1,1,1001,أحمد,FP,2024-10-01 07:05:00\r\n" +
        "2,1,1001,أحمد,FP,2024-10-01 15:10:00\r\n" +
        "3,1,9999,مجهول,FP,2024-10-01 07:00:00\r\n" +
        "4,1,1001,أحمد,FP,ليس-تاريخ\r\n" +
        "5,1,1001,أحمد,FP,2024-10-01 07:05:00\r\n";

    private static byte[] CsvBytes() => Encoding.UTF8.GetBytes(Csv);

    [Fact] // 9 - موظف غير مسجل يظهر في تقرير الأخطاء
    public async Task Unknown_Employee_Is_Detected()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee());
        await ctx.SaveChangesAsync();

        var service = new AttendanceImportService(ctx, NullLogger<AttendanceImportService>.Instance);
        var analysis = await service.AnalyzeAsync(CsvBytes(), "att.csv");

        Assert.True(analysis.HasMapping);
        Assert.Equal(1, analysis.UnknownEmployeeCount);
        Assert.Equal(1, analysis.InvalidCount);
        Assert.Equal(1, analysis.DuplicateCount);
        Assert.Equal(2, analysis.SuccessCount);
    }

    [Fact] // 10 - إعادة استيراد نفس الملف لا تُكرر الحركات
    public async Task Reimporting_Same_File_Does_Not_Duplicate()
    {
        using var db = new TestDb();
        await using var ctx = db.CreateContext();
        ctx.Shifts.Add(TestDb.FirstShift());
        ctx.Employees.Add(TestDb.Employee());
        await ctx.SaveChangesAsync();

        var service = new AttendanceImportService(ctx, NullLogger<AttendanceImportService>.Instance);
        var bytes = CsvBytes();

        var first = await service.CommitAsync(await service.AnalyzeAsync(bytes, "att.csv"), "att.csv", "tester", "Tester");
        Assert.Equal(2, first.Inserted);
        Assert.Equal(2, ctx.RawPunches.Count());

        // إعادة تحليل نفس الملف: كل السجلات تُكتشف كمكررة ولا يُضاف شيء جديد
        var secondAnalysis = await service.AnalyzeAsync(bytes, "att.csv");
        Assert.Equal(0, secondAnalysis.SuccessCount);
        Assert.Equal(3, secondAnalysis.DuplicateCount); // سجلان مكرران في قاعدة البيانات + سجل مكرر داخل الملف

        var second = await service.CommitAsync(secondAnalysis, "att.csv", "tester", "Tester");
        Assert.Equal(0, second.Inserted);
        Assert.Equal(2, ctx.RawPunches.Count());
    }

    [Fact] // اكتشاف نوع الملف من المحتوى وليس من الامتداد (ملف .xls بتنسيق Excel XML)
    public void Detects_Excel_Xml_Despite_Xls_Extension()
    {
        var xml = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\">" +
            "<Worksheet><Table><Row><Cell><Data ss:Type=\"String\">ID.</Data></Cell></Row></Table></Worksheet></Workbook>");

        Assert.Equal(AttendanceFileFormat.SpreadsheetXml, AttendanceFileParser.DetectFormat(xml));
    }

    [Fact]
    public void Detects_Format_From_Magic_Bytes()
    {
        Assert.Equal(AttendanceFileFormat.Xlsx, AttendanceFileParser.DetectFormat(new byte[] { 0x50, 0x4B, 0x03, 0x04 }));
        Assert.Equal(AttendanceFileFormat.Biff8, AttendanceFileParser.DetectFormat(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }));
        Assert.Equal(AttendanceFileFormat.Csv, AttendanceFileParser.DetectFormat(Encoding.UTF8.GetBytes("ID.,Att.Time\r\n1,x")));
    }

    [Fact] // تحويل Att.Time إلى تاريخ/وقت صحيح
    public void Parses_Common_Time_Formats()
    {
        Assert.True(PunchTimeParser.TryParse("2024-10-01 07:05:00", out var d1, out _));
        Assert.Equal(new DateTime(2024, 10, 1, 7, 5, 0), d1);

        Assert.True(PunchTimeParser.TryParse("01/10/2024 07:05", out var d2, out _));
        Assert.Equal(new DateTime(2024, 10, 1, 7, 5, 0), d2);

        Assert.False(PunchTimeParser.TryParse("ليس-تاريخ", out _, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Detects_Excel_Xml_Rows_And_Columns()
    {
        var xml = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?>" +
            "<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">" +
            "<Worksheet ss:Name=\"Sheet1\"><Table>" +
            "<Row><Cell><Data ss:Type=\"String\">No.</Data></Cell><Cell><Data ss:Type=\"String\">Device ID</Data></Cell>" +
            "<Cell><Data ss:Type=\"String\">ID.</Data></Cell><Cell><Data ss:Type=\"String\">Name</Data></Cell>" +
            "<Cell><Data ss:Type=\"String\">Att.Type</Data></Cell><Cell><Data ss:Type=\"String\">Att.Time</Data></Cell></Row>" +
            "<Row><Cell><Data ss:Type=\"Number\">1</Data></Cell><Cell><Data ss:Type=\"Number\">1</Data></Cell>" +
            "<Cell><Data ss:Type=\"Number\">1001</Data></Cell><Cell><Data ss:Type=\"String\">أحمد</Data></Cell>" +
            "<Cell><Data ss:Type=\"String\">FP</Data></Cell><Cell><Data ss:Type=\"String\">2024-10-01 07:05:00</Data></Cell></Row>" +
            "</Table></Worksheet></Workbook>");

        var sheet = AttendanceFileParser.Parse(xml, AttendanceFileFormat.SpreadsheetXml);
        Assert.Equal(2, sheet.Rows.Count);
        var mapping = ColumnMapper.Detect(sheet.Rows);
        Assert.NotNull(mapping);
        Assert.True(mapping!.IsUsable);
        Assert.Equal(2, mapping.EmployeeCode);
        Assert.Equal(5, mapping.AttTime);
    }
}
