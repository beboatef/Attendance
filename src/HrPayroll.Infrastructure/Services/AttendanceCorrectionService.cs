using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPayroll.Infrastructure.Services;

/// <summary>
/// التصحيح اليدوي لسجلات الحضور.
/// كل تصحيح يسجّل القيمة الأصلية والمصححة والسبب والمستخدم والوقت،
/// وتبقى البصمات الخام محفوظة كما وردت من الجهاز.
/// </summary>
public class AttendanceCorrectionService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public AttendanceCorrectionService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public sealed class CorrectionRequest
    {
        public int DailyAttendanceId { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public AttendanceStatus? Status { get; set; }
        public int? ApprovedOvertimeMinutes { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool Approve { get; set; } = true;
    }

    public async Task<DailyAttendance> CorrectAsync(
        CorrectionRequest request, string? user, string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("سبب التصحيح مطلوب.");

        var daily = await _db.DailyAttendances.Include(d => d.Shift)
                        .FirstOrDefaultAsync(d => d.Id == request.DailyAttendanceId, ct)
                    ?? throw new InvalidOperationException("سجل الحضور غير موجود");

        await EnsurePeriodNotLockedAsync(daily.WorkDate, ct);

        var corrections = new List<AttendanceCorrection>();

        void Record(string field, string? oldValue, string? newValue)
        {
            if (oldValue == newValue) return;
            corrections.Add(new AttendanceCorrection
            {
                DailyAttendanceId = daily.Id,
                EmployeeId = daily.EmployeeId,
                WorkDate = daily.WorkDate,
                FieldName = field,
                OldValue = oldValue,
                NewValue = newValue,
                Reason = request.Reason,
                Status = request.Approve ? ApprovalStatus.Approved : ApprovalStatus.Pending,
                ApprovedBy = request.Approve ? user : null,
                ApprovedAt = request.Approve ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = user
            });
        }

        if (request.CheckIn.HasValue && request.CheckIn.Value != daily.CheckInTime)
            Record(nameof(DailyAttendance.CheckInTime), daily.CheckInTime?.ToString("yyyy-MM-dd HH:mm"), request.CheckIn.Value.ToString("yyyy-MM-dd HH:mm"));

        if (request.CheckOut.HasValue && request.CheckOut.Value != daily.CheckOutTime)
            Record(nameof(DailyAttendance.CheckOutTime), daily.CheckOutTime?.ToString("yyyy-MM-dd HH:mm"), request.CheckOut.Value.ToString("yyyy-MM-dd HH:mm"));

        if (request.Status.HasValue && request.Status.Value != daily.Status)
            Record(nameof(DailyAttendance.Status), daily.Status.ToString(), request.Status.Value.ToString());

        if (request.ApprovedOvertimeMinutes.HasValue && request.ApprovedOvertimeMinutes.Value != daily.ApprovedOvertimeMinutes)
            Record(nameof(DailyAttendance.ApprovedOvertimeMinutes), daily.ApprovedOvertimeMinutes.ToString(), request.ApprovedOvertimeMinutes.Value.ToString());

        if (corrections.Count == 0)
            throw new InvalidOperationException("لا توجد أي قيمة مختلفة لتصحيحها.");

        if (request.CheckIn.HasValue) daily.CheckInTime = request.CheckIn.Value;
        if (request.CheckOut.HasValue) daily.CheckOutTime = request.CheckOut.Value;
        if (request.Status.HasValue) daily.Status = request.Status.Value;
        if (request.ApprovedOvertimeMinutes.HasValue)
        {
            var minutes = Math.Max(0, request.ApprovedOvertimeMinutes.Value);
            if (minutes > daily.RawOvertimeMinutes && daily.RawOvertimeMinutes > 0)
                minutes = daily.RawOvertimeMinutes;
            daily.ApprovedOvertimeMinutes = minutes;
        }

        daily.IsManualOverride = true;
        daily.HasIncompletePunches = daily.CheckInTime is null || daily.CheckOutTime is null;
        daily.AdminNotes = request.Reason;
        daily.UpdatedAt = DateTime.UtcNow;
        daily.UpdatedBy = user;

        _db.AttendanceCorrections.AddRange(corrections);
        await _audit.LogAsync("تصحيح حضور", nameof(DailyAttendance), daily.Id.ToString(),
            string.Join("; ", corrections.Select(c => $"{c.FieldName}:{c.OldValue}")),
            string.Join("; ", corrections.Select(c => $"{c.FieldName}:{c.NewValue}")),
            user, userId, ct);
        await _db.SaveChangesAsync(ct);

        return daily;
    }

    /// <summary>يمنع تعديل حضور فترة رواتب مقفلة.</summary>
    public async Task EnsurePeriodNotLockedAsync(DateTime workDate, CancellationToken ct = default)
    {
        var date = workDate.Date;
        var locked = await _db.PayrollRuns.AsNoTracking()
            .AnyAsync(r => r.Year == date.Year && r.Month == date.Month
                           && (r.Status == PayrollStatus.Locked || r.Status == PayrollStatus.Approved), ct);
        if (locked)
            throw new InvalidOperationException(
                $"فترة {date:yyyy-MM} مقفلة براتب معتمد. أعد فتح الكشف بسبب مُسجَّل قبل تعديل الحضور.");
    }

    /// <summary>سجل التصحيحات لسجل حضور معين.</summary>
    public async Task<List<AttendanceCorrection>> GetHistoryAsync(int dailyAttendanceId, CancellationToken ct = default)
        => await _db.AttendanceCorrections.AsNoTracking()
            .Where(c => c.DailyAttendanceId == dailyAttendanceId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
}
