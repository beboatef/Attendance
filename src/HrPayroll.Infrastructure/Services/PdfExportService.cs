using HrPayroll.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrPayroll.Infrastructure.Services;

/// <summary>
/// توليد ملفات PDF (كشف الراتب الفردي وتقارير الرواتب والحضور) باستخدام خط عربي مضمّن.
/// لا يحتاج أي تثبيت خارجي على جهاز العميل.
/// </summary>
public class PdfExportService
{
    private static bool _initialized;
    private static readonly object Sync = new();
    private const string FontFamily = "Cairo";

    private readonly ReportService _reports;
    private readonly SettingsService _settings;

    public PdfExportService(ReportService reports, SettingsService settings)
    {
        _reports = reports;
        _settings = settings;
    }

    /// <summary>تسجيل الخط العربي وترخيص QuestPDF Community مرة واحدة.</summary>
    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (Sync)
        {
            if (_initialized) return;

            QuestPDF.Settings.License = LicenseType.Community;

            var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.ttf"))
                {
                    using var stream = File.OpenRead(file);
                    QuestPDF.Drawing.FontManager.RegisterFont(stream);
                }
            }
            QuestPDF.Settings.UseEnvironmentFonts = false;
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
            _initialized = true;
        }
    }

    /// <summary>كشف راتب موظف واحد.</summary>
    public async Task<byte[]> BuildPayslipAsync(int payrollItemId, CancellationToken ct = default)
    {
        EnsureInitialized();
        var slip = await _reports.GetPayslipAsync(payrollItemId, ct)
                   ?? throw new InvalidOperationException("سطر الراتب غير موجود");
        var i = slip.Item;

        string Money(decimal v) => $"{v:N2} {slip.Currency}";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(slip.CompanyName).FontSize(15).Bold();
                        row.RelativeItem().AlignLeft().Text($"كشف راتب — {slip.Year}-{slip.Month:00}").FontSize(11).Bold();
                    });
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Row(row =>
                    {
                        Info(row, "كود الموظف", i.EmployeeCode);
                        Info(row, "الاسم", i.EmployeeName);
                        Info(row, "القسم", i.Department ?? "-");
                    });
                    col.Item().Row(row =>
                    {
                        Info(row, "المسمى الوظيفي", i.JobTitle ?? "-");
                        Info(row, "حالة الكشف", ExcelExportService.PayrollStatusText(slip.RunStatus));
                        Info(row, "العملة", slip.Currency);
                    });

                    col.Item().PaddingTop(4).Text("أساس الحساب").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                        Head(t, "البند", "القيمة", "البند", "القيمة");
                        Row2(t, "الراتب الأساسي", Money(i.BasicSalary), "الأجر اليومي", Money(i.DailyRate));
                        Row2(t, "أجر الساعة", Money(i.HourlyRate), "أيام الشهر", "30");
                        Row2(t, "أيام الحضور", i.AttendanceDays.ToString(), "أيام راحة مدفوعة", i.PaidRestDays.ToString());
                        Row2(t, "إجازات مدفوعة", i.PaidLeaveDays.ToString("0.##"), "إجازات بدون أجر", i.UnpaidLeaveDays.ToString("0.##"));
                        Row2(t, "أيام غياب", i.AbsenceDays.ToString("0.##"), "دقائق التأخير", i.LateMinutes.ToString());
                    });

                    col.Item().PaddingTop(4).Text("الإضافي").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                        Head(t, "البند", "القيمة", "البند", "القيمة");
                        Row2(t, "إضافي خام", $"{i.RawOvertimeHours:0.##} ساعة", "إضافي معتمد", $"{i.ApprovedOvertimeHours:0.##} ساعة");
                        Row2(t, "إضافي مدفوع", $"{i.PaidOvertimeHours:0.##} ساعة", "معامل الإضافي", "1 (بدون مضاعفة)");
                        Row2(t, "قيمة الإضافي", Money(i.OvertimeAmount), "—", "—");
                    });

                    col.Item().PaddingTop(4).Text("الاستحقاقات").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); });
                        Head2(t, "البند", "المبلغ");
                        Line(t, "الراتب الأساسي", Money(i.BasicSalary));
                        Line(t, "قيمة الإضافي المعتمد", Money(i.OvertimeAmount));
                        Line(t, "مكافآت", Money(i.Bonuses));
                        Line(t, "بدلات", Money(i.Allowances));
                        Line(t, $"أيام عمل إضافية ({i.AdditionalDaysCount:0.##})", Money(i.AdditionalDaysAmount));
                        Line(t, "إجمالي الاستحقاقات", Money(i.GrossEarnings), bold: true);
                    });

                    col.Item().PaddingTop(4).Text("الخصومات").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); });
                        Head2(t, "البند", "المبلغ");
                        Line(t, "خصم التأخير", Money(i.LateDeduction));
                        Line(t, "خصم الغياب", Money(i.AbsenceDeduction));
                        Line(t, "خصم الإجازة بدون أجر", Money(i.UnpaidLeaveDeduction));
                        Line(t, "السلف", Money(i.LoansTotal));
                        Line(t, "خصومات", Money(i.DeductionsTotal));
                        Line(t, "خصومات أخرى", Money(i.OtherDeductions));
                        Line(t, "إجمالي الخصومات", Money(i.TotalDeductions), bold: true);
                    });

                    col.Item().PaddingTop(6).Background(Colors.Grey.Lighten3).Padding(8).Row(row =>
                    {
                        row.RelativeItem().Text("صافي الراتب المستحق").FontSize(12).Bold();
                        row.RelativeItem().AlignLeft().Text(Money(i.NetSalary)).FontSize(12).Bold();
                    });

                    if (slip.Notes.Count > 0)
                    {
                        col.Item().PaddingTop(6).Text("ملاحظات الحساب").Bold();
                        foreach (var n in slip.Notes)
                            col.Item().Text("• " + n).FontSize(8);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span("طُبع آليًا من نظام الموارد البشرية والرواتب — ");
                    t.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                });
            });
        });

        return doc.GeneratePdf();
    }

    /// <summary>تقرير الرواتب الكامل بصيغة PDF.</summary>
    public async Task<byte[]> BuildPayrollReportAsync(int payrollRunId, CancellationToken ct = default)
    {
        EnsureInitialized();
        var rows = await _reports.GetPayrollRowsAsync(payrollRunId, ct: ct);
        var settings = await _settings.GetAllAsync(ct);
        var company = settings.TryGetValue(SettingKeys.CompanyName, out var cn) ? cn : "HR & Payroll";
        var currency = settings.TryGetValue(SettingKeys.Currency, out var cur) ? cur : "EGP";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(15);
                page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(7));

                page.Header().Column(col =>
                {
                    col.Item().Text($"{company} — تقرير الرواتب الكامل").FontSize(12).Bold();
                    col.Item().Text($"عدد الموظفين: {rows.Count} | إجمالي الاستحقاقات: {rows.Sum(r => r.GrossEarnings):N2} | " +
                                    $"إجمالي الخصومات: {rows.Sum(r => r.TotalDeductions):N2} | صافي الكشف: {rows.Sum(r => r.NetSalary):N2} {currency}");
                    col.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingVertical(5).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(45);   // كود
                        c.RelativeColumn(2.2f); // الاسم
                        c.RelativeColumn(1.4f); // القسم
                        c.ConstantColumn(50);   // أساسي
                        c.ConstantColumn(38);   // أيام
                        c.ConstantColumn(34);   // راحة
                        c.ConstantColumn(38);   // إجازة مدفوعة
                        c.ConstantColumn(38);   // إجازة بدون أجر
                        c.ConstantColumn(32);   // غياب
                        c.ConstantColumn(42);   // إضافي معتمد
                        c.ConstantColumn(48);   // قيمة الإضافي
                        c.ConstantColumn(46);   // خصم تأخير
                        c.ConstantColumn(46);   // خصم غياب
                        c.ConstantColumn(44);   // سلف
                        c.ConstantColumn(50);   // إجمالي الاستحقاقات
                        c.ConstantColumn(48);   // إجمالي الخصومات
                        c.ConstantColumn(55);   // الصافي
                    });

                    table.Header(header =>
                    {
                        foreach (var h in new[] { "كود", "الاسم", "القسم", "أساسي", "أيام حضور", "راحة", "إجازة مدفوعة", "إجازة بدون أجر", "غياب", "إضافي معتمد (ساعة)", "قيمة الإضافي", "خصم تأخير", "خصم غياب", "سلف", "إجمالي الاستحقاقات", "إجمالي الخصومات", "الصافي" })
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(3)
                                .Text(h).FontSize(7).Bold().AlignCenter();
                        }
                    });

                    foreach (var r in rows)
                    {
                        Cell(table, r.EmployeeCode);
                        Cell(table, r.EmployeeName);
                        Cell(table, r.Department ?? "-");
                        Cell(table, r.BasicSalary.ToString("N2"));
                        Cell(table, r.AttendanceDays.ToString());
                        Cell(table, r.PaidRestDays.ToString());
                        Cell(table, r.PaidLeaveDays.ToString("0.##"));
                        Cell(table, r.UnpaidLeaveDays.ToString("0.##"));
                        Cell(table, r.AbsenceDays.ToString("0.##"));
                        Cell(table, r.ApprovedOvertimeHours.ToString("0.##"));
                        Cell(table, r.OvertimeAmount.ToString("N2"));
                        Cell(table, r.LateDeduction.ToString("N2"));
                        Cell(table, r.AbsenceDeduction.ToString("N2"));
                        Cell(table, r.LoansTotal.ToString("N2"));
                        Cell(table, r.GrossEarnings.ToString("N2"));
                        Cell(table, r.TotalDeductions.ToString("N2"));
                        Cell(table, r.NetSalary.ToString("N2"), bold: true);
                    }
                });

                page.Footer().AlignCenter().Text($"طُبع آليًا — {DateTime.Now:yyyy-MM-dd HH:mm}")
                    .FontSize(7).FontColor(Colors.Grey.Darken1);
            });
        });

        return doc.GeneratePdf();
    }

    /// <summary>تقرير الحضور اليومي بصيغة PDF.</summary>
    public async Task<byte[]> BuildAttendanceReportAsync(AttendanceReportFilter filter, CancellationToken ct = default)
    {
        EnsureInitialized();
        var rows = await _reports.GetDailyAttendanceAsync(filter, ct);
        var settings = await _settings.GetAllAsync(ct);
        var company = settings.TryGetValue(SettingKeys.CompanyName, out var cn) ? cn : "HR & Payroll";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(15);
                page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(7));

                page.Header().Column(col =>
                {
                    col.Item().Text($"{company} — تقرير الحضور اليومي").FontSize(12).Bold();
                    col.Item().Text($"{filter.PeriodText} | عدد الصفوف: {rows.Count} | " +
                                    $"حضور: {rows.Count(r => r.CheckIn != null)} | تأخير: {rows.Count(r => r.LateMinutes > 0)} | " +
                                    $"يحتاج مراجعة: {rows.Count(r => r.NeedsReview)}");
                    col.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingVertical(5).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(45);
                        c.RelativeColumn(2.2f);
                        c.RelativeColumn(1.5f);
                        c.RelativeColumn(1.4f);
                        c.ConstantColumn(48);
                        c.ConstantColumn(40);
                        c.ConstantColumn(40);
                        c.ConstantColumn(40);
                        c.ConstantColumn(40);
                        c.ConstantColumn(40);
                        c.ConstantColumn(38);
                        c.ConstantColumn(38);
                        c.ConstantColumn(38);
                        c.ConstantColumn(50);
                        c.RelativeColumn(1.6f);
                    });

                    table.Header(header =>
                    {
                        foreach (var h in new[] { "كود", "الاسم", "القسم", "الوردية", "التاريخ", "بداية", "نهاية", "دخول", "خروج", "ساعات العمل", "تأخير", "إضافي خام", "إضافي معتمد", "الحالة", "ملاحظات" })
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(h).FontSize(7).Bold().AlignCenter();
                    });

                    foreach (var r in rows)
                    {
                        Cell(table, r.EmployeeCode);
                        Cell(table, r.EmployeeName);
                        Cell(table, r.Department ?? "-");
                        Cell(table, r.ShiftName ?? "-");
                        Cell(table, r.WorkDate.ToString("yyyy-MM-dd"));
                        Cell(table, r.ShiftStart.ToString("HH:mm"));
                        Cell(table, r.ShiftEnd.ToString("HH:mm"));
                        Cell(table, r.CheckIn?.ToString("HH:mm") ?? "-");
                        Cell(table, r.CheckOut?.ToString("HH:mm") ?? "-");
                        Cell(table, r.WorkedHours.ToString("0.##"));
                        Cell(table, r.LateMinutes.ToString());
                        Cell(table, r.RawOvertimeHours.ToString("0.##"));
                        Cell(table, r.ApprovedOvertimeHours.ToString("0.##"));
                        Cell(table, ExcelExportService.AttendanceText(r.Status));
                        Cell(table, r.Notes ?? "-");
                    }
                });

                page.Footer().AlignCenter().Text($"طُبع آليًا — {DateTime.Now:yyyy-MM-dd HH:mm}")
                    .FontSize(7).FontColor(Colors.Grey.Darken1);
            });
        });

        return doc.GeneratePdf();
    }

    // ===== أدوات مساعدة =====

    private static void Cell(TableDescriptor table, string text, bool bold = false)
    {
        var cell = table.Cell().BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2).Padding(3);
        var t = cell.Text(text ?? "-").FontSize(7);
        if (bold) t.Bold();
    }

    private static void Info(RowDescriptor row, string label, string value)
    {
        row.RelativeItem().Column(c =>
        {
            c.Item().Text(label).FontSize(7).FontColor(Colors.Grey.Darken1);
            c.Item().Text(value).FontSize(9).Bold();
        });
    }

    private static void Head(TableDescriptor t, string a, string b, string c, string d)
    {
        foreach (var h in new[] { a, b, c, d })
            t.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(h).FontSize(8).Bold().AlignCenter();
    }

    private static void Head2(TableDescriptor t, string a, string b)
    {
        foreach (var h in new[] { a, b })
            t.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(h).FontSize(8).Bold().AlignCenter();
    }

    private static void Row2(TableDescriptor t, string a, string b, string c, string d)
    {
        Cell(t, a, false);
        Cell(t, b);
        Cell(t, c);
        Cell(t, d);
    }

    private static void Line(TableDescriptor t, string label, string value, bool bold = false)
    {
        t.Cell().BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2).Padding(3)
            .Text(label).FontSize(9).Bold()!.FontColor(Colors.Black);
        Cell(t, value, bold);
    }
}
