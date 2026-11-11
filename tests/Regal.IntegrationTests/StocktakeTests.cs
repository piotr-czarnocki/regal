using Microsoft.EntityFrameworkCore;
using Npgsql;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

/// <summary>Count sheets and the posting of their differences (SPEC 5.14).</summary>
[Collection(PostgresCollection.Name)]
public class StocktakeTests(PostgresFixture postgres) : WarehouseTestBase(postgres)
{
    private StocktakeService Stocktakes => new(Postgres.DbContextFactory, new FixedUser(UserId), Documents, Clock);

    private async Task<int> NewSheetAsync()
    {
        var created = await Stocktakes.CreateAsync();
        Assert.True(created.IsSuccess, created.Error);
        return created.Id!.Value;
    }

    private async Task<StocktakeDetails> SheetAsync(int id) => (await Stocktakes.GetDetailsAsync(id))!;

    private async Task CountAsync(int sheet, int productId, string batch, decimal counted)
    {
        var line = (await SheetAsync(sheet)).Lines.Single(l => l.ProductId == productId && l.BatchNumber == batch);
        var result = await Stocktakes.SetCountedAsync(sheet, line.Id, counted);
        Assert.True(result.Success, result.Message);
    }

    private async Task ApproveOkAsync(int sheet)
    {
        var result = await Stocktakes.ApproveAsync(sheet);
        Assert.True(result.IsSuccess, result.Error);
    }

    private async Task<decimal> BatchStockAsync(int productId, string batch) =>
        (await BatchesOfAsync(productId)).Single(b => b.Number == batch).Quantity;

    [Fact]
    public async Task Scanning_a_product_with_one_batch_counts_one_unit_per_scan()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A")));
        var sheet = await NewSheetAsync();

        await Stocktakes.AddProductAsync(sheet, product);
        await Stocktakes.AddProductAsync(sheet, product);
        var third = await Stocktakes.AddProductAsync(sheet, product);

