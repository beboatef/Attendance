using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

/// <summary>دورة كشف الرواتب: توليد ← مراجعة ← اعتماد ← قفل ← إعادة فتح ← تصدير.</summary>
[Authorize(Policy = "ManagePayroll")]
public class PayrollController : Controller
{
    private readonly AppDbContext _db;
    private readonly PayrollService _payroll;
    private readonly ReportService _reports;
    private readonly ExcelExportService _excel;
    private readonly PdfExportService _pdf;
    private readonly PayrollSheetService _sheet;
    private readonly ILogger<PayrollController> _logger;

    public PayrollController(AppDbContext db, PayrollService payroll, ReportService reports,
        ExcelExportService excel, PdfExportService pdf, PayrollSheetService sheet,
        ILogger<PayrollController> logger)
    {
        _db = db;
        _payroll = payroll;
        _reports = reports;
        _excel = excel;
        _pdf = pdf;
        _sheet = sheet;
        _logger = logger;
    }

    private static readonly string[] MonthNames =
    {
        "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
        "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
    };

    public static string MonthName(int month) => month is >= 1 and <= 12 ? MonthNames[month - 1] : month.ToString();

    /// <summary>شاشة "كشف المرتبات الشهري": جدول واحد قابل للتحرير لكل موظفي الشهر.</summary>
    public async Task<IActionResult> Index(int? year, int? month, string? department, int? employeeId, string? search,
        PayrollStatus? status, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        var m = month ?? DateTime.Today.Month;
        if (m is < 1 or > 12) m = DateTime.Today.Month;

        var run = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Year == y && r.Month == m && r.Status != PayrollStatus.Cancelled)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        List<PayrollReportRow> rows = run is null
            ? new List<PayrollReportRow>()
            : await _reports.GetPayrollRowsAsync(run.Id, department, employeeId, status, ct);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            rows = rows.Where(r => r.EmployeeName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || r.EmployeeCode.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return View(new PayrollIndexViewModel
        {
            Year = y,
            Month = m,
            PeriodText = $"{MonthName(m)} {y}",
            CurrentRun = run,
            Readiness = await _payroll.GetReadinessAsync(y, m, ct),
            Rows = rows,
            Department = department,
            EmployeeId = employeeId,
            Search = search,
            Status = status,
            Departments = await _db.Employees.AsNoTracking()
                .Where(e => e.Department != null).Select(e => e.Department!).Distinct().OrderBy(d => d).ToListAsync(ct),
            Employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct),
            Runs = await _db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                .ThenByDescending(r => r.Version).Take(24).ToListAsync(ct)
        });
    }

    /// <summary>حفظ تعديلات خلايا الكشف يدويًا (تُعاد كل القيم للحساب على السيرفر).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.EditPayrollSheet)]
    public async Task<IActionResult> SaveRows([FromBody] PayrollSheetSaveRequest request, CancellationToken ct)
    {
        if (request.Rows.Count == 0)
            return Json(new { ok = true, savedCount = 0, message = "لا توجد تغييرات للحفظ." });

        try
        {
            var result = await _sheet.SaveAsync(request.RunId,
                request.Rows.Select(r => r.ToServiceModel()).ToList(),
                request.AllowOverdraft, this.CurrentUserName(), this.CurrentUserId(), ct);

            return Json(new
            {
                ok = !result.AnyError,
                savedCount = result.SavedCount,
                totals = new
                {
                    gross = result.TotalGross,
                    deductions = result.TotalDeductions,
                    net = result.TotalNet,
                    overtime = result.TotalOvertime
                },
                rows = result.Rows.Select(r => new
                {
                    payrollItemId = r.PayrollItemId,
                    employeeCode = r.EmployeeCode,
                    employeeName = r.EmployeeName,
                    saved = r.Saved,
                    requiresConfirmation = r.RequiresConfirmation,
                    error = r.Error,
                    warnings = r.Warnings,
                    needsReview = r.NeedsReview,
                    reviewStatusText = r.ReviewStatusText,
                    values = new
                    {
                        absenceDays = r.AbsenceDays,
                        originalAbsenceDays = r.OriginalAbsenceDays,
                        annualLeaveDays = r.AnnualLeaveDays,
                        unpaidLeaveDays = r.UnpaidLeaveDays,
                        annualLeaveBalance = r.AnnualLeaveBalance,
                        approvedOvertimeHours = r.ApprovedOvertimeHours,
                        paidOvertimeHours = r.PaidOvertimeHours,
                        overtimeAmount = r.OvertimeAmount,
                        bonuses = r.Bonuses,
                        allowances = r.Allowances,
                        additionalDaysCount = r.AdditionalDaysCount,
                        additionalDaysAmount = r.AdditionalDaysAmount,
                        advances = r.Advances,
                        otherDeductions = r.OtherDeductions,
                        lateDeduction = r.LateDeduction,
                        absenceDeduction = r.AbsenceDeduction,
                        unpaidLeaveDeduction = r.UnpaidLeaveDeduction,
                        grossEarnings = r.GrossEarnings,
                        totalDeductions = r.TotalDeductions,
                        netSalary = r.NetSalary
                    }
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.UserMessage(_logger, "فشل حفظ تعديلات كشف المرتبات") });
        }
    }

    /// <summary>تفاصيل حساب سطر واحد (يُعرض في نافذة منبثقة داخل الكشف).</summary>
    [HttpGet]
    public async Task<IActionResult> RowBreakdown(int id, CancellationToken ct)
    {
        var item = await _db.PayrollItems.AsNoTracking()
            .Include(i => i.Employee)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();

        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == item.PayrollRunId, ct);

        return Json(new
        {
            payrollItemId = item.Id,
            employeeCode = item.EmployeeCode,
            employeeName = item.EmployeeName,
            department = item.DepartmentName,
            period = run is null ? null : $"{MonthName(run.Month)} {run.Year}",
            status = run is null ? null : ExcelExportService.PayrollStatusText(run.Status),
            basicSalary = item.BasicSalary,
            dailyRate = item.DailyRate,
            hourlyRate = item.HourlyRate,
            attendanceDays = item.AttendanceDays,
            paidRestDays = item.PaidRestDays,
            paidLeaveDays = item.PaidLeaveDays,
            unpaidLeaveDays = item.UnpaidLeaveDays,
            originalAbsenceDays = item.OriginalAbsenceDays,
            absenceDays = item.AbsenceDays,
            annualLeaveDays = item.AnnualLeaveDays,
            annualLeaveBalance = item.Employee?.AnnualLeaveBalance,
            annualLeaveEntitlement = item.Employee?.AnnualLeaveEntitlement,
            annualLeaveCarriedOver = item.Employee?.AnnualLeaveCarriedOver,
            lateMinutes = item.LateMinutes,
            rawOvertimeHours = item.RawOvertimeHours,
            approvedOvertimeHours = item.ApprovedOvertimeHours,
            paidOvertimeHours = item.PaidOvertimeHours,
            overtimeMultiplier = item.OvertimeMultiplier,
            overtimeAmount = item.OvertimeAmount,
            bonuses = item.Bonuses,
            allowances = item.Allowances,
            additionalDaysCount = item.AdditionalDaysCount,
            additionalDaysRate = item.AdditionalDaysRate,
            additionalDaysAmount = item.AdditionalDaysAmount,
            lateDeduction = item.LateDeduction,
            absenceDeduction = item.AbsenceDeduction,
            unpaidLeaveDeduction = item.UnpaidLeaveDeduction,
            advances = item.LoansTotal,
            deductionsTotal = item.DeductionsTotal,
            otherDeductions = item.OtherDeductions,
            grossEarnings = item.GrossEarnings,
            totalDeductions = item.TotalDeductions,
            netSalary = item.NetSalary,
            isManuallyAdjusted = item.IsManuallyAdjusted,
            lastEditedBy = item.LastEditedBy,
            lastEditedAt = item.LastEditedAt,
            notes = item.Notes,
            calculationDetails = item.CalculationDetails
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int year, int month, CancellationToken ct)
    {
        try
        {
            var result = await _payroll.GenerateAsync(year, month, this.CurrentUserName(), this.CurrentUserId(), ct);
            TempData["Success"] = $"تم احتساب كشف {year}-{month:00} (إصدار {result.Version}) لعدد {result.ItemCount} موظف. " +
                                  $"إجمالي الاستحقاقات {result.TotalGross:N2} — الخصومات {result.TotalDeductions:N2} — الصافي {result.TotalNet:N2}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.UserMessage(_logger, "فشل احتساب كشف المرتبات");
        }
        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, int year, int month, CancellationToken ct)
    {
        try
        {
            await _payroll.MarkPendingReviewAsync(id, this.CurrentUserName(), this.CurrentUserId(), ct);
            TempData["Success"] = "تم إرسال الكشف للمراجعة.";
        }
        catch (Exception ex) { TempData["Error"] = ex.UserMessage(_logger, "فشل تنفيذ عملية على كشف المرتبات"); }
        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, int year, int month, CancellationToken ct)
    {
        try
        {
            await _payroll.ApproveAsync(id, this.CurrentUserName(), this.CurrentUserId(), ct);
            TempData["Success"] = "تم اعتماد الكشف.";
        }
        catch (Exception ex) { TempData["Error"] = ex.UserMessage(_logger, "فشل تنفيذ عملية على كشف المرتبات"); }
        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(int id, int year, int month, CancellationToken ct)
    {
        try
        {
            await _payroll.LockAsync(id, this.CurrentUserName(), this.CurrentUserId(), ct);
            TempData["Success"] = "تم قفل الكشف. لن يتأثر بأي استيراد بصمات لاحق بدون إعادة فتح بسبب مُسجَّل.";
        }
        catch (Exception ex) { TempData["Error"] = ex.UserMessage(_logger, "فشل تنفيذ عملية على كشف المرتبات"); }
        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, int year, int month, string reason, CancellationToken ct)
    {
        try
        {
            await _payroll.ReopenAsync(id, reason, this.CurrentUserName(), this.CurrentUserId(), ct);
            TempData["Success"] = "تم إعادة فتح الكشف بسبب مُسجَّل. يمكن الآن التصحيح وإعادة الاحتساب.";
        }
        catch (Exception ex) { TempData["Error"] = ex.UserMessage(_logger, "فشل تنفيذ عملية على كشف المرتبات"); }
        return RedirectToAction(nameof(Index), new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, int year, int month, CancellationToken ct)
    {
        try
        {
            await _payroll.CancelAsync(id, this.CurrentUserName(), this.CurrentUserId(), ct);
            TempData["Success"] = "تم إلغاء الكشف.";
        }
        catch (Exception ex) { TempData["Error"] = ex.UserMessage(_logger, "فشل تنفيذ عملية على كشف المرتبات"); }
        return RedirectToAction(nameof(Index), new { year, month });
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();

        return View(new PayrollDetailsViewModel
        {
            Run = run,
            Rows = await _reports.GetPayrollRowsAsync(id, ct: ct),
            Departments = await _db.Employees.AsNoTracking()
                .Where(e => e.Department != null).Select(e => e.Department!).Distinct().OrderBy(d => d).ToListAsync(ct)
        });
    }

    /// <summary>كشف راتب موظف واحد مع كل تفاصيل الحساب.</summary>
    public async Task<IActionResult> Payslip(int id, CancellationToken ct)
    {
        var slip = await _reports.GetPayslipAsync(id, ct);
        if (slip is null) return NotFound();
        return View(slip);
    }

    public async Task<IActionResult> ExportExcel(int id, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();
        var bytes = await _excel.ExportPayrollAsync(id, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Payroll_{run.Year}_{run.Month:00}.xlsx");
    }

    public async Task<IActionResult> ExportPdf(int id, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();
        var bytes = await _pdf.BuildPayrollReportAsync(id, ct);
        return File(bytes, "application/pdf", $"Payroll_{run.Year}_{run.Month:00}.pdf");
    }

    public async Task<IActionResult> PayslipExcel(int id, CancellationToken ct)
    {
        var item = await _db.PayrollItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        var bytes = await _excel.ExportPayslipAsync(id, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Payslip_{item.EmployeeCode}.xlsx");
    }

    public async Task<IActionResult> PayslipPdf(int id, CancellationToken ct)
    {
        var item = await _db.PayrollItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        var bytes = await _pdf.BuildPayslipAsync(id, ct);
        return File(bytes, "application/pdf", $"Payslip_{item.EmployeeCode}.pdf");
    }
}
