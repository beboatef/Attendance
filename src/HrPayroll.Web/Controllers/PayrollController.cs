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

    public PayrollController(AppDbContext db, PayrollService payroll, ReportService reports,
        ExcelExportService excel, PdfExportService pdf)
    {
        _db = db;
        _payroll = payroll;
        _reports = reports;
        _excel = excel;
        _pdf = pdf;
    }

    public async Task<IActionResult> Index(int? year, int? month, string? department, int? employeeId,
        PayrollStatus? status, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        var m = month ?? DateTime.Today.Month;

        var run = await _db.PayrollRuns.AsNoTracking()
            .Where(r => r.Year == y && r.Month == m && r.Status != PayrollStatus.Cancelled)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        List<PayrollReportRow> rows = run is null
            ? new List<PayrollReportRow>()
            : await _reports.GetPayrollRowsAsync(run.Id, department, employeeId, status, ct);

        return View(new PayrollIndexViewModel
        {
            Year = y,
            Month = m,
            CurrentRun = run,
            Readiness = await _payroll.GetReadinessAsync(y, m, ct),
            Rows = rows,
            Department = department,
            EmployeeId = employeeId,
            Status = status,
            Departments = await _db.Employees.AsNoTracking()
                .Where(e => e.Department != null).Select(e => e.Department!).Distinct().OrderBy(d => d).ToListAsync(ct),
            Employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct),
            Runs = await _db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                .ThenByDescending(r => r.Version).Take(24).ToListAsync(ct)
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
            TempData["Error"] = ex.Message;
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
        catch (Exception ex) { TempData["Error"] = ex.Message; }
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
        catch (Exception ex) { TempData["Error"] = ex.Message; }
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
        catch (Exception ex) { TempData["Error"] = ex.Message; }
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
        catch (Exception ex) { TempData["Error"] = ex.Message; }
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
        catch (Exception ex) { TempData["Error"] = ex.Message; }
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
