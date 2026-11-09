namespace Regal.Web.Data.Entities;

public sealed class Warehouse
{
    public const int MainWarehouseId = 1;

    public int Id { get; set; }

    public required string Name { get; set; }
}
