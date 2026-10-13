using ClosedXML.Excel;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

/// <summary>مرشح تصدير البصمات الخام.</summary>
public sealed class ExportFilter
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int? EmployeeId { get; set; }
    public int? ShiftId { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public string? Department { get; set; }

    public string PeriodText => $"من {From:yyyy-MM-dd} إلى {To:yyyy-MM-dd}";
}

/// <summary>
/// إنشاء ملفات Excel من بيانات قاعدة البيانات الفعلية.
/// كل قيمة معروضة على الشاشة تُصدَّر بنفس الحسابات المخزّنة.
/// </summary>
public class ExcelExportService
{
    private const string DateFormat = "yyyy-mm-dd";
    private const string DateTimeFormat = "yyyy-mm-dd hh:mm";
    private const string TimeFormat = "hh:mm";
    private const string MoneyFormat = "#,##0.00";

    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly ReportService _reports;

    public ExcelExportService(AppDbContext db, SettingsService settings, ReportService reports)
    {
        _db = db;
        _settings = settings;
        _reports = reports;
    }

    // ======================= التقارير الرئيسية =======================

    /// <summary>تقرير الحضور اليومي (نفس الصفوف المعروضة على الشاشة).</summary>
    public async Task<byte[]> ExportDailyAttendanceAsync(AttendanceReportFilter filter, CancellationToken ct = default)
    {
        var rows = await _reports.GetDailyAttendanceAsync(filter, ct);
        using var wb = new XLWorkbook();

        AddSheet(wb, "Attendance", "الحضور اليومي", filter.PeriodText,
            new[]
            {
                "م", "كود الموظف", "الاسم", "القسم", "المسمى الوظيفي", "التاريخ", "الوردية",
                "بداية الوردية", "نهاية الوردية", "الدخول", "الخروج", "ساعات العمل",
                "تأخير (دقيقة)", "انصراف مبكر (دقيقة)", "إضافي خام (ساعة)", "إضافي معتمد (ساعة)",
                "الحالة", "حالة المراجعة", "بصمات ناقصة", "ملاحظات"
            },
            rows.Select((r, i) => new object?[]
            {
                i + 1, r.EmployeeCode, r.EmployeeName, r.Department, r.JobTitle, r.WorkDate, r.ShiftName,
                r.ShiftStart.ToString("HH:mm"), r.ShiftEnd.ToString("HH:mm"),
                r.CheckIn?.ToString("HH:mm"), r.CheckOut?.ToString("HH:mm"), r.WorkedHours,
                r.LateMinutes, r.EarlyLeaveMinutes, r.RawOvertimeHours, r.ApprovedOvertimeHours,
                AttendanceText(r.Status), StatusText(r.PunchReviewStatus), r.HasIncompletePunches ? "نعم" : "لا", r.Notes
            }),
            summary: $"إجمالي الصفوف: {rows.Count} | حضور فعلي: {rows.Count(r => r.CheckIn != null)} | تأخير: {rows.Count(r => r.LateMinutes > 0)} | يحتاج مراجعة: {rows.Count(r => r.NeedsReview)}",
            dateFormatColumns: new[] { 6 });

        return ToBytes(wb);
    }

    /// <summary>تقرير الأقسام.</summary>
    public async Task<byte[]> ExportDepartmentReportAsync(AttendanceReportFilter filter, CancellationToken ct = default)
    {
        var rows = await _reports.GetDepartmentReportAsync(filter, ct);
        using var wb = new XLWorkbook();

        AddSheet(wb, "Departments", "ملخص الأقسام", filter.PeriodText,
            new[] { "القسم", "عدد الموظفين", "حضور", "غياب", "تأخير", "بصمات ناقصة", "إجازات", "راحة/عطلة", "نسبة الحضور %" },
            rows.Select(r => new object?[]
            {
                r.Department, r.Total, r.Present, r.Absent, r.Late, r.Incomplete, r.OnLeave, r.RestOrHoliday, r.PresentRate
            }),
            summary: $"عدد الأقسام: {rows.Count}");

        AddSheet(wb, "Attendance", "تفاصيل الحضور", filter.PeriodText,
            new[] { "القسم", "كود الموظف", "الاسم", "التاريخ", "الدخول", "الخروج", "الحالة", "تأخير (دقيقة)" },
            rows.SelectMany(d => d.Rows).Select(r => new object?[]
            {
                r.Department, r.EmployeeCode, r.EmployeeName, r.WorkDate,
                r.CheckIn?.ToString("HH:mm"), r.CheckOut?.ToString("HH:mm"), AttendanceText(r.Status), r.LateMinutes
            }),
            summary: null,
            dateFormatColumns: new[] { 4 });

        return ToBytes(wb);
    }

