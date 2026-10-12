using ClosedXML.Excel;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

public sealed class ExportFilter
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int? EmployeeId { get; set; }
    public int? ShiftId { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }

    public string PeriodText => $"من {From:yyyy-MM-dd} إلى {To:yyyy-MM-dd}";
}

/// <summary>
/// إنشاء ملفات Excel متعددة الأوراق (ClosedXML) لكل التقارير.
/// </summary>
public class ExcelExportService
{
    private const string DateFormat = "yyyy-mm-dd";
    private const string DateTimeFormat = "yyyy-mm-dd hh:mm:ss";
    private const string MoneyFormat = "#,##0.00";

    private readonly AppDbContext _db;
    private readonly SettingsService _settings;

    public ExcelExportService(AppDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    // ============================ التصدير الكامل ============================
    public async Task<byte[]> ExportFullAsync(ExportFilter filter, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();

        var raw = await GetRawPunchesAsync(filter, ct);
        AddSheet(wb, "Raw Punches", "البصمات الخام المستوردة", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "رقم الجهاز", "نوع البصمة", "وقت البصمة", "النص الأصلي للوقت", "رقم الصف", "الموظف مسجل؟" },
            raw.Select((r, i) => new object?[]
            {
                i + 1, r.Code, r.Name, r.DeviceId, r.AttType, r.PunchTime, r.RawText, r.RowNumber,
                r.EmployeeId.HasValue ? "نعم" : "لا"
            }),
            summary: $"إجمالي البصمات الخام: {raw.Count}",
            dateTimeColumns: new[] { 6 });

        var events = await GetEventsAsync(filter, ct);
        var entries = events.Where(e => e.EventType == AttendanceEventType.Entry).ToList();
        var exits = events.Where(e => e.EventType == AttendanceEventType.Exit).ToList();

        AddSheet(wb, "Entry", "سجلات الدخول المصنفة", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "التاريخ", "الوقت", "رقم الجهاز", "رقم السجل الأصلي", "حالة التصنيف", "مصدر التصنيف", "ملاحظة" },
            entries.Select((e, i) => new object?[]
            {
                i + 1, e.Code, e.Name, e.EventTime.Date, e.EventTime.TimeOfDay, e.DeviceId, e.RawPunchId,
                StatusText(e.ClassificationStatus), SourceText(e.ClassificationSource), e.Note
            }),
            summary: $"إجمالي سجلات الدخول: {entries.Count}",
            dateFormatColumns: new[] { 4 }, timeFormatColumns: new[] { 5 });

