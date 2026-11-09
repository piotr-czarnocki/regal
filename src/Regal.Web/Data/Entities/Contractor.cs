namespace Regal.Web.Data.Entities;

/// <summary>A company or a private person; the same record can be a customer and a supplier.</summary>
public sealed class Contractor
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Ten digits without separators, or null.</summary>
    public string? Nip { get; set; }

    public string? Address { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;
}
