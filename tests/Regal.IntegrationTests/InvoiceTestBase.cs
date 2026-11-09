using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Products;

namespace Regal.IntegrationTests;

public abstract class InvoiceTestBase(PostgresFixture postgres) : WarehouseTestBase(postgres)
{
    protected FakeFakturownia Fakturownia { get; } = new();

    protected InvoiceService Invoices => new(Postgres.DbContextFactory, new FixedUser(UserId), Documents, Fakturownia, Clock);

    protected InvoiceSender Sender => new(Postgres.DbContextFactory, Fakturownia, Clock, NullLogger<InvoiceSender>.Instance);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // The database is shared by the whole collection: tasks left by other tests must not be picked up here.
        await using var db = Postgres.CreateAppDbContext();
        await db.Database.ExecuteSqlRawAsync("UPDATE \"OutboxMessages\" SET \"NextAttemptAt\" = NULL WHERE \"NextAttemptAt\" IS NOT NULL");
    }

    protected async Task<int> NewServiceAsync()
    {
        var created = await Products.CreateAsync(new ProductInput
        {
            Sku = Unique("USL"),
            Name = Unique("Usługa"),
            CategoryId = CategoryId,
            Unit = "h",
            Kind = ProductKind.Service,
            NetPrice = 100m,
        });
        Assert.True(created.IsSuccess, created.Error);
        return created.Id!.Value;
    }

    protected InvoiceDraft Draft(params InvoiceDraftLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), ContractorId = SupplierId, Lines = [.. lines] };

    protected static InvoiceDraftLine Item(int productId, decimal quantity, decimal? netPrice = null, decimal discount = 0) =>
        new() { ProductId = productId, Quantity = quantity, NetPrice = netPrice, DiscountPercent = discount };

    protected async Task<int> IssueOkAsync(InvoiceDraft draft)
    {
        var result = await Invoices.IssueAsync(draft);
        Assert.True(result.IsSuccess, result.Error);
        return result.Id!.Value;
    }

    /// <summary>A product with stock, so a WZ can be issued for it.</summary>
    protected async Task<int> StockedGoodsAsync(decimal quantity = 100, decimal cost = 5m)
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, quantity, cost)));
        return product;
    }

    protected async Task<Invoice> InvoiceAsync(int id)
    {
        await using var db = Postgres.CreateAppDbContext();
        return await db.Invoices.AsNoTracking().Include(i => i.Lines.OrderBy(l => l.Id)).SingleAsync(i => i.Id == id);
    }

    protected async Task<OutboxMessage> MessageAsync(int invoiceId)
    {
        await using var db = Postgres.CreateAppDbContext();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.InvoiceId == invoiceId);
    }

    protected async Task<int> InvoiceCountAsync(Guid requestId)
    {
        var oid = InvoiceRules.OidOf(requestId);
        await using var db = Postgres.CreateAppDbContext();
        return await db.Invoices.CountAsync(i => i.ExternalOrderId == oid);
    }

    protected async Task DrainAsync()
    {
        var sender = Sender;
        while (await sender.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}
