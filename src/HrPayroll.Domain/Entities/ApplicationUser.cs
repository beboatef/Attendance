using Microsoft.AspNetCore.Identity;

namespace HrPayroll.Domain.Entities;

/// <summary>مستخدم النظام.</summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Hr = "HR";
    public const string Accountant = "Accountant";
    public const string Viewer = "Viewer";

    public static readonly string[] All = { Admin, Hr, Accountant, Viewer };
}
