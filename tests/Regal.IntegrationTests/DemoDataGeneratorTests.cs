using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Regal.Web.Data.Entities;
using Regal.Web.Data.Generator;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DemoDataGeneratorTests(PostgresFixture postgres)
{
    private static GeneratorOptions Small(string prefix, int seed = 7) => new()
    {
        Products = 30,
        Customers = 6,
        Suppliers = 2,
        Days = 40,
        InvoicesPerDay = 4,
        Seed = seed,
        SkuPrefix = prefix,
    };

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

    private static string Prefix() => "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Generator_refuses_any_environment_but_development_and_testing(string environment) =>
        Assert.Throws<InvalidOperationException>(() => DemoDataGenerator.EnsureAllowed(environment));

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Generator_runs_in_development_and_testing(string environment) =>
        DemoDataGenerator.EnsureAllowed(environment);

    [Fact]
    public async Task Generated_trade_follows_the_rules_and_is_never_sent()
    {
        var prefix = Prefix();

        var report = await new DemoDataGenerator(postgres.DbContextFactory, Small(prefix), Clock()).RunAsync();

        Assert.Equal(30, report.Products);
        Assert.True(report.Invoices > 20, $"Only {report.Invoices} invoices.");
        Assert.True(report.StockDocuments > 2);

        await using var db = postgres.CreateAppDbContext();
        var mine = db.Invoices.Where(i => i.Lines.Any(l => l.Product!.Sku.StartsWith(prefix + "-")));

        // Nothing for the sender to pick up, and nothing pretending to come from Fakturownia.
        Assert.False(await db.OutboxMessages.AnyAsync(m => m.NextAttemptAt != null && mine.Any(i => i.Id == m.InvoiceId)));
        Assert.True(await mine.Where(i => i.Status == InvoiceStatus.Sent).AllAsync(i => i.Number!.StartsWith("DEMO ") && i.FakturowniaId == null));
        Assert.False(await mine.AnyAsync(i => i.Status == InvoiceStatus.ToSend || i.Status == InvoiceStatus.Failed));

        // Dated over the simulated period, not all today.
        var dates = await mine.Select(i => i.IssueDate).Distinct().CountAsync();
        Assert.True(dates > 15, $"Invoices on only {dates} days.");

        // The generator's user owns the records and cannot sign in.
        var user = await db.Users.SingleAsync(u => u.Email == DemoDataGenerator.UserEmail);
        Assert.False(user.IsActive);
        Assert.True(await mine.AllAsync(i => i.UserId == user.Id));

        // Some goods never sell, so the slow-moving analysis has something to show.
        var unsold = await db.Products.CountAsync(p =>
            p.Sku.StartsWith(prefix + "-") && p.Kind == ProductKind.Goods && !db.InvoiceLines.Any(l => l.ProductId == p.Id));
        Assert.True(unsold > 0);
    }

    [Fact]
    public async Task Weekly_write_off_clears_all_expired_goods_even_in_a_large_catalog()
    {
        // More expiring batches in one week than one page of the write-off. An earlier version
        // wrote off a single page (200 batches) a week, so in a catalog of 10 000 products
        // expired stock piled up and most sales of it were refused. A page of 5 shows the
        // same case on a small catalog.
        var prefix = Prefix();
        var options = new GeneratorOptions
        {
            WriteOffPageSize = 5,
            Products = 300,
            Customers = 4,
            Suppliers = 2,
            Days = 60,
            InvoicesPerDay = 2,
            Seed = 11,
            SkuPrefix = prefix,
        };

        await new DemoDataGenerator(postgres.DbContextFactory, options, Clock()).RunAsync();

        // The simulation ends on Sunday 8.11.2026, so the last write-off ran on Monday 2.11.2026.
        var lastWriteOff = new DateOnly(2026, 11, 2);
        await using var db = postgres.CreateAppDbContext();
        var left = await db.Stock.CountAsync(s =>
            s.Quantity > 0 && s.Batch!.ExpiryDate < lastWriteOff && s.Batch.Product!.Sku.StartsWith(prefix + "-"));
        var writtenOff = await db.StockDocuments.CountAsync(d =>
            d.Type == StockDocumentType.RW && d.Movements.Any(m => m.Batch!.Product!.Sku.StartsWith(prefix + "-")));

        Assert.Equal(0, left);
        Assert.True(writtenOff > 0);
    }

    [Fact]
    public async Task Same_seed_gives_the_same_trade()
    {
        var first = await new DemoDataGenerator(postgres.DbContextFactory, Small(Prefix(), seed: 3), Clock()).RunAsync();
        var second = await new DemoDataGenerator(postgres.DbContextFactory, Small(Prefix(), seed: 3), Clock()).RunAsync();

        Assert.Equal((first.StockDocuments, first.Invoices, first.CancelledInvoices), (second.StockDocuments, second.Invoices, second.CancelledInvoices));
    }

    [Fact]
    public async Task Second_run_with_the_same_prefix_is_refused()
    {
        var prefix = Prefix();
        var options = Small(prefix);
        options.Days = 2;
        await new DemoDataGenerator(postgres.DbContextFactory, options, Clock()).RunAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new DemoDataGenerator(postgres.DbContextFactory, options, Clock()).RunAsync());
    }
}
