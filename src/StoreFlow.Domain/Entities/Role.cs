namespace StoreFlow.Domain.Entities;

/// <summary>A job role. Roles carry permissions; they never carry display behaviour.</summary>
public sealed class Role
{
    public int Id { get; set; }

    /// <summary>Stable machine-readable code, for example <c>owner</c> or <c>cashier</c>.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional Arabic display name.</summary>
    public string? NameAr { get; set; }

    /// <summary>True for the built-in administrator role, which bypasses grant checks.</summary>
    public bool IsAdministrator { get; set; }

    public ICollection<User> Users { get; } = new List<User>();

    public ICollection<RolePermission> RolePermissions { get; } = new List<RolePermission>();
}
