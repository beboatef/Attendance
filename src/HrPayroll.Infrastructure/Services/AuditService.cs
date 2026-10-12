using HrPayroll.Domain.Entities;
using HrPayroll.Infrastructure.Persistence;

namespace HrPayroll.Infrastructure.Services;

/// <summary>تسجيل العمليات الحساسة في سجل المراجعة.</summary>
public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public void Log(string action, string? entityName = null, string? entityId = null,
        string? oldValue = null, string? newValue = null, string? user = null, string? userId = null, string? ip = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
            UserName = user,
            UserId = userId,
            IpAddress = ip,
            Timestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user
        });
    }

    public async Task LogAndSaveAsync(string action, string? entityName = null, string? entityId = null,
        string? oldValue = null, string? newValue = null, string? user = null, string? userId = null, CancellationToken ct = default)
    {
        Log(action, entityName, entityId, oldValue, newValue, user, userId);
        await _db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? s) => s is null ? null : s.Length <= 4000 ? s : s[..4000];
}
