using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Import;
using HrPayroll.Infrastructure.Persistence;
using HrPayroll.Infrastructure.Services;
using HrPayroll.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Web.Controllers;

[Authorize(Policy = "ManageEmployees")]
public class EmployeesController : Controller
{
    private const long MaxFileSize = 25 * 1024 * 1024;
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "hrpayroll-employees");

    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly EmployeeImportService _import;
    private readonly LeaveService _leaves;

    public EmployeesController(AppDbContext db, AuditService audit, EmployeeImportService import, LeaveService leaves)
    {
        _db = db;
        _audit = audit;
        _import = import;
        _leaves = leaves;
    }

    public async Task<IActionResult> Index(string? search, string? department, int? departmentId, int? shiftId,
        EmploymentStatus? employmentStatus, bool? isActive, int page = 1, CancellationToken ct = default)
    {
        const int pageSize = 20;
        var query = _db.Employees.Include(e => e.Shift).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(e => e.FullName.Contains(s) || e.EmployeeCode.Contains(s)
                                     || (e.DeviceEmployeeCode != null && e.DeviceEmployeeCode.Contains(s))
                                     || (e.Phone != null && e.Phone.Contains(s)));
        }
        if (!string.IsNullOrWhiteSpace(department)) query = query.Where(e => e.Department == department);
        if (departmentId.HasValue) query = query.Where(e => e.DepartmentId == departmentId.Value);
        if (shiftId.HasValue) query = query.Where(e => e.ShiftId == shiftId.Value);
        if (employmentStatus.HasValue) query = query.Where(e => e.EmploymentStatus == employmentStatus.Value);
        if (isActive.HasValue) query = query.Where(e => e.IsActive == isActive.Value);

        var total = await query.CountAsync(ct);
        var list = await query.OrderBy(e => e.EmployeeCode)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return View(new EmployeeIndexViewModel
        {
            Employees = list,
            Search = search,
            DepartmentFilter = department,
            DepartmentId = departmentId,
            ShiftId = shiftId,
            EmploymentStatus = employmentStatus,
            IsActive = isActive,
            TotalCount = total,
            Page = Math.Max(1, page),
            PageSize = pageSize,
            Departments = await _db.Employees.Where(e => e.Department != null)
                .Select(e => e.Department!).Distinct().OrderBy(d => d).ToListAsync(ct),
            DepartmentRefs = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct),
            Shifts = await _db.Shifts.AsNoTracking().OrderBy(s => s.ShiftNumber).ToListAsync(ct)
        });
    }

    // ===================== إضافة / تعديل =====================

    private async Task FillLookupsAsync(EmployeeFormViewModel vm, CancellationToken ct)
    {
        vm.Shifts = await _db.Shifts.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.ShiftNumber).ToListAsync(ct);
        vm.Departments = await _db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(ct);
        vm.JobTitles = await _db.JobTitles.AsNoTracking().Where(j => j.IsActive).OrderBy(j => j.Name).ToListAsync(ct);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new EmployeeFormViewModel();
        await FillLookupsAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EmployeeFormViewModel vm, CancellationToken ct)
    {
        if (await _db.Employees.AnyAsync(e => e.EmployeeCode == vm.EmployeeCode.Trim(), ct))
            ModelState.AddModelError(nameof(vm.EmployeeCode), "كود الموظف مستخدم مسبقًا");

        var deviceCode = string.IsNullOrWhiteSpace(vm.DeviceEmployeeCode) ? vm.EmployeeCode.Trim() : vm.DeviceEmployeeCode!.Trim();
        if (await _db.Employees.AnyAsync(e => e.DeviceEmployeeCode == deviceCode, ct))
            ModelState.AddModelError(nameof(vm.DeviceEmployeeCode), "كود البصمة مستخدم مسبقًا لموظف آخر");

        if (!ModelState.IsValid)
        {
            await FillLookupsAsync(vm, ct);
            return View("Form", vm);
        }

        var dept = vm.DepartmentId.HasValue
            ? await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == vm.DepartmentId.Value, ct)
            : await _db.Departments.FirstOrDefaultAsync(d => d.Name == vm.Department, ct);
        var job = vm.JobTitleId.HasValue
            ? await _db.JobTitles.AsNoTracking().FirstOrDefaultAsync(j => j.Id == vm.JobTitleId.Value, ct)
            : await _db.JobTitles.FirstOrDefaultAsync(j => j.Name == vm.JobTitle, ct);

        var emp = new Employee
        {
            EmployeeCode = vm.EmployeeCode.Trim(),
            DeviceEmployeeCode = deviceCode,
            FullName = vm.FullName.Trim(),
            JobTitle = job?.Name ?? vm.JobTitle,
            JobTitleId = job?.Id ?? vm.JobTitleId,
            Department = dept?.Name ?? vm.Department,
            DepartmentId = dept?.Id ?? vm.DepartmentId,
            ShiftId = vm.ShiftId,
            BasicSalary = vm.BasicSalary,
            DailyRate = vm.DailyRate,
            StandardWorkHours = vm.StandardWorkHours <= 0 ? 8m : vm.StandardWorkHours,
            AnnualLeaveEntitlement = vm.AnnualLeaveEntitlement,
            AnnualLeaveBalance = vm.AnnualLeaveBalance > 0 ? vm.AnnualLeaveBalance : vm.AnnualLeaveEntitlement,
            HireDate = vm.HireDate.Date,
            EmploymentStatus = vm.EmploymentStatus,
            IsActive = vm.EmploymentStatus == EmploymentStatus.Active,
            TerminationDate = vm.TerminationDate,
            Email = vm.Email,
            Phone = vm.Phone,
            ContactInfo = vm.ContactInfo,
            NationalId = vm.NationalId,
            Notes = vm.Notes,
            CreatedBy = this.CurrentUserName(),
            CreatedAt = DateTime.UtcNow
        };
        _db.Employees.Add(emp);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("إضافة موظف", nameof(Employee), emp.Id.ToString(), null,
            $"{emp.EmployeeCode} - {emp.FullName}", this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم إضافة الموظف بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var emp = await _db.Employees.FindAsync(new object?[] { id }, ct);
        if (emp is null) return NotFound();
        var vm = EmployeeFormViewModel.FromEntity(emp);
        await FillLookupsAsync(vm, ct);
        return View("Form", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EmployeeFormViewModel vm, CancellationToken ct)
    {
        if (id != vm.Id) return BadRequest();
        if (await _db.Employees.AnyAsync(e => e.EmployeeCode == vm.EmployeeCode.Trim() && e.Id != id, ct))
            ModelState.AddModelError(nameof(vm.EmployeeCode), "كود الموظف مستخدم مسبقًا");

        var deviceCode = string.IsNullOrWhiteSpace(vm.DeviceEmployeeCode) ? vm.EmployeeCode.Trim() : vm.DeviceEmployeeCode!.Trim();
        if (await _db.Employees.AnyAsync(e => e.DeviceEmployeeCode == deviceCode && e.Id != id, ct))
            ModelState.AddModelError(nameof(vm.DeviceEmployeeCode), "كود البصمة مستخدم مسبقًا لموظف آخر");

        if (!ModelState.IsValid)
        {
            await FillLookupsAsync(vm, ct);
            return View("Form", vm);
        }

        var emp = await _db.Employees.FindAsync(new object?[] { id }, ct);
        if (emp is null) return NotFound();

        var dept = vm.DepartmentId.HasValue
            ? await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == vm.DepartmentId.Value, ct)
            : null;
        var job = vm.JobTitleId.HasValue
            ? await _db.JobTitles.AsNoTracking().FirstOrDefaultAsync(j => j.Id == vm.JobTitleId.Value, ct)
            : null;

        var old = $"{emp.EmployeeCode}|{emp.FullName}|{emp.BasicSalary}|{emp.Department}|{emp.JobTitle}|{emp.EmploymentStatus}";

        emp.EmployeeCode = vm.EmployeeCode.Trim();
        emp.DeviceEmployeeCode = deviceCode;
        emp.FullName = vm.FullName.Trim();
        emp.JobTitle = job?.Name ?? vm.JobTitle;
        emp.JobTitleId = job?.Id ?? vm.JobTitleId;
        emp.Department = dept?.Name ?? vm.Department;
        emp.DepartmentId = dept?.Id ?? vm.DepartmentId;
        emp.ShiftId = vm.ShiftId;
        emp.BasicSalary = vm.BasicSalary;
        emp.DailyRate = vm.DailyRate;
        emp.StandardWorkHours = vm.StandardWorkHours <= 0 ? 8m : vm.StandardWorkHours;
        emp.AnnualLeaveEntitlement = vm.AnnualLeaveEntitlement;
        emp.AnnualLeaveBalance = vm.AnnualLeaveBalance;
        emp.HireDate = vm.HireDate.Date;
        emp.EmploymentStatus = vm.EmploymentStatus;
        emp.IsActive = vm.EmploymentStatus == EmploymentStatus.Active;
        emp.TerminationDate = vm.TerminationDate;
        emp.Email = vm.Email;
        emp.Phone = vm.Phone;
        emp.ContactInfo = vm.ContactInfo;
        emp.NationalId = vm.NationalId;
        emp.Notes = vm.Notes;
        emp.UpdatedAt = DateTime.UtcNow;
        emp.UpdatedBy = this.CurrentUserName();

        await _db.SaveChangesAsync(ct);
        await _audit.LogAndSaveAsync("تعديل موظف", nameof(Employee), emp.Id.ToString(), old,
            $"{emp.EmployeeCode}|{emp.FullName}|{emp.BasicSalary}|{emp.Department}|{emp.JobTitle}|{emp.EmploymentStatus}",
            this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = "تم تحديث بيانات الموظف";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// تعطيل/تفعيل الموظف. الموظف الذي له تاريخ حضور أو رواتب لا يُحذف نهائيًا،
    /// بل يُعطَّل ويُستثنى من كشوف الرواتب الجديدة مع بقاء تاريخه قابلًا للعرض.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var emp = await _db.Employees.FindAsync(new object?[] { id }, ct);
        if (emp is null) return NotFound();

        emp.IsActive = !emp.IsActive;
        emp.EmploymentStatus = emp.IsActive ? EmploymentStatus.Active : EmploymentStatus.Terminated;
        if (!emp.IsActive && emp.TerminationDate is null) emp.TerminationDate = DateTime.Today;
        if (emp.IsActive) emp.TerminationDate = null;
        emp.UpdatedAt = DateTime.UtcNow;
        emp.UpdatedBy = this.CurrentUserName();
        await _db.SaveChangesAsync(ct);

        await _audit.LogAndSaveAsync(emp.IsActive ? "تفعيل موظف" : "تعطيل موظف", nameof(Employee), emp.Id.ToString(),
            null, $"{emp.EmploymentStatus}", this.CurrentUserName(), this.CurrentUserId(), ct);

        TempData["Success"] = emp.IsActive ? "تم تفعيل الموظف" : "تم تعطيل الموظف (سجلّه التاريخي محفوظ)";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var emp = await _db.Employees.Include(e => e.Shift).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (emp is null) return NotFound();

        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        ViewBag.ThisMonth = await _db.DailyAttendances.AsNoTracking()
            .Where(d => d.EmployeeId == id && d.WorkDate >= first && d.WorkDate <= last)
            .OrderBy(d => d.WorkDate).ToListAsync(ct);
        ViewBag.Leaves = await _db.LeaveRequests.AsNoTracking()
            .Where(l => l.EmployeeId == id).OrderByDescending(l => l.StartDate).Take(10).ToListAsync(ct);
        ViewBag.PayrollItems = await _db.PayrollItems.AsNoTracking()
            .Where(i => i.EmployeeId == id)
            .Include(i => i.PayrollRun)
            .OrderByDescending(i => i.Id).Take(12).ToListAsync(ct);
        return View(emp);
    }

    // ===================== استيراد الموظفين من Excel =====================

    [HttpGet]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        ViewBag.Batches = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.DetectedFormat == EmployeeImportService.EmployeeImportMarker)
            .OrderByDescending(b => b.ImportedAt).Take(20).ToListAsync(ct);
        return View(new EmployeeImportPreviewViewModel());
    }

    [HttpGet]
    public async Task<IActionResult> DownloadTemplate()
    {
        var excel = HttpContext.RequestServices.GetRequiredService<ExcelExportService>();
        var bytes = await excel.BuildEmployeeImportTemplateAsync(HttpContext.RequestAborted);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Employee_Import_Template.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "الرجاء اختيار ملف Excel";
            return RedirectToAction(nameof(Import));
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        EmployeeImportAnalysis analysis;
        try
        {
            analysis = await _import.AnalyzeAsync(bytes, file.FileName, ct);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "تعذّر قراءة الملف: " + ex.Message;
            return RedirectToAction(nameof(Import));
        }

        if (analysis.Mapping is null || !analysis.Mapping.IsUsable)
        {
            TempData["Error"] = "لم يتم التعرف على أعمدة كود الموظف والاسم في الملف. تأكد من وجود عمودي \"كود الموظف\" و\"الاسم\".";
            return RedirectToAction(nameof(Import));
        }

        Directory.CreateDirectory(TempDir);
        var token = Guid.NewGuid().ToString("N");
        await System.IO.File.WriteAllBytesAsync(Path.Combine(TempDir, token + ".bin"), bytes, ct);

        return View("ImportPreview", new EmployeeImportPreviewViewModel
        {
            Token = token,
            FileName = file.FileName,
            Analysis = analysis,
            DepartmentNames = await _db.Departments.AsNoTracking().Select(d => d.Name).ToListAsync(ct)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportConfirm(string token, string fileName, EmployeeImportMode mode, CancellationToken ct)
    {
        var path = Path.Combine(TempDir, token + ".bin");
        if (!System.IO.File.Exists(path))
        {
            TempData["Error"] = "انتهت صلاحية المعاينة. الرجاء رفع الملف مرة أخرى.";
            return RedirectToAction(nameof(Import));
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
        var analysis = await _import.AnalyzeAsync(bytes, fileName, ct);
        var result = await _import.CommitAsync(analysis, mode, fileName, this.CurrentUserId(), this.CurrentUserName(), ct);

        try { System.IO.File.Delete(path); } catch { /* ignore */ }

        TempData["Success"] = $"تم الاستيراد: {result.Created} موظف جديد، {result.Updated} محدَّث، {result.Skipped} متجاهَل.";
        return RedirectToAction(nameof(Index));
    }
}