        var line = Assert.Single((await SheetAsync(sheet)).Lines);
        Assert.Equal((3m, 5m, -2m), (line.Counted!.Value, line.Expected, line.Difference!.Value));
        Assert.Contains("policzono 3", third.Message);
    }

    [Fact]
    public async Task Scanning_goods_sold_by_weight_puts_them_on_the_sheet_without_counting()
    {
        var flour = await NewGoodsAsync(unit: "kg");
        await PostOkAsync(Pz(In(flour, 12.5m, 2m, "A")));
        var sheet = await NewSheetAsync();

        var first = await Stocktakes.AddProductAsync(sheet, flour);
        await Stocktakes.AddProductAsync(sheet, flour);

        // One scan is one item, which says nothing about kilograms: the weight is typed in.
        var line = Assert.Single((await SheetAsync(sheet)).Lines);
        Assert.Null(line.Counted);
        Assert.Contains("wpisz", first.Message);
    }

    [Fact]
    public async Task A_product_with_several_batches_puts_each_on_the_sheet_uncounted()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A"), In(product, 3, 2m, "B")));
        var sheet = await NewSheetAsync();

        await Stocktakes.AddProductAsync(sheet, product);
        await Stocktakes.AddProductAsync(sheet, product);

        var lines = (await SheetAsync(sheet)).Lines;
        Assert.Equal(["A", "B"], lines.Select(l => l.BatchNumber).Order());
        Assert.All(lines, l => Assert.Null(l.Counted));
    }

    [Fact]
    public async Task Services_and_goods_without_stock_cannot_be_counted()
    {
        var empty = await NewGoodsAsync();
        var sheet = await NewSheetAsync();

        var result = await Stocktakes.AddProductAsync(sheet, empty);

        Assert.False(result.Success);
        Assert.Contains("brak stanu", result.Message);
        Assert.Empty((await SheetAsync(sheet)).Lines);
    }

    [Fact]
    public async Task Approval_posts_a_shortage_as_rw_and_a_surplus_as_pw_on_the_same_batches()
    {
        var short1 = await NewGoodsAsync();
        var extra = await NewGoodsAsync();
        var exact = await NewGoodsAsync();
        await PostOkAsync(Pz(In(short1, 10, 2m, "S"), In(extra, 10, 4m, "E"), In(exact, 10, 1m, "X")));
        var untouched = await NewGoodsAsync();
        await PostOkAsync(Pz(In(untouched, 7, 1m, "U")));
        var sheet = await NewSheetAsync();
        foreach (var product in new[] { short1, extra, exact })
        {
            await Stocktakes.AddProductAsync(sheet, product);
        }

        await CountAsync(sheet, short1, "S", 8);
        await CountAsync(sheet, extra, "E", 12);
        await CountAsync(sheet, exact, "X", 10);
        await ApproveOkAsync(sheet);

        var details = await SheetAsync(sheet);
        Assert.Equal(StocktakeStatus.Approved, details.Status);
        Assert.Matches(@"^INW/\d+/2026$", details.Number);
        Assert.Equal((8m, 12m, 10m, 7m), (await StockOfAsync(short1), await StockOfAsync(extra), await StockOfAsync(exact), await StockOfAsync(untouched)));
        Assert.Equal(4m, await AverageOfAsync(extra));
        Assert.Equal(2, details.Documents.Count);
        var types = new List<StockDocumentType>();
        foreach (var link in details.Documents)
        {
            var document = await DocumentAsync(link.Id);
            types.Add(document.Type);
            Assert.Contains(details.Number!, document.Notes);
        }

        Assert.Equal([StockDocumentType.PW, StockDocumentType.RW], types.Order());
        Assert.Equal((10m, 8m), (details.Lines.Single(l => l.ProductId == short1).Expected, details.Lines.Single(l => l.ProductId == short1).Counted!.Value));
    }

    [Fact]
    public async Task Shortages_of_one_product_on_two_batches_give_two_rw()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A"), In(product, 5, 2m, "B")));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        await CountAsync(sheet, product, "A", 4);
        await CountAsync(sheet, product, "B", 1);

        await ApproveOkAsync(sheet);

        Assert.Equal((4m, 1m), (await BatchStockAsync(product, "A"), await BatchStockAsync(product, "B")));
        Assert.Equal(2, (await SheetAsync(sheet)).Documents.Count);
    }

    [Fact]
    public async Task The_difference_is_against_the_stock_at_approval()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 10, 2m, "A")));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        await CountAsync(sheet, product, "A", 6);

        await PostOkAsync(Wz(Out(product, 3)));
        await ApproveOkAsync(sheet);

        // Expected 7 at approval, counted 6: one unit short, so 6 remain.
        Assert.Equal(6m, await StockOfAsync(product));
        Assert.Equal(7m, (await SheetAsync(sheet)).Lines.Single().Expected);
    }

    [Fact]
    public async Task Approval_needs_every_line_counted_and_a_non_empty_sheet()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A"), In(product, 3, 2m, "B")));
        var empty = await NewSheetAsync();
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        await CountAsync(sheet, product, "A", 5);

        Assert.Contains("pusty", (await Stocktakes.ApproveAsync(empty)).Error);
        Assert.Contains("1 bez ilości", (await Stocktakes.ApproveAsync(sheet)).Error);
        Assert.Equal(StocktakeStatus.Draft, (await SheetAsync(sheet)).Status);
    }

    [Fact]
    public async Task A_sheet_without_differences_is_approved_with_no_documents()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A")));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        await CountAsync(sheet, product, "A", 5);

        await ApproveOkAsync(sheet);
        await ApproveOkAsync(sheet);

        Assert.Empty((await SheetAsync(sheet)).Documents);
        Assert.NotNull((await SheetAsync(sheet)).Number);
    }

    [Fact]
    public async Task A_surplus_on_an_expired_batch_is_refused_and_nothing_is_posted()
    {
        var product = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(product, 5, 2m, "OLD", Today.AddDays(1))));
        Clock.Advance(TimeSpan.FromDays(3));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        await CountAsync(sheet, product, "OLD", 6);

        var result = await Stocktakes.ApproveAsync(sheet);

        Assert.Contains("przeterminowaną", result.Error);
        Assert.Equal(5m, await StockOfAsync(product));
        Assert.Equal(StocktakeStatus.Draft, (await SheetAsync(sheet)).Status);
    }

    [Fact]
    public async Task Counts_are_validated()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A")));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        var line = (await SheetAsync(sheet)).Lines.Single();

        Assert.Contains("ujemna", (await Stocktakes.SetCountedAsync(sheet, line.Id, -1)).Message);
        Assert.Contains("całkowitą", (await Stocktakes.SetCountedAsync(sheet, line.Id, 1.5m)).Message);
        Assert.True((await Stocktakes.SetCountedAsync(sheet, line.Id, 0)).Success);
    }

    [Fact]
    public async Task A_closed_sheet_refuses_every_change_in_the_service_and_in_the_database()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A")));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        await ApproveOkAsync(sheet);
        var line = (await SheetAsync(sheet)).Lines.Single();

        Assert.False((await Stocktakes.AddProductAsync(sheet, product)).Success);
        Assert.False((await Stocktakes.SetCountedAsync(sheet, line.Id, 2)).Success);
        Assert.False((await Stocktakes.RemoveLineAsync(sheet, line.Id)).Success);
        Assert.Contains("nie można anulować", (await Stocktakes.CancelAsync(sheet)).Error);

        await using var db = Postgres.CreateAppDbContext();
        var lineChange = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StocktakeLines\" SET \"Counted\" = 99 WHERE \"Id\" = {line.Id}"));
        var sheetChange = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Stocktakes\" SET \"Status\" = 'Draft' WHERE \"Id\" = {sheet}"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Stocktakes\" WHERE \"Id\" = {sheet}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, lineChange.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, sheetChange.SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, delete.SqlState);
    }

    [Fact]
    public async Task A_cancelled_sheet_posts_nothing_and_stays_cancelled()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A")));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        await CountAsync(sheet, product, "A", 1);

        Assert.True((await Stocktakes.CancelAsync(sheet)).IsSuccess);

        Assert.False((await Stocktakes.ApproveAsync(sheet)).IsSuccess);
        Assert.Equal(5m, await StockOfAsync(product));
        Assert.Null((await SheetAsync(sheet)).Number);
    }

    /// <summary>
    /// The approval posts a PW for the surplus on Y and then an RW for the shortage on X. Another
    /// transaction holds X and then asks for Y, as a WZ of X and Y does (ascending). If the approval
    /// locked as its documents go, it would hold Y and wait for X: a deadlock. Locking all of the
    /// sheet's products first, ascending, it waits for X before touching Y. Deterministic, unlike a
    /// race between real documents, whose window is a few milliseconds.
    /// </summary>
    [Fact]
    public async Task Approval_locks_all_its_products_before_posting_so_it_cannot_deadlock_with_an_issue()
    {
        var x = await NewGoodsAsync();
        var y = await NewGoodsAsync(); // created second, so its id is higher
        await PostOkAsync(Pz(In(x, 10, 2m, "X"), In(y, 10, 2m, "Y")));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, x);
        await Stocktakes.AddProductAsync(sheet, y);
        await CountAsync(sheet, x, "X", 1);
        await CountAsync(sheet, y, "Y", 12);
        var stocktakes = Stocktakes;

        await using var db = Postgres.CreateAppDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Products.FromSql($"""SELECT * FROM "Products" WHERE "Id" = {x} FOR UPDATE""").ToListAsync();
        var approval = Task.Run(() => stocktakes.ApproveAsync(sheet));
        await Task.Delay(TimeSpan.FromSeconds(1)); // the approval is now waiting for X

        var lockY = db.Products.FromSql($"""SELECT * FROM "Products" WHERE "Id" = {y} FOR UPDATE""").ToListAsync();
        var first = await Task.WhenAny(lockY, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(lockY, first);
        await lockY; // a deadlock victim would throw 40P01 here
        await transaction.CommitAsync();
        var result = await approval;
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal((1m, 12m), (await StockOfAsync(x), await StockOfAsync(y)));
    }

    [Fact]
    public async Task Parallel_approvals_and_issues_keep_stock_consistent()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 20, 2m, "A")));
        var sheet = await NewSheetAsync();
        await Stocktakes.AddProductAsync(sheet, product);
        await CountAsync(sheet, product, "A", 15);
        var stocktakes = Stocktakes;
        var documents = Documents;

        var approvals = Enumerable.Range(0, 3).Select(_ => Task.Run(() => stocktakes.ApproveAsync(sheet)));
        var issues = Enumerable.Range(0, 3).Select(_ => Task.Run(() => documents.PostAsync(Wz(Out(product, 1)))));
        await Task.WhenAll(approvals.Concat<Task>(issues));

        // Whichever order: the sheet set the batch to 15 at its moment, and the issues took 1 each.
        var details = await SheetAsync(sheet);
        var expected = details.Lines.Single().Expected;
        var issuedBefore = 20m - expected;
        var issuedAfter = 3m - issuedBefore;
        Assert.Equal(15m - issuedAfter, await StockOfAsync(product));
        Assert.True(details.Documents.Count <= 1);
    }
}
