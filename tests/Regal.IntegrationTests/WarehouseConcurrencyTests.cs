using Microsoft.EntityFrameworkCore;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Common;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class WarehouseConcurrencyTests(PostgresFixture postgres) : WarehouseTestBase(postgres)
{
    private Task<SaveResult[]> PostAllAsync(IEnumerable<StockDocumentDraft> drafts)
    {
        var service = Documents;
        return Task.WhenAll(drafts.Select(draft => Task.Run(() => service.PostAsync(draft))));
    }

    [Fact]
    public async Task Parallel_issues_exceeding_stock_exactly_one_succeeds_and_stock_stays_valid()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 10, 5m)));

        var results = await PostAllAsync(Enumerable.Range(0, 12).Select(_ => Wz(Out(product, 7))));

        Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Contains("brak towaru", r.Error));
        Assert.Equal(3m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Parallel_issues_within_stock_all_succeed_across_several_batches()
    {
        var product = await NewGoodsAsync();
        var lines = Enumerable.Range(1, 10).Select(i => In(product, 5, 5m, $"P{i}")).ToArray();
        await PostOkAsync(Pz(lines));

        var results = await PostAllAsync(Enumerable.Range(0, 15).Select(_ => Wz(Out(product, 3))));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(5m, await StockOfAsync(product));
        Assert.All(await BatchesOfAsync(product), b => Assert.True(b.Quantity >= 0));
    }

    [Fact]
    public async Task Parallel_receipts_of_one_product_give_the_correct_average()
    {
        var product = await NewGoodsAsync();
        var receipts = Enumerable.Range(0, 20)
            .Select(i => (Quantity: 1m + (i % 3), Cost: i % 2 == 0 ? 10m : 30m))
            .ToList();
        var expected = receipts.Sum(r => r.Quantity * r.Cost) / receipts.Sum(r => r.Quantity);

        var results = await PostAllAsync(receipts.Select((r, i) => Pz(In(product, r.Quantity, r.Cost, $"R{i}"))));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(receipts.Sum(r => r.Quantity), await StockOfAsync(product));

        // Each step rounds to four places, so the order of arrival can move the result slightly.
        Assert.InRange(await AverageOfAsync(product), expected - 0.01m, expected + 0.01m);
    }

    [Fact]
    public async Task Parallel_documents_get_consecutive_numbers_without_duplicates()
    {
        var products = new List<int>();
        for (var i = 0; i < 20; i++)
        {
            products.Add(await NewGoodsAsync());
        }

        var results = await PostAllAsync(products.Select(product => Pw(In(product, 1, 5m))));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        var sequence = new List<int>();
        foreach (var result in results)
        {
            sequence.Add(SequenceOf((await DocumentAsync(result.Id!.Value)).Number));
        }

        sequence.Sort();
        Assert.Equal(Enumerable.Range(sequence[0], 20), sequence);
    }

    [Fact]
    public async Task Documents_listing_products_in_opposite_order_do_not_deadlock()
    {
        var first = await NewGoodsAsync();
        var second = await NewGoodsAsync();
        await PostOkAsync(Pz(In(first, 100, 5m), In(second, 100, 5m)));

        // Two types on purpose: documents of one type already queue at their number counter,
        // and that alone would keep them from ever updating stock at the same time.
        var results = await PostAllAsync(Enumerable.Range(0, 40)
            .Select(i => i % 2 == 0 ? Wz(Out(first, 1), Out(second, 1)) : Rw(Out(second, 1), Out(first, 1))));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(60m, await StockOfAsync(first));
        Assert.Equal(60m, await StockOfAsync(second));
    }

    [Fact]
    public async Task Parallel_sends_of_one_draft_create_one_document()
    {
        var product = await NewGoodsAsync();
        var draft = Pz(In(product, 10, 5m));

        var results = await PostAllAsync(Enumerable.Repeat(draft, 8));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal(1, await DocumentCountAsync(draft.RequestId));
        Assert.Equal(10m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Parallel_corrections_of_one_document_create_one_correction()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 10, 5m)));
        var service = Documents;

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => service.ReverseAsync(pz, Guid.NewGuid()))));

        Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal("Ten dokument ma już korektę.", r.Error));
        Assert.Equal(0m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Deactivation_racing_with_a_receipt_never_leaves_an_inactive_product_with_stock()
    {
        for (var round = 0; round < 10; round++)
        {
            var product = await NewGoodsAsync();
            var products = Products;
            var documents = Documents;

            var receipt = Task.Run(() => documents.PostAsync(Pz(In(product, 1, 5m))));
            var deactivation = Task.Run(() => products.SetActiveAsync(product, false));
            await Task.WhenAll(receipt, deactivation);

            await using var db = Postgres.CreateAppDbContext();
            var isActive = await db.Products.Where(p => p.Id == product).Select(p => p.IsActive).SingleAsync();
            Assert.False(!isActive && await StockOfAsync(product) > 0, "An inactive product received stock.");
        }
    }
}
