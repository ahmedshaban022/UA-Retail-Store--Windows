using StoreFlow.Domain.Authorization;

namespace StoreFlow.Domain.Entities;

/// <summary>
/// Grants one permission to one role. The composite key is (RoleId, Permission); the permission is
/// stored as its enum value so the matrix stays compact and queryable.
/// </summary>
public sealed class RolePermission
{
    public int RoleId { get; set; }

    public Role? Role { get; set; }

    public Permission Permission { get; set; }
}