        AddSheet(wb, "Exit", "سجلات الخروج المصنفة", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "التاريخ", "الوقت", "رقم الجهاز", "رقم السجل الأصلي", "حالة التصنيف", "مصدر التصنيف", "ملاحظة" },
            exits.Select((e, i) => new object?[]
            {
                i + 1, e.Code, e.Name, e.EventTime.Date, e.EventTime.TimeOfDay, e.DeviceId, e.RawPunchId,
                StatusText(e.ClassificationStatus), SourceText(e.ClassificationSource), e.Note
            }),
            summary: $"إجمالي سجلات الخروج: {exits.Count}",
            dateFormatColumns: new[] { 4 }, timeFormatColumns: new[] { 5 });

        var daily = await GetDailyAsync(filter, ct);
        AddSheet(wb, "Daily Attendance", "ملخص الحضور اليومي", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "تاريخ العمل", "الوردية", "وقت الدخول", "وقت الخروج",
                    "التأخير (دقيقة)", "انصراف مبكر (دقيقة)", "إضافي محسوب (ساعة)", "إضافي معتمد (ساعة)",
                    "حالة الحضور", "حالة المراجعة", "ملاحظات" },
            daily.Select((d, i) => new object?[]
            {
                i + 1, d.Code, d.Name, d.WorkDate.Date, d.ShiftName, d.CheckIn, d.CheckOut,
                d.LateMinutes, d.EarlyLeaveMinutes, Math.Round(d.CalculatedOvertimeMinutes / 60m, 2), Math.Round(d.ApprovedOvertimeMinutes / 60m, 2),
                AttendanceText(d.Status), ReviewText(d.PunchReviewStatus), d.Notes
            }),
            summary: $"إجمالي أيام الحضور: {daily.Count} | إجمالي دقائق التأخير: {daily.Sum(d => d.LateMinutes)} | إجمالي ساعات الإضافي المعتمد: {Math.Round(daily.Sum(d => d.ApprovedOvertimeMinutes) / 60m, 2)}",
            dateFormatColumns: new[] { 4 }, dateTimeColumns: new[] { 6, 7 });

        var lateRows = daily.Where(d => d.LateMinutes > 0).ToList();
        AddSheet(wb, "Late Report", "تقرير التأخير", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "تاريخ العمل", "الوردية", "وقت الدخول", "دقائق التأخير", "ملاحظات" },
            lateRows.Select((d, i) => new object?[] { i + 1, d.Code, d.Name, d.WorkDate.Date, d.ShiftName, d.CheckIn, d.LateMinutes, d.Notes }),
            summary: $"إجمالي الحالات المتأخرة: {lateRows.Count} | إجمالي الدقائق: {lateRows.Sum(d => d.LateMinutes)}",
            dateFormatColumns: new[] { 4 }, dateTimeColumns: new[] { 6 });

        var otRows = daily.Where(d => d.CalculatedOvertimeMinutes > 0 || d.ApprovedOvertimeMinutes > 0).ToList();
        AddSheet(wb, "Overtime Report", "تقرير الإضافي", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "تاريخ العمل", "الوردية", "نهاية الوردية", "وقت الخروج", "إضافي محسوب (ساعة)", "إضافي معتمد (ساعة)" },
            otRows.Select((d, i) => new object?[] { i + 1, d.Code, d.Name, d.WorkDate.Date, d.ShiftName, d.ShiftEnd, d.CheckOut, Math.Round(d.CalculatedOvertimeMinutes / 60m, 2), Math.Round(d.ApprovedOvertimeMinutes / 60m, 2) }),
            summary: $"إجمالي الساعات المحسوبة: {Math.Round(otRows.Sum(d => d.CalculatedOvertimeMinutes) / 60m, 2)} | المعتمدة: {Math.Round(otRows.Sum(d => d.ApprovedOvertimeMinutes) / 60m, 2)}",
            dateFormatColumns: new[] { 4 }, dateTimeColumns: new[] { 6, 7 }, decimalColumns: new[] { 8, 9 });

        await AddPayrollSheetAsync(wb, filter, ct);

        var exceptions = await GetExceptionsAsync(filter, ct);
        AddSheet(wb, "Exceptions", "الاستثناءات والبصمات غير المصنفة", filter.PeriodText,
            new[] { "م", "النوع", "كود الموظف", "الاسم", "التاريخ/الوقت", "الوصف" },
            exceptions.Select((e, i) => new object?[] { i + 1, e.Kind, e.Code, e.Name, e.When, e.Description }),
            summary: $"إجمالي الاستثناءات: {exceptions.Count}");

        return ToBytes(wb);
    }

    public async Task<byte[]> ExportPayrollAsync(int payrollRunId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        await AddPayrollSheetAsync(wb, new ExportFilter { From = DateTime.MinValue, To = DateTime.MaxValue }, ct, payrollRunId);
        return ToBytes(wb);
    }

    public async Task<byte[]> ExportRawAsync(ExportFilter filter, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var raw = await GetRawPunchesAsync(filter, ct);
        AddSheet(wb, "Raw Punches", "البصمات الخام المستوردة", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "رقم الجهاز", "نوع البصمة", "وقت البصمة", "النص الأصلي للوقت", "رقم الصف" },
            raw.Select((r, i) => new object?[] { i + 1, r.Code, r.Name, r.DeviceId, r.AttType, r.PunchTime, r.RawText, r.RowNumber }),
            summary: $"إجمالي البصمات: {raw.Count}", dateTimeColumns: new[] { 6 });
        return ToBytes(wb);
    }

    public async Task<byte[]> ExportEventsAsync(ExportFilter filter, AttendanceEventType type, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var events = (await GetEventsAsync(filter, ct)).Where(e => e.EventType == type).ToList();
        var title = type == AttendanceEventType.Entry ? "سجلات الدخول المصنفة" : "سجلات الخروج المصنفة";
        AddSheet(wb, type == AttendanceEventType.Entry ? "Entry" : "Exit", title, filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "التاريخ", "الوقت", "رقم الجهاز", "رقم السجل الأصلي", "حالة التصنيف", "مصدر التصنيف", "ملاحظة" },
            events.Select((e, i) => new object?[]
            {
                i + 1, e.Code, e.Name, e.EventTime.Date, e.EventTime.TimeOfDay, e.DeviceId, e.RawPunchId,
                StatusText(e.ClassificationStatus), SourceText(e.ClassificationSource), e.Note
            }),
            summary: $"الإجمالي: {events.Count}", dateFormatColumns: new[] { 4 }, timeFormatColumns: new[] { 5 });
        return ToBytes(wb);
    }

    public async Task<byte[]> ExportDailyAsync(ExportFilter filter, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var daily = await GetDailyAsync(filter, ct);
        AddSheet(wb, "Daily Attendance", "ملخص الحضور اليومي", filter.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "تاريخ العمل", "الوردية", "وقت الدخول", "وقت الخروج", "التأخير (دقيقة)", "إضافي معتمد (ساعة)", "حالة الحضور", "حالة المراجعة", "ملاحظات" },
            daily.Select((d, i) => new object?[] { i + 1, d.Code, d.Name, d.WorkDate.Date, d.ShiftName, d.CheckIn, d.CheckOut, d.LateMinutes, Math.Round(d.ApprovedOvertimeMinutes / 60m, 2), AttendanceText(d.Status), ReviewText(d.PunchReviewStatus), d.Notes }),
            summary: $"إجمالي الأيام: {daily.Count}", dateFormatColumns: new[] { 4 }, dateTimeColumns: new[] { 6, 7 });
        return ToBytes(wb);
    }

    // ============================ الاستعلامات ============================
    private IQueryable<Domain.Entities.RawPunch> RawQuery(ExportFilter f)
    {
        var q = _db.RawPunches.AsNoTracking().Where(p => p.PunchTime >= f.From.Date && p.PunchTime < f.To.Date.AddDays(1));
        if (f.EmployeeId.HasValue) q = q.Where(p => p.EmployeeId == f.EmployeeId);
        return q;
    }

    private async Task<List<RawRow>> GetRawPunchesAsync(ExportFilter f, CancellationToken ct)
    {
        var q = from p in RawQuery(f)
                join e in _db.Employees.AsNoTracking() on p.EmployeeId equals e.Id into g
                from e in g.DefaultIfEmpty()
                orderby p.PunchTime
                select new RawRow
                {
                    Code = e != null ? e.EmployeeCode : p.DeviceEmployeeCode,
                    Name = e != null ? e.FullName : (p.RawEmployeeName ?? "غير معروف"),
                    DeviceId = p.DeviceId,
                    AttType = p.AttType,
                    PunchTime = p.PunchTime,
                    RawText = p.RawPunchTimeText,
                    RowNumber = p.RowNumber,
                    EmployeeId = p.EmployeeId
                };
        return await q.ToListAsync(ct);
    }

    private async Task<List<EventRow>> GetEventsAsync(ExportFilter f, CancellationToken ct)
    {
        var q = _db.AttendanceEvents.AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.RawPunch)
            .Where(a => a.WorkDate >= f.From.Date && a.WorkDate <= f.To.Date);
        if (f.EmployeeId.HasValue) q = q.Where(a => a.EmployeeId == f.EmployeeId);
        if (f.ShiftId.HasValue) q = q.Where(a => a.ShiftId == f.ShiftId);

        var list = await q.OrderBy(a => a.EventTime).ToListAsync(ct);
        return list.Select(a => new EventRow
        {
            EmployeeId = a.EmployeeId,
            Code = a.Employee?.EmployeeCode ?? a.RawPunch?.DeviceEmployeeCode ?? string.Empty,
            Name = a.Employee?.FullName ?? a.RawPunch?.RawEmployeeName ?? string.Empty,
            DeviceId = a.RawPunch?.DeviceId,
            RawPunchId = a.RawPunchId,
            EventType = a.EventType,
            EventTime = a.EventTime,
            ClassificationStatus = a.ClassificationStatus,
            ClassificationSource = a.ClassificationSource,
            Note = a.Notes
        }).ToList();
    }

    private async Task<List<DailyRow>> GetDailyAsync(ExportFilter f, CancellationToken ct)
    {
        var q = _db.DailyAttendances.AsNoTracking()
            .Include(d => d.Employee)
            .Include(d => d.Shift)
            .Where(d => d.WorkDate >= f.From.Date && d.WorkDate <= f.To.Date);
        if (f.EmployeeId.HasValue) q = q.Where(d => d.EmployeeId == f.EmployeeId);
        if (f.ShiftId.HasValue) q = q.Where(d => d.ShiftId == f.ShiftId);

        var list = await q.OrderBy(d => d.WorkDate).ThenBy(d => d.EmployeeId).ToListAsync(ct);
        return list.Select(d => new DailyRow
        {
            EmployeeId = d.EmployeeId,
            Code = d.Employee?.EmployeeCode ?? string.Empty,
            Name = d.Employee?.FullName ?? string.Empty,
            WorkDate = d.WorkDate,
            ShiftName = d.Shift?.ShiftName ?? string.Empty,
            CheckIn = d.CheckInTime,
            CheckOut = d.CheckOutTime,
            ShiftEnd = d.ShiftEnd,
            LateMinutes = d.LateMinutes,
            EarlyLeaveMinutes = d.EarlyLeaveMinutes,
            CalculatedOvertimeMinutes = d.CalculatedOvertimeMinutes,
            ApprovedOvertimeMinutes = d.ApprovedOvertimeMinutes,
            Status = d.Status,
            PunchReviewStatus = d.PunchReviewStatus,
            Notes = d.Notes ?? d.AdminNotes
        }).ToList();
    }

    private async Task AddPayrollSheetAsync(XLWorkbook wb, ExportFilter f, CancellationToken ct, int? runId = null)
    {
        Domain.Entities.PayrollRun? run;
        if (runId.HasValue)
        {
            run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        }
        else
        {
            var year = f.Year ?? f.To.Year;
            var month = f.Month ?? f.To.Month;
            run = await _db.PayrollRuns.AsNoTracking()
                .Where(r => r.Year == year && r.Month == month && r.Status != PayrollStatus.Cancelled)
                .OrderByDescending(r => r.Version)
                .FirstOrDefaultAsync(ct);
        }

        if (run is null)
        {
            AddSheet(wb, "Payroll", "كشف الرواتب", f.PeriodText,
                new[] { "لا يوجد كشف رواتب لهذه الفترة" },
                Enumerable.Empty<object?[]>(), summary: null);
            return;
        }

        var items = await _db.PayrollItems.AsNoTracking()
            .Where(i => i.PayrollRunId == run.Id)
            .OrderBy(i => i.EmployeeCode)
            .ToListAsync(ct);

        AddSheet(wb, "Payroll", $"كشف رواتب {run.Year}-{run.Month:00} (إصدار {run.Version})", f.PeriodText,
            new[] { "م", "كود الموظف", "الاسم", "الراتب الأساسي", "دقائق التأخير", "خصم التأخير",
                    "إضافي معتمد (ساعة)", "معامل الإضافي", "قيمة الإضافي", "السلف", "الخصومات والجزاءات",
                    "خصومات أخرى", "صافي الراتب", "الحالة" },
            items.Select((i, idx) => new object?[]
            {
                idx + 1, i.EmployeeCode, i.EmployeeName, i.BasicSalary, i.LateMinutes, i.LateDeduction,
                i.ApprovedOvertimeHours, i.OvertimeMultiplier, i.OvertimeValue, i.LoansTotal, i.DeductionsTotal,
                i.OtherDeductions, i.NetSalary, PayrollStatusText(i.Status)
            }),
            summary: $"عدد الموظفين: {items.Count} | إجمالي صافي الرواتب: {items.Sum(i => i.NetSalary):#,##0.00} | حالة الكشف: {PayrollStatusText(run.Status)}",
            moneyColumns: new[] { 4, 6, 9, 10, 11, 12, 13 }, decimalColumns: new[] { 7, 8 });
    }

    private async Task<List<ExceptionRow>> GetExceptionsAsync(ExportFilter f, CancellationToken ct)
    {
        var rows = new List<ExceptionRow>();

        var daily = await _db.DailyAttendances.AsNoTracking()
            .Include(d => d.Employee)
            .Where(d => d.WorkDate >= f.From.Date && d.WorkDate <= f.To.Date
                        && (d.Status == AttendanceStatus.MissingCheckIn || d.Status == AttendanceStatus.MissingCheckOut
                            || d.Status == AttendanceStatus.NeedsReview))
            .ToListAsync(ct);
        foreach (var d in daily)
            rows.Add(new ExceptionRow
            {
                Kind = d.Status == AttendanceStatus.MissingCheckIn ? "بصمة دخول مفقودة"
                    : d.Status == AttendanceStatus.MissingCheckOut ? "بصمة خروج مفقودة" : "يحتاج مراجعة",
                Code = d.Employee?.EmployeeCode ?? string.Empty,
                Name = d.Employee?.FullName ?? string.Empty,
                When = d.WorkDate,
                Description = d.Notes ?? "بصمات ناقصة أو غير مؤكدة"
            });

        var reviewEvents = await _db.AttendanceEvents.AsNoTracking()
            .Include(a => a.Employee)
            .Include(a => a.RawPunch)
            .Where(a => a.WorkDate >= f.From.Date && a.WorkDate <= f.To.Date
                        && (a.ClassificationStatus == ClassificationStatus.NeedsReview || a.EventType == AttendanceEventType.Unknown))
            .ToListAsync(ct);
        foreach (var e in reviewEvents)
            rows.Add(new ExceptionRow
            {
                Kind = e.EventType == AttendanceEventType.Unknown ? "بصمة غير مصنفة" : "تصنيف بحاجة لمراجعة",
                Code = e.Employee?.EmployeeCode ?? e.RawPunch?.DeviceEmployeeCode ?? string.Empty,
                Name = e.Employee?.FullName ?? e.RawPunch?.RawEmployeeName ?? string.Empty,
                When = e.EventTime,
                Description = e.Notes ?? "تصنيف آلية بثقة منخفضة"
            });

        var unmatched = await _db.RawPunches.AsNoTracking()
            .Where(p => p.EmployeeId == null && p.PunchTime >= f.From.Date && p.PunchTime < f.To.Date.AddDays(1))
            .ToListAsync(ct);
        foreach (var p in unmatched)
            rows.Add(new ExceptionRow
            {
                Kind = "موظف غير مسجل",
                Code = p.DeviceEmployeeCode,
                Name = p.RawEmployeeName ?? "-",
                When = p.PunchTime,
                Description = "لا يوجد موظف مطابق لهذا الكود"
            });

        return rows.OrderBy(r => r.When).ToList();
    }

    // ============================ أدوات التنسيق ============================
    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void AddSheet(
        XLWorkbook wb, string sheetName, string title, string period,
        string[] headers, IEnumerable<object?[]> rows, string? summary,
        int[]? dateTimeColumns = null, int[]? dateFormatColumns = null, int[]? timeFormatColumns = null,
        int[]? moneyColumns = null, int[]? decimalColumns = null)
    {
        var ws = wb.Worksheets.Add(sheetName);
        ws.RightToLeft = true;
        var cols = Math.Max(1, headers.Length);

        // العنوان
        ws.Cell(1, 1).Value = title;
        ws.Range(1, 1, 1, cols).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(2, 1).Value = period;
        ws.Range(2, 1, 2, cols).Merge();
        ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(2, 1).Style.Font.Italic = true;

        const int headerRow = 3;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length && c < headers.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                setValue(cell, row[c]);
            }
            r++;
        }

        var lastRow = Math.Max(r - 1, headerRow);
        if (rows.Any())
            ws.Range(headerRow, 1, lastRow, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(headerRow);

        // تنسيقات الأعمدة
        void ApplyFormat(int[]? columns, string fmt)
        {
            if (columns is null) return;
            foreach (var col in columns)
                if (col >= 1 && col <= headers.Length)
                    ws.Column(col).Style.NumberFormat.Format = fmt;
        }
        ApplyFormat(dateTimeColumns, DateTimeFormat);
        ApplyFormat(dateFormatColumns, DateFormat);
        ApplyFormat(timeFormatColumns, "hh:mm:ss");
        ApplyFormat(moneyColumns, MoneyFormat);
        ApplyFormat(decimalColumns, "0.00");

        if (!string.IsNullOrWhiteSpace(summary))
        {
            var sr = lastRow + 2;
            ws.Cell(sr, 1).Value = summary;
            ws.Range(sr, 1, sr, headers.Length).Merge();
            ws.Cell(sr, 1).Style.Font.Bold = true;
            ws.Cell(sr, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
        }

        ws.Columns().AdjustToContents();
        foreach (var column in ws.Columns())
        {
            if (column.Width > 40) column.Width = 40;
            if (column.Width < 10) column.Width = 10;
        }
    }

    private static void setValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: cell.Value = string.Empty; break;
            case string s: cell.Value = s; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case decimal m: cell.Value = m; break;
            case double d: cell.Value = d; break;
            case DateTime dt: cell.Value = dt; break;
            case TimeSpan ts: cell.Value = ts.ToString(@"hh\:mm\:ss"); break;
            case bool b: cell.Value = b ? "نعم" : "لا"; break;
            default: cell.Value = value.ToString(); break;
        }
    }

    private static string StatusText(ClassificationStatus s) => s switch
    {
        ClassificationStatus.Classified => "مصنف",
        ClassificationStatus.NeedsReview => "يحتاج مراجعة",
        _ => "غير مصنف"
    };

    private static string SourceText(ClassificationSource s) => s switch
    {
        ClassificationSource.ExplicitDeviceType => "نوع الحركة من الجهاز",
        ClassificationSource.Rule => "قواعد آلية",
        ClassificationSource.Manual => "مراجعة يدوية",
        _ => "-"
    };

    private static string ReviewText(ClassificationStatus s) => StatusText(s);

    private static string AttendanceText(AttendanceStatus s) => s switch
    {
        AttendanceStatus.Present => "حاضر",
        AttendanceStatus.Absent => "غائب",
        AttendanceStatus.Late => "متأخر",
        AttendanceStatus.MissingCheckIn => "بصمة دخول مفقودة",
        AttendanceStatus.MissingCheckOut => "بصمة خروج مفقودة",
        AttendanceStatus.Leave => "إجازة",
        AttendanceStatus.RestDay => "يوم راحة",
        AttendanceStatus.Holiday => "عطلة",
        _ => "يحتاج مراجعة"
    };

    private static string PayrollStatusText(PayrollStatus s) => s switch
    {
        PayrollStatus.Draft => "مسودة",
        PayrollStatus.Computed => "محسوب",
        PayrollStatus.Approved => "معتمد",
        _ => "ملغى"
    };

    // DTOs داخلية
    private sealed class RawRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? DeviceId { get; set; }
        public string? AttType { get; set; }
        public DateTime PunchTime { get; set; }
        public string? RawText { get; set; }
        public int? RowNumber { get; set; }
        public int? EmployeeId { get; set; }
    }

    private sealed class EventRow
    {
        public int EmployeeId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? DeviceId { get; set; }
        public int RawPunchId { get; set; }
        public AttendanceEventType EventType { get; set; }
        public DateTime EventTime { get; set; }
        public ClassificationStatus ClassificationStatus { get; set; }
        public ClassificationSource ClassificationSource { get; set; }
        public string? Note { get; set; }
    }

    private sealed class DailyRow
    {
        public int EmployeeId { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime WorkDate { get; set; }
        public string ShiftName { get; set; } = "";
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public DateTime ShiftEnd { get; set; }
        public int LateMinutes { get; set; }
        public int EarlyLeaveMinutes { get; set; }
        public int CalculatedOvertimeMinutes { get; set; }
        public int ApprovedOvertimeMinutes { get; set; }
        public AttendanceStatus Status { get; set; }
        public ClassificationStatus PunchReviewStatus { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class ExceptionRow
    {
        public string Kind { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime When { get; set; }
        public string Description { get; set; } = "";
    }
}
