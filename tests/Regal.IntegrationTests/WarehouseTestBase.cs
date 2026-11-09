using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

public abstract class WarehouseTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    private sealed class FixedUser(int id) : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => Task.FromResult(id);
    }

    protected PostgresFixture Postgres => postgres;

    protected FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero));

    protected DateOnly Today => WarehouseTime.DateOf(Clock.GetUtcNow());

    protected int UserId { get; private set; }

    protected int CategoryId { get; private set; }

    protected int SupplierId { get; private set; }

    protected StockDocumentService Documents => new(postgres.DbContextFactory, new FixedUser(UserId), Clock);

    protected StockService Stock => new(postgres.DbContextFactory, new FixedUser(UserId), Clock);

    protected ProductService Products => new(postgres.DbContextFactory, new FixedUser(UserId), Clock);

    protected ContractorService Contractors => new(postgres.DbContextFactory, new FixedUser(UserId), Clock);

    public async Task InitializeAsync()
    {
        await using var db = postgres.CreateAppDbContext();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", Role = Roles.Administrator };
        var category = new Category { Name = Unique("Kategoria") };
        db.AddRange(user, category);
        await db.SaveChangesAsync();
        UserId = user.Id;
        CategoryId = category.Id;
        SupplierId = await NewContractorAsync();
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected static int SequenceOf(string number) => int.Parse(number.Split('/')[1]);

    protected async Task<int> NewContractorAsync() =>
        (await Contractors.SaveAsync(null, new ContractorInput { Name = Unique("Kontrahent") })).Id!.Value;

    protected async Task<int> NewGoodsAsync(string unit = "szt", bool hasExpiry = false)
    {
        var created = await Products.CreateAsync(new ProductInput
        {
            Sku = Unique("SKU"),
            Name = Unique("Produkt"),
            CategoryId = CategoryId,
            Unit = unit,
            NetPrice = 20m,
            HasExpiry = hasExpiry,
        });
        Assert.True(created.IsSuccess, created.Error);
        return created.Id!.Value;
    }

    protected static StockDocumentLine In(int productId, decimal quantity, decimal? cost, string? batch = null, DateOnly? expiry = null) =>
        new() { ProductId = productId, Quantity = quantity, UnitCost = cost, BatchNumber = batch, ExpiryDate = expiry };

    protected static StockDocumentLine Out(int productId, decimal quantity, int? batchId = null) =>
        new() { ProductId = productId, Quantity = quantity, BatchId = batchId };

    protected StockDocumentDraft Pz(params StockDocumentLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), Type = StockDocumentType.PZ, ContractorId = SupplierId, Lines = [.. lines] };

    protected static StockDocumentDraft Pw(params StockDocumentLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), Type = StockDocumentType.PW, Lines = [.. lines] };

    protected static StockDocumentDraft Wz(params StockDocumentLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), Type = StockDocumentType.WZ, Lines = [.. lines] };

    protected static StockDocumentDraft Rw(params StockDocumentLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), Type = StockDocumentType.RW, Notes = "Strata", Lines = [.. lines] };

    protected async Task<int> PostOkAsync(StockDocumentDraft draft)
    {
        var result = await Documents.PostAsync(draft);
        Assert.True(result.IsSuccess, result.Error);
        return result.Id!.Value;
    }

    protected async Task<int> ReverseOkAsync(int documentId)
    {
        var result = await Documents.ReverseAsync(documentId, Guid.NewGuid());
        Assert.True(result.IsSuccess, result.Error);
        return result.Id!.Value;
    }

    protected Task<SaveResult> ReverseAsync(int documentId) => Documents.ReverseAsync(documentId, Guid.NewGuid());

    protected async Task<decimal> StockOfAsync(int productId)
    {
        await using var db = postgres.CreateAppDbContext();
        return await db.Stock.Where(s => s.Batch!.ProductId == productId).SumAsync(s => s.Quantity);
    }

    protected async Task<decimal> AverageOfAsync(int productId)
    {
        await using var db = postgres.CreateAppDbContext();
        return await db.Products.Where(p => p.Id == productId).Select(p => p.AverageCost).SingleAsync();
    }

    /// <summary>Every batch of the product with its stock, oldest first.</summary>
    protected async Task<IReadOnlyList<(int Id, string Number, DateOnly? Expiry, decimal Quantity)>> BatchesOfAsync(int productId)
    {
        await using var db = postgres.CreateAppDbContext();
        var rows = await db.Batches.Where(b => b.ProductId == productId).OrderBy(b => b.Id)
            .Select(b => new
            {
                b.Id,
                b.Number,
                b.ExpiryDate,
                Quantity = db.Stock.Where(s => s.BatchId == b.Id).Sum(s => s.Quantity),
            })
            .ToListAsync();
        return rows.Select(r => (r.Id, r.Number, r.ExpiryDate, r.Quantity)).ToList();
    }

    protected async Task<StockDocument> DocumentAsync(int id)
    {
        await using var db = postgres.CreateAppDbContext();
        return await db.StockDocuments.AsNoTracking().Include(d => d.Movements).SingleAsync(d => d.Id == id);
    }

    protected async Task<int> DocumentCountAsync(Guid requestId)
    {
        await using var db = postgres.CreateAppDbContext();
        return await db.StockDocuments.CountAsync(d => d.RequestId == requestId);
    }
}
