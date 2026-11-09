using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class WarehouseSecurityTests(PostgresFixture postgres) : WarehouseTestBase(postgres)
{
    private sealed class DeniedUser : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => throw new UnauthorizedAccessException();
    }

    [Theory]
    [InlineData("UPDATE \"StockDocuments\" SET \"Notes\" = 'zmiana'")]
    [InlineData("DELETE FROM \"StockDocuments\"")]
    [InlineData("UPDATE \"StockMovements\" SET \"UnitCost\" = 1")]
    [InlineData("DELETE FROM \"StockMovements\"")]
    [InlineData("UPDATE \"Batches\" SET \"Number\" = 'zmiana'")]
    [InlineData("DELETE FROM \"Batches\"")]
    public async Task Application_role_cannot_change_or_delete_approved_rows(string sql)
    {
        await using var db = Postgres.CreateAppDbContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Database_refuses_negative_stock_as_the_last_barrier()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 1, 5m)));
        var batch = Assert.Single(await BatchesOfAsync(product));

        await using var db = Postgres.CreateAppDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Stock.Where(s => s.BatchId == batch.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, -1m)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Database_keeps_one_correction_per_document()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 1, 5m)));
        await ReverseOkAsync(pz);

        await using var db = Postgres.CreateAppDbContext();
        db.StockDocuments.Add(new StockDocument
        {
            Type = StockDocumentType.PZ,
            Number = Unique("X")[..20],
            UserId = UserId,
            ReversesDocumentId = pz,
            RequestId = Guid.NewGuid(),
            CreatedAt = Clock.GetUtcNow().UtcDateTime,
            DocumentDate = Today,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (exception.InnerException as PostgresException)?.SqlState);
    }

    [Fact]
    public async Task Every_service_method_requires_an_administrator()
    {
        var clock = new FakeTimeProvider();
        var documents = new StockDocumentService(Postgres.DbContextFactory, new DeniedUser(), clock);
        var stock = new StockService(Postgres.DbContextFactory, new DeniedUser(), clock);
        var draft = Pw(In(1, 1, 1m));
        var filter = new StockDocumentFilter(null, null, null, null);
        await using var db = Postgres.CreateAppDbContext();

        Func<Task>[] calls =
        [
            () => documents.PostAsync(draft),
            () => documents.PostInTransactionAsync(db, draft),
            () => documents.ReverseAsync(1, Guid.NewGuid()),
            () => documents.PreviewIssueAsync(draft),
            () => documents.SearchAsync(filter, 0, 10),
            () => documents.GetDetailsAsync(1),
            () => stock.SearchAsync(new StockFilter(null, null, null), 0, 10),
            () => stock.GetBatchesAsync(1),
            () => stock.GetProductMovementsAsync(1, 0, 10),
            () => stock.SearchGoodsAsync(null, 10),
            () => stock.FindByCodeAsync("5901234123457"),
        ];

        foreach (var call in calls)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(call);
        }
    }

    [Fact]
    public async Task Text_with_sql_in_it_is_only_data()
    {
        var product = await NewGoodsAsync();
        const string number = "X'; DROP TABLE \"Stock\";--";

        await PostOkAsync(Pz(In(product, 1, 5m, number)));

        Assert.Equal(number.ToUpperInvariant(), Assert.Single(await BatchesOfAsync(product)).Number);
    }
}