    /// <summary>تقرير الرواتب الكامل لكشف محسوب.</summary>
    public async Task<byte[]> ExportPayrollAsync(int payrollRunId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        await AddPayrollSheetAsync(wb, payrollRunId, ct);
        return ToBytes(wb);
    }

    /// <summary>بناء ورقة كشف الرواتب داخل مصنّف قائم.</summary>
    private async Task AddPayrollSheetAsync(XLWorkbook wb, int payrollRunId, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == payrollRunId, ct);
        if (run is null) throw new InvalidOperationException("كشف الرواتب غير موجود");

        var rows = await _reports.GetPayrollRowsAsync(payrollRunId, ct: ct);
        var settings = await _settings.GetAllAsync(ct);
        var currency = settings.TryGetValue(SettingKeys.Currency, out var c) ? c : "EGP";

        AddSheet(wb, "Payroll", $"كشف رواتب {run.Year}-{run.Month:00}", $"الإصدار {run.Version} | العملة {currency}",
            new[]
            {
                "م", "كود الموظف", "الاسم", "القسم", "المسمى", "الراتب الأساسي", "الأجر اليومي", "أجر الساعة",
                "أيام حضور", "راحة مدفوعة", "إجازات مدفوعة", "إجازات بدون أجر", "أيام غياب", "تأخير (دقيقة)",
                "إضافي خام (ساعة)", "إضافي معتمد (ساعة)", "إضافي مدفوع (ساعة)", "قيمة الإضافي",
                "مكافآت", "بدلات", "أيام إضافية", "قيمة الأيام الإضافية",
                "خصم تأخير", "خصم غياب", "خصم إجازة بدون أجر", "سلف", "خصومات", "خصومات أخرى",
                "إجمالي الاستحقاقات", "إجمالي الخصومات", "صافي الراتب", "الحالة"
            },
            rows.Select((r, i) => new object?[]
            {
                i + 1, r.EmployeeCode, r.EmployeeName, r.Department, r.JobTitle, r.BasicSalary, r.DailyRate, r.HourlyRate,
                r.AttendanceDays, r.PaidRestDays, r.PaidLeaveDays, r.UnpaidLeaveDays, r.AbsenceDays, r.LateMinutes,
                r.RawOvertimeHours, r.ApprovedOvertimeHours, r.PaidOvertimeHours, r.OvertimeAmount,
                r.Bonuses, r.Allowances, r.AdditionalDaysCount, r.AdditionalDaysAmount,
                r.LateDeduction, r.AbsenceDeduction, r.UnpaidLeaveDeduction, r.LoansTotal, r.DeductionsTotal, r.OtherDeductions,
                r.GrossEarnings, r.TotalDeductions, r.NetSalary, PayrollStatusText(r.Status)
            }),
            summary: $"عدد الموظفين: {rows.Count} | إجمالي الاستحقاقات: {rows.Sum(r => r.GrossEarnings):N2} | إجمالي الخصومات: {rows.Sum(r => r.TotalDeductions):N2} | صافي الكشف: {rows.Sum(r => r.NetSalary):N2} {currency}",
            moneyColumns: Enumerable.Range(6, 3).Concat(Enumerable.Range(18, 11)).ToArray(),
            decimalColumns: new[] { 11, 12, 13, 15, 16, 17, 21 });
    }

    /// <summary>كشف راتب موظف واحد (Payslip) بصيغة Excel.</summary>
    public async Task<byte[]> ExportPayslipAsync(int payrollItemId, CancellationToken ct = default)
    {
        var slip = await _reports.GetPayslipAsync(payrollItemId, ct)
                   ?? throw new InvalidOperationException("سطر الراتب غير موجود");
        var item = slip.Item;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Payslip");
        ws.RightToLeft = true;
        var r = 1;

        void Line(string label, object? value)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 1).Style.Font.Bold = true;
            setValue(ws.Cell(r, 2), value);
            r++;
        }

        void Section(string title)
        {
            r++;
            var cell = ws.Cell(r, 1);
            cell.Value = title;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
            ws.Range(r, 1, r, 2).Merge();
            r++;
        }

        ws.Cell(1, 1).Value = slip.CompanyName;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 2).Merge();
        ws.Cell(2, 1).Value = $"كشف راتب موظف — {slip.Year}-{slip.Month:00}";
        ws.Range(2, 1, 2, 2).Merge();
        r = 4;

        Section("بيانات الموظف");
        Line("كود الموظف", item.EmployeeCode);
        Line("الاسم", item.EmployeeName);
        Line("القسم", item.Department);
        Line("المسمى الوظيفي", item.JobTitle);

        Section("الأجر وأساس الحساب");
        Line("الراتب الأساسي", item.BasicSalary);
        Line("الأجر اليومي", item.DailyRate);
        Line("أجر الساعة", item.HourlyRate);
        Line("عدد أيام الشهر المستخدم", item.DailyRate == 0 ? 30 : Math.Round(item.BasicSalary / item.DailyRate, 2));
        Line("إجمالي ساعات اليوم", 8);

        Section("الحضور");
        Line("أيام الحضور", item.AttendanceDays);
        Line("أيام راحة مدفوعة (مشمولة بالراتب)", item.PaidRestDays);
        Line("إجازات مدفوعة", item.PaidLeaveDays);
        Line("إجازات بدون أجر", item.UnpaidLeaveDays);
        Line("أيام غياب", item.AbsenceDays);
        Line("دقائق التأخير", item.LateMinutes);

        Section("الإضافي");
        Line("إضافي خام (ساعة)", item.RawOvertimeHours);
        Line("إضافي معتمد (ساعة)", item.ApprovedOvertimeHours);
        Line("إضافي مدفوع (ساعة)", item.PaidOvertimeHours);
        Line("معامل الإضافي", 1);
        Line("قيمة الإضافي", item.OvertimeAmount);

        Section("الاستحقاقات");
        Line("الراتب الأساسي", item.BasicSalary);
        Line("مكافآت", item.Bonuses);
        Line("بدلات", item.Allowances);
        Line($"أيام إضافية ({item.AdditionalDaysCount})", item.AdditionalDaysAmount);
        Line("إجمالي الاستحقاقات", item.GrossEarnings);

        Section("الخصومات");
        Line("خصم تأخير", item.LateDeduction);
        Line("خصم غياب", item.AbsenceDeduction);
        Line("خصم إجازة بدون أجر", item.UnpaidLeaveDeduction);
        Line("سلف", item.LoansTotal);
        Line("خصومات", item.DeductionsTotal);
        Line("خصومات أخرى", item.OtherDeductions);
        Line("إجمالي الخصومات", item.TotalDeductions);

        Section("الصافي");
        Line($"صافي الراتب ({slip.Currency})", item.NetSalary);

        if (slip.Notes.Count > 0)
        {
            Section("ملاحظات الحساب");
            foreach (var n in slip.Notes) Line("•", n);
        }

        ws.Column(1).Width = 34;
        ws.Column(2).Width = 34;
        foreach (var row in ws.RowsUsed())
            row.Cells().Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cells().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        return ToBytes(wb);
    }

    /// <summary>تقرير الإضافي.</summary>
    public async Task<byte[]> ExportOvertimeAsync(AttendanceReportFilter filter, CancellationToken ct = default)
    {
        var rows = await _reports.GetOvertimeAsync(filter, onlyWithOvertime: true, ct: ct);
        using var wb = new XLWorkbook();

        AddSheet(wb, "Overtime", "تقرير الإضافي", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "القسم", "التاريخ", "الوردية", "نهاية الوردية", "الخروج الفعلي", "إضافي خام (دقيقة)", "إضافي معتمد (دقيقة)", "معتمد؟" },
            rows.Select((r, i) => new object?[]
            {
                i + 1, r.EmployeeCode, r.EmployeeName, r.Department, r.WorkDate, r.ShiftName,
                r.ShiftEnd.ToString("HH:mm"), r.CheckOut?.ToString("HH:mm"),
                r.RawMinutes, r.ApprovedMinutes, r.IsApproved ? "نعم" : "لا"
            }),
            summary: $"إجمالي الساعات المعتمدة: {Math.Round(rows.Sum(r => r.ApprovedMinutes) / 60m, 2)}",
            dateFormatColumns: new[] { 5 });

        return ToBytes(wb);
    }

    /// <summary>تقرير الغياب والتأخير والبصمات الناقصة.</summary>
    public async Task<byte[]> ExportAbsenceAsync(AttendanceReportFilter filter, CancellationToken ct = default)
    {
        var rows = await _reports.GetAbsenceAndLatenessAsync(filter, ct);
        using var wb = new XLWorkbook();

        AddSheet(wb, "Absence", "الغياب والتأخير", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "القسم", "التاريخ", "الحالة", "تأخير (دقيقة)", "انصراف مبكر (دقيقة)", "بصمات ناقصة", "ملاحظات" },
            rows.Select((r, i) => new object?[]
            {
                i + 1, r.EmployeeCode, r.EmployeeName, r.Department, r.WorkDate,
                AttendanceText(r.Status), r.LateMinutes, r.EarlyLeaveMinutes, r.HasIncompletePunches ? "نعم" : "لا", r.Notes
            }),
            summary: $"إجمالي الحالات: {rows.Count} | غياب: {rows.Count(r => r.Status is AttendanceStatus.Absent or AttendanceStatus.UnexcusedAbsence)} | حالة مراجعة: {rows.Count(r => r.HasIncompletePunches)}",
            dateFormatColumns: new[] { 5 });

        return ToBytes(wb);
    }

    /// <summary>البصمات الخام كما وردت من الجهاز.</summary>
    public async Task<byte[]> ExportRawPunchesAsync(ExportFilter filter, CancellationToken ct = default)
    {
        var raw = await GetRawPunchesAsync(filter, ct);
        using var wb = new XLWorkbook();

        AddSheet(wb, "Raw Punches", "البصمات الخام المستوردة", filter.PeriodText,
            new[] { "م", "كود الجهاز", "كود الموظف", "الاسم", "رقم الجهاز", "نوع البصمة", "وقت البصمة", "النص الأصلي", "رقم الصف", "الموظف مسجل؟" },
            raw.Select((r, i) => new object?[]
            {
                i + 1, r.DeviceCode, r.Code, r.Name, r.DeviceId, r.AttType, r.PunchTime, r.RawText, r.RowNumber,
                r.EmployeeId.HasValue ? "نعم" : "لا"
            }),
            summary: $"إجمالي البصمات الخام: {raw.Count}",
            dateTimeColumns: new[] { 7 });

        return ToBytes(wb);
    }

    /// <summary>
    /// الملف الشامل: يحوي الأوراق الثمانية المعتمدة بنفس الترتيب:
    /// Raw Punches, Entry, Exit, Daily Attendance, Late Report, Overtime Report, Payroll, Exceptions.
    /// </summary>
    public async Task<byte[]> ExportFullWorkbookAsync(AttendanceReportFilter filter, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();

        // 1) البصمات الخام كما وردت من الجهاز
        var raw = await GetRawPunchesAsync(new ExportFilter
        {
            From = filter.From, To = filter.To, EmployeeId = filter.EmployeeId,
            ShiftId = filter.ShiftId, Department = filter.Department
        }, ct);
        AddSheet(wb, "Raw Punches", "البصمات الخام المستوردة", filter.PeriodText,
            new[] { "م", "كود الجهاز", "كود الموظف", "الاسم", "رقم الجهاز", "نوع البصمة", "وقت البصمة", "النص الأصلي", "رقم الصف", "الموظف مسجل؟" },
            raw.Select((r, i) => new object?[]
            {
                i + 1, r.DeviceCode, r.Code, r.Name, r.DeviceId, r.AttType, r.PunchTime, r.RawText, r.RowNumber,
                r.EmployeeId.HasValue ? "نعم" : "لا"
            }),
            summary: $"إجمالي البصمات الخام: {raw.Count}",
            dateTimeColumns: new[] { 7 });

        // 2) و 3) سجلات الدخول والخروج المصنفة (مرتبطة بالسجل الأصلي)
        var events = await GetEventRowsAsync(filter, ct);
        AddEventSheet(wb, AttendanceEventType.Entry, filter.PeriodText, events.Where(e => e.EventType == AttendanceEventType.Entry).ToList());
        AddEventSheet(wb, AttendanceEventType.Exit, filter.PeriodText, events.Where(e => e.EventType == AttendanceEventType.Exit).ToList());

        // 4) ملخص الحضور اليومي
        var daily = await _reports.GetDailyAttendanceAsync(filter, ct);
        AddSheet(wb, "Daily Attendance", "ملخص الحضور اليومي", filter.PeriodText,
            new[]
            {
                "م", "كود الموظف", "الاسم", "القسم", "المسمى الوظيفي", "تاريخ العمل", "الوردية",
                "الدخول", "الخروج", "ساعات العمل", "التأخير (دقيقة)", "انصراف مبكر (دقيقة)",
                "إضافي محسوب (ساعة)", "إضافي معتمد (ساعة)", "حالة الحضور", "حالة المراجعة", "ملاحظات"
            },
            daily.Select((d, i) => new object?[]
            {
                i + 1, d.EmployeeCode, d.EmployeeName, d.Department, d.JobTitle, d.WorkDate, d.ShiftName,
                d.CheckIn?.ToString("HH:mm"), d.CheckOut?.ToString("HH:mm"), d.WorkedHours,
                d.LateMinutes, d.EarlyLeaveMinutes, d.RawOvertimeHours, d.ApprovedOvertimeHours,
                AttendanceText(d.Status), StatusText(d.PunchReviewStatus), d.Notes
            }),
            summary: $"إجمالي أيام الحضور: {daily.Count} | إجمالي دقائق التأخير: {daily.Sum(d => d.LateMinutes)} | إجمالي ساعات الإضافي المعتمد: {Math.Round(daily.Sum(d => d.ApprovedOvertimeMinutes) / 60m, 2)}",
            dateFormatColumns: new[] { 6 });

        // 5) تقرير التأخير
        var lateRows = daily.Where(d => d.LateMinutes > 0).ToList();
        AddSheet(wb, "Late Report", "تقرير التأخير", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "القسم", "تاريخ العمل", "الوردية", "وقت الدخول", "دقائق التأخير", "ملاحظات" },
            lateRows.Select((d, i) => new object?[]
            {
                i + 1, d.EmployeeCode, d.EmployeeName, d.Department, d.WorkDate, d.ShiftName,
                d.CheckIn?.ToString("HH:mm"), d.LateMinutes, d.Notes
            }),
            summary: $"إجمالي الحالات المتأخرة: {lateRows.Count} | إجمالي الدقائق: {lateRows.Sum(d => d.LateMinutes)}",
            dateFormatColumns: new[] { 5 });

        // 6) تقرير الإضافي
        var overtime = await _reports.GetOvertimeAsync(filter, onlyWithOvertime: true, ct: ct);
        AddSheet(wb, "Overtime Report", "تقرير الإضافي", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "القسم", "التاريخ", "الوردية", "نهاية الوردية", "الخروج الفعلي", "إضافي محسوب (ساعة)", "إضافي معتمد (ساعة)", "معتمد؟" },
            overtime.Select((r, i) => new object?[]
            {
                i + 1, r.EmployeeCode, r.EmployeeName, r.Department, r.WorkDate, r.ShiftName,
                r.ShiftEnd.ToString("HH:mm"), r.CheckOut?.ToString("HH:mm"),
                Math.Round(r.RawMinutes / 60m, 2), r.DecimalHours, r.IsApproved ? "نعم" : "لا"
            }),
            summary: $"إجمالي الساعات المحسوبة: {Math.Round(overtime.Sum(r => r.RawMinutes) / 60m, 2)} | المعتمدة: {Math.Round(overtime.Sum(r => r.ApprovedMinutes) / 60m, 2)}",
            dateFormatColumns: new[] { 5 }, decimalColumns: new[] { 9, 10 });

        // 7) كشف رواتب الشهر الذي تنتهي فيه الفترة
        await AddPayrollSheetForPeriodAsync(wb, filter, ct);

        // 8) الاستثناءات
        var exceptions = await GetExceptionRowsAsync(filter, ct);
        AddSheet(wb, "Exceptions", "الاستثناءات والبصمات غير المصنفة", filter.PeriodText,
            new[] { "م", "النوع", "كود الموظف", "الاسم", "التاريخ/الوقت", "الوصف" },
            exceptions.Select((e, i) => new object?[] { i + 1, e.Kind, e.Code, e.Name, e.When, e.Description }),
            summary: $"إجمالي الاستثناءات: {exceptions.Count}",
            dateTimeColumns: new[] { 5 });

        return ToBytes(wb);
    }

    /// <summary>كشف رواتب شهر الفترة (أحدث إصدار غير ملغى).</summary>
    private async Task AddPayrollSheetForPeriodAsync(XLWorkbook wb, AttendanceReportFilter f, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Year == f.To.Year && r.Month == f.To.Month && r.Status != PayrollStatus.Cancelled)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        if (run is null)
        {
            AddSheet(wb, "Payroll", "كشف الرواتب", f.PeriodText,
                new[] { "لا يوجد كشف رواتب", "احتسب الكشف من شاشة الرواتب ثم أعد التصدير" },
                Enumerable.Empty<object?[]>(), summary: null);
            return;
        }

        await AddPayrollSheetAsync(wb, run.Id, ct);
    }

    /// <summary>تصدير سجلات الدخول أو الخروج المصنفة في ملف منفرد.</summary>
    public async Task<byte[]> ExportEventsAsync(AttendanceReportFilter filter, AttendanceEventType type, CancellationToken ct = default)
    {
        var rows = (await GetEventRowsAsync(filter, ct)).Where(e => e.EventType == type).ToList();
        using var wb = new XLWorkbook();
        AddEventSheet(wb, type, filter.PeriodText, rows);
        return ToBytes(wb);
    }

    /// <summary>قالب Excel فارغ بعناوين الأعمدة المطلوبة لاستيراد الموظفين.</summary>
    public Task<byte[]> BuildEmployeeImportTemplateAsync(CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Employees");
        ws.RightToLeft = true;

        var headers = new[]
        {
            "كود الموظف", "كود البصمة", "الاسم", "القسم", "المسمى الوظيفي",
            "الراتب الأساسي", "الأجر اليومي", "تاريخ التعيين", "الهاتف", "البريد الإلكتروني",
            "الرقم القومي", "ساعات العمل", "استحقاق الإجازة", "ملاحظات"
        };
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // صف مثال يوضّح الصيغة المطلوبة ثم يُحذف قبل الاستيراد
        var sample = new object?[] { "1001", "1001", "محمد أحمد", "المالية", "محاسب", 9000, "", "2024-01-15", "01000000000", "emp@example.com", "", 8, 21, "" };
        for (var c = 0; c < sample.Length; c++) setValue(ws.Cell(2, c + 1), sample[c]);

        ws.Row(2).Style.Font.FontColor = XLColor.Gray;
        ws.Columns(1, headers.Length).AdjustToContents();
        return Task.FromResult(ToBytes(wb));
    }

    // ======================= الاستعلامات =======================

    private sealed class RawRow
    {
        public string DeviceCode { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? DeviceId { get; set; }
        public string? AttType { get; set; }
        public DateTime PunchTime { get; set; }
        public string? RawText { get; set; }
        public int? RowNumber { get; set; }
        public int? EmployeeId { get; set; }
    }

    private async Task<List<RawRow>> GetRawPunchesAsync(ExportFilter f, CancellationToken ct)
    {
        var q = _db.RawPunches.AsNoTracking()
            .Include(p => p.Employee)
            .Where(p => p.PunchTime >= f.From.Date && p.PunchTime <= f.To.Date.AddDays(1));

        if (f.EmployeeId.HasValue) q = q.Where(p => p.EmployeeId == f.EmployeeId.Value);
        if (!string.IsNullOrWhiteSpace(f.Department)) q = q.Where(p => p.Employee!.Department == f.Department);

        return await q.OrderBy(p => p.PunchTime)
            .Select(p => new RawRow
            {
                DeviceCode = p.DeviceEmployeeCode,
                Code = p.Employee != null ? p.Employee.EmployeeCode : null,
                Name = p.RawEmployeeName ?? (p.Employee != null ? p.Employee.FullName : null),
                DeviceId = p.DeviceId,
                AttType = p.AttType,
                PunchTime = p.PunchTime,
                RawText = p.RawPunchTimeText,
                RowNumber = p.RowNumber,
                EmployeeId = p.EmployeeId
            })
            .ToListAsync(ct);
    }

    // ======================= سجلات الدخول/الخروج والاستثناءات =======================

    private sealed class EventRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string? DeviceId { get; set; }
        public int RawPunchId { get; set; }
        public AttendanceEventType EventType { get; set; }
        public DateTime EventTime { get; set; }
        public ClassificationStatus ClassificationStatus { get; set; }
        public ClassificationSource ClassificationSource { get; set; }
        public string? Note { get; set; }
    }

    private sealed class ExceptionRow
    {
        public string Kind { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime When { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>أعمدة ورقتي Entry/Exit مطابقة للمتطلب.</summary>
    private static readonly string[] EventSheetHeaders =
    {
        "م", "كود الموظف", "الاسم", "التاريخ", "الوقت", "رقم الجهاز", "رقم السجل الأصلي", "حالة التصنيف", "مصدر التصنيف", "ملاحظة"
    };

    /// <summary>البصمات المصنفة دخول/خروج مع رابطها بالسجل الأصلي في RawPunches.</summary>
    private async Task<List<EventRow>> GetEventRowsAsync(AttendanceReportFilter f, CancellationToken ct)
    {
        var q = _db.AttendanceEvents.AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.RawPunch)
            .Where(a => a.WorkDate >= f.From.Date && a.WorkDate <= f.To.Date);

        if (f.EmployeeId.HasValue) q = q.Where(a => a.EmployeeId == f.EmployeeId.Value);
        if (f.ShiftId.HasValue) q = q.Where(a => a.ShiftId == f.ShiftId.Value);
        if (!string.IsNullOrWhiteSpace(f.Department)) q = q.Where(a => a.Employee!.Department == f.Department);

        var list = await q.OrderBy(a => a.EventTime).ThenBy(a => a.Id).ToListAsync(ct);
        return list.Select(a => new EventRow
        {
            EmployeeId = a.EmployeeId,
            EmployeeCode = a.Employee?.EmployeeCode ?? a.RawPunch?.DeviceEmployeeCode ?? string.Empty,
            EmployeeName = a.Employee?.FullName ?? a.RawPunch?.RawEmployeeName ?? string.Empty,
            DeviceId = a.RawPunch?.DeviceId,
            RawPunchId = a.RawPunchId,
            EventType = a.EventType,
            EventTime = a.EventTime,
            ClassificationStatus = a.ClassificationStatus,
            ClassificationSource = a.ClassificationSource,
            Note = a.Notes
        }).ToList();
    }

    private static void AddEventSheet(XLWorkbook wb, AttendanceEventType type, string periodText, List<EventRow> rows)
    {
        var isEntry = type == AttendanceEventType.Entry;
        AddSheet(wb, isEntry ? "Entry" : "Exit",
            isEntry ? "سجلات الدخول المصنفة" : "سجلات الخروج المصنفة", periodText,
            EventSheetHeaders,
            rows.Select((e, i) => new object?[]
            {
                i + 1, e.EmployeeCode, e.EmployeeName, e.EventTime.Date, e.EventTime.TimeOfDay,
                e.DeviceId, e.RawPunchId, StatusText(e.ClassificationStatus), SourceText(e.ClassificationSource), e.Note
            }),
            summary: $"إجمالي سجلات {(isEntry ? "الدخول" : "الخروج")}: {rows.Count}",
            dateFormatColumns: new[] { 4 }, timeFormatColumns: new[] { 5 });
    }

    /// <summary>كل ما يحتاج مراجعة بشرية: بصمات ناقصة/غامضة/غير مصنفة/موظف غير معروف.</summary>
    private async Task<List<ExceptionRow>> GetExceptionRowsAsync(AttendanceReportFilter f, CancellationToken ct)
    {
        var rows = new List<ExceptionRow>();

        var daily = await _db.DailyAttendances.AsNoTracking()
            .Include(d => d.Employee)
            .Where(d => d.WorkDate >= f.From.Date && d.WorkDate <= f.To.Date
                        && (d.Status == AttendanceStatus.MissingCheckIn
                            || d.Status == AttendanceStatus.MissingCheckOut
                            || d.Status == AttendanceStatus.NeedsReview
                            || d.PunchReviewStatus == ClassificationStatus.NeedsReview))
            .OrderBy(d => d.WorkDate).ThenBy(d => d.EmployeeId)
            .ToListAsync(ct);

        foreach (var d in daily)
            rows.Add(new ExceptionRow
            {
                Kind = d.Status == AttendanceStatus.MissingCheckIn ? "بصمة دخول مفقودة"
                    : d.Status == AttendanceStatus.MissingCheckOut ? "بصمة خروج مفقودة"
                    : "يحتاج مراجعة",
                Code = d.Employee?.EmployeeCode ?? string.Empty,
                Name = d.Employee?.FullName ?? string.Empty,
                When = d.WorkDate,
                Description = d.Notes ?? d.AdminNotes ?? "بصمات ناقصة أو تصنيف غير مؤكد"
            });

        foreach (var e in (await GetEventRowsAsync(f, ct))
                     .Where(e => e.EventType == AttendanceEventType.Unknown
                                 || e.ClassificationStatus == ClassificationStatus.NeedsReview))
            rows.Add(new ExceptionRow
            {
                Kind = e.EventType == AttendanceEventType.Unknown ? "بصمة غير مصنفة" : "تصنيف بحاجة لمراجعة",
                Code = e.EmployeeCode,
                Name = e.EmployeeName,
                When = e.EventTime,
                Description = e.Note ?? "تصنيف آلي غير مؤكد"
            });

        var unknown = await _db.RawPunches.AsNoTracking()
            .Where(p => p.EmployeeId == null && p.PunchTime >= f.From.Date && p.PunchTime <= f.To.Date.AddDays(1))
            .OrderBy(p => p.PunchTime)
            .ToListAsync(ct);

        foreach (var p in unknown)
            rows.Add(new ExceptionRow
            {
                Kind = "موظف غير معروف",
                Code = p.DeviceEmployeeCode,
                Name = p.RawEmployeeName ?? "غير معروف",
                When = p.PunchTime,
                Description = $"كود البصمة {p.DeviceEmployeeCode} غير مرتبط بأي موظف"
            });

        return rows.OrderBy(r => r.When).ToList();
    }

    // ======================= أدوات بناء الأوراق =======================

    private static void AddSheet(
        XLWorkbook wb, string sheetKey, string title, string subtitle,
        string[] headers, IEnumerable<object?[]> rows, string? summary,
        int[]? dateFormatColumns = null, int[]? dateTimeColumns = null, int[]? timeFormatColumns = null,
        int[]? moneyColumns = null, int[]? decimalColumns = null)
    {
        var ws = wb.AddWorksheet(sheetKey);
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(2, 1).Value = subtitle;
        if (!string.IsNullOrWhiteSpace(summary)) ws.Cell(3, 1).Value = summary;

        const int headerRow = 5;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
                setValue(ws.Cell(r, c + 1), row[c]);
            r++;
        }

        var lastRow = Math.Max(r - 1, headerRow);
        if (r > headerRow + 1)
        {
            ws.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();
            ws.Range(headerRow, 1, lastRow, headers.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        ApplyFormat(ws, dateFormatColumns, DateFormat, lastRow);
        ApplyFormat(ws, dateTimeColumns, DateTimeFormat, lastRow);
        ApplyFormat(ws, timeFormatColumns, TimeFormat, lastRow);
        ApplyFormat(ws, moneyColumns, MoneyFormat, lastRow);
        ApplyFormat(ws, decimalColumns, "#,##0.00", lastRow);

        if (headers.Length > 0)
        {
            try { ws.Columns(1, headers.Length).AdjustToContents(headerRow, lastRow); }
            catch (Exception) { /* التنسيق اختياري */ }
            for (var c = 1; c <= headers.Length; c++)
                if (ws.Column(c).Width > 40) ws.Column(c).Width = 40;
        }
        ws.SheetView.FreezeRows(headerRow);
    }

    private static void ApplyFormat(IXLWorksheet ws, int[]? columns, string format, int lastRow)
    {
        if (columns is null || lastRow < 6) return;
        foreach (var c in columns.Where(x => x > 0))
            ws.Range(6, c, lastRow, c).Style.NumberFormat.Format = format;
    }

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void setValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: cell.Value = string.Empty; break;
            case string s: cell.Value = s; break;
            case DateTime dt: cell.Value = dt; break;
            case bool b: cell.Value = b; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case decimal d: cell.Value = d; break;
            case double db: cell.Value = db; break;
            default: cell.Value = value.ToString(); break;
        }
    }

    // ======================= نصوص العرض =======================

    public static string StatusText(ClassificationStatus s) => s switch
    {
        ClassificationStatus.Classified => "مصنف",
        ClassificationStatus.NeedsReview => "يحتاج مراجعة",
        _ => "غير مصنف"
    };

    public static string SourceText(ClassificationSource s) => s switch
    {
        ClassificationSource.ExplicitDeviceType => "نوع الحركة من الجهاز",
        ClassificationSource.Rule => "قواعد آلية",
        ClassificationSource.Manual => "مراجعة يدوية",
        _ => "-"
    };

    public static string AttendanceText(AttendanceStatus s) => s switch
    {
        AttendanceStatus.Present => "حاضر",
        AttendanceStatus.Absent => "غائب",
        AttendanceStatus.Late => "متأخر",
        AttendanceStatus.MissingCheckIn => "دخول مفقود",
        AttendanceStatus.MissingCheckOut => "خروج مفقود",
        AttendanceStatus.Leave => "إجازة",
        AttendanceStatus.RestDay => "يوم راحة",
        AttendanceStatus.Holiday => "عطلة رسمية",
        AttendanceStatus.NeedsReview => "يحتاج مراجعة",
        AttendanceStatus.EarlyDeparture => "انصراف مبكر",
        AttendanceStatus.PaidLeave => "إجازة مدفوعة",
        AttendanceStatus.UnpaidLeave => "إجازة بدون أجر",
        AttendanceStatus.UnexcusedAbsence => "غياب بدون عذر",
        _ => s.ToString()
    };

    public static string PayrollStatusText(PayrollStatus s) => s switch
    {
        PayrollStatus.Draft => "مسودة",
        PayrollStatus.Computed => "محسوب",
        PayrollStatus.Approved => "معتمد",
        PayrollStatus.Cancelled => "ملغى",
        PayrollStatus.PendingReview => "قيد المراجعة",
        PayrollStatus.Locked => "مقفل",
        PayrollStatus.Reopened => "أُعيد فتحه",
        _ => s.ToString()
    };
}
