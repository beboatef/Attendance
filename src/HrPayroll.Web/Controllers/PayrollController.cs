using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "ManagePayroll")]
public class PayrollController : Controller
{
    private readonly AppDbContext _db;
    private readonly PayrollService _payroll;
    private readonly ExcelExportService _excel;

    public PayrollController(AppDbContext db, PayrollService payroll, ExcelExportService excel)
    {
        _db = db; _payroll = payroll; _excel = excel;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _db.PayrollRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenByDescending(r => r.Version).ToListAsync(ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int year, int month, CancellationToken ct)
    {
        if (month is < 1 or > 12)
        {
            TempData["Error"] = "شهر غير صالح";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var result = await _payroll.GenerateAsync(year, month, User.Identity?.Name, userId, ct);
            TempData["Success"] = $"تم توليد كشف {year}-{month:00} (إصدار {result.Version}) لعدد {result.ItemCount} موظف. صافي الإجمالي: {result.TotalNet:#,##0.00}";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var run = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null) return NotFound();
        var items = await _db.PayrollItems.AsNoTracking().Where(i => i.PayrollRunId == id).OrderBy(i => i.EmployeeCode).ToListAsync(ct);
        ViewBag.Items = items;
        return View(run);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        await _payroll.ApproveAsync(id, User.Identity?.Name, userId, ct);
        TempData["Success"] = "تم اعتماد كشف الرواتب وقفله";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        await _payroll.CancelAsync(id, User.Identity?.Name, userId, ct);
        TempData["Success"] = "تم إلغاء الاعتماد. يمكن الآن إعادة الاحتساب";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Export(int id, CancellationToken ct)
    {
        var bytes = await _excel.ExportPayrollAsync(id, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Payroll_{id}_{DateTime.Now:yyyyMMddHHmm}.xlsx");
    }
}
