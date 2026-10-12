using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "ManagePayroll")]
public class OvertimeController : Controller
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public OvertimeController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(int? year, int? month, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        var m = month ?? DateTime.Today.Month;
        var first = new DateTime(y, m, 1);
        var last = first.AddMonths(1).AddDays(-1);
        ViewBag.Year = y; ViewBag.Month = m; ViewBag.First = first; ViewBag.Last = last;

        var list = await _db.OvertimeApprovals.Include(o => o.Employee)
            .Where(o => o.PeriodStart >= first && o.PeriodEnd <= last)
            .OrderByDescending(o => o.PeriodStart).ToListAsync(ct);
        return View(list);
    }

    /// <summary>توليد اعتمادات إضافي من الحضور المحسوب لفترة محددة.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(DateTime from, DateTime to, CancellationToken ct)
    {
        var computed = await _db.DailyAttendances.AsNoTracking()
            .Where(d => d.WorkDate >= from.Date && d.WorkDate <= to.Date && d.CalculatedOvertimeMinutes > 0)
            .GroupBy(d => d.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Minutes = g.Sum(x => x.CalculatedOvertimeMinutes) })
            .ToListAsync(ct);

        var created = 0;
        foreach (var c in computed)
        {
            var exists = await _db.OvertimeApprovals.AnyAsync(o => o.EmployeeId == c.EmployeeId
                && o.PeriodStart == from.Date && o.PeriodEnd == to.Date, ct);
            if (exists) continue;

            var hours = Math.Round(c.Minutes / 60m, 2, MidpointRounding.AwayFromZero);
            _db.OvertimeApprovals.Add(new OvertimeApproval
            {
                EmployeeId = c.EmployeeId,
                PeriodStart = from.Date,
                PeriodEnd = to.Date,
                CalculatedHours = hours,
                ApprovedHours = 0m, // لا يُعتمد تلقائيًا
                OvertimeMultiplier = 1m,
                Status = ApprovalStatus.Pending,
                CreatedBy = User.Identity?.Name
            });
            created++;
        }
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"تم إنشاء {created} سجل إضافي بحاجة لاعتماد";
        return RedirectToAction(nameof(Index), new { year = from.Year, month = from.Month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, decimal approvedHours, CancellationToken ct)
    {
        var ot = await _db.OvertimeApprovals.FindAsync(new object?[] { id }, ct);
        if (ot is null) return NotFound();
        if (ot.PayrollItemId.HasValue)
        {
            TempData["Error"] = "لا يمكن تعديل إضافي تم إدراجه في كشف رواتب";
            return RedirectToAction(nameof(Index));
        }
        if (approvedHours < 0) approvedHours = 0;
        if (approvedHours > ot.CalculatedHours)
            TempData["Warning"] = "الساعات المعتمدة تتجاوز المحسوبة، سيتم تسجيلها كما أدخلتها مع بقاء المحسوب للمراجعة.";

        var old = $"{ot.CalculatedHours}/{ot.ApprovedHours}";
        ot.ApprovedHours = approvedHours;
        ot.Status = ApprovalStatus.Approved;
        ot.ApprovedBy = User.Identity?.Name;
        ot.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _audit.Log("اعتماد إضافي", nameof(OvertimeApproval), ot.Id.ToString(), old, $"{ot.CalculatedHours}/{ot.ApprovedHours}", User.Identity?.Name);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم اعتماد الإضافي";
        return RedirectToAction(nameof(Index), new { year = ot.PeriodStart.Year, month = ot.PeriodStart.Month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, CancellationToken ct)
    {
        var ot = await _db.OvertimeApprovals.FindAsync(new object?[] { id }, ct);
        if (ot is null) return NotFound();
        if (ot.PayrollItemId.HasValue)
        {
            TempData["Error"] = "لا يمكن تعديل إضافي مدرج في كشف";
            return RedirectToAction(nameof(Index));
        }
        ot.ApprovedHours = 0;
        ot.Status = ApprovalStatus.Rejected;
        ot.ApprovedBy = User.Identity?.Name;
        ot.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "تم رفض الإضافي";
        return RedirectToAction(nameof(Index), new { year = ot.PeriodStart.Year, month = ot.PeriodStart.Month });
    }
}
