namespace Regal.Web.Data.Entities;

public sealed class User
{
    public int Id { get; set; }

    /// <summary>Always stored trimmed and in lower case, see EmailAddress.Normalize.</summary>
    public required string Email { get; set; }

    public required string Role { get; set; }

    public bool IsActive { get; set; } = true;
}
