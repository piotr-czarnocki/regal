namespace Regal.Web.Data.Entities;

/// <summary>
/// Append-only journal entry. Must never contain tokens or secrets.
/// The application's database role has no UPDATE or DELETE on this table.
/// </summary>
public sealed class AuditLogEntry
{
    public long Id { get; set; }

    /// <summary>Null when the actor is not a known user, e.g. a denied address.</summary>
    public int? UserId { get; set; }

    public User? User { get; set; }

    public required string Action { get; set; }

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string? Details { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
