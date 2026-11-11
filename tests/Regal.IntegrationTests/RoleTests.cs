using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Regal.TestHost;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Analytics;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Orders;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

/// <summary>
/// The warehouse role through the real CurrentUser and the identity built at login (SPEC 5.19).
/// The other tests use a user that ignores roles, so only these prove who may do what.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RoleTests(PostgresFixture postgres) : WarehouseTestBase(postgres)
{
    private sealed class SignedIn(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    private async Task<(int Id, CurrentUser User)> SignInAsync(string role)
    {
        await using var db = Postgres.CreateAppDbContext();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", Role = role };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, new CurrentUser(new SignedIn(AuthService.CreatePrincipal(user, null))));
    }

    [Fact]
    public async Task A_warehouse_worker_receives_issues_and_counts_goods()
    {
        var product = await NewGoodsAsync();
        var (_, worker) = await SignInAsync(Roles.Warehouse);
        var documents = new StockDocumentService(Postgres.DbContextFactory, worker, Clock);
        var stock = new StockService(Postgres.DbContextFactory, worker, Clock);
        var stocktakes = new StocktakeService(Postgres.DbContextFactory, worker, documents, Clock);

        var receipt = await documents.PostAsync(new StockDocumentDraft
        {
            RequestId = Guid.NewGuid(), Type = StockDocumentType.PZ, ContractorId = SupplierId, Lines = [In(product, 5, 2m, "A")],
        });
        Assert.True(receipt.IsSuccess, receipt.Error);
        Assert.True((await documents.PostAsync(new StockDocumentDraft
        {
            RequestId = Guid.NewGuid(), Type = StockDocumentType.RW, Notes = "Uszkodzone", Lines = [Out(product, 1)],
        })).IsSuccess);

        var sheet = await stocktakes.CreateAsync();
        Assert.True(sheet.IsSuccess, sheet.Error);
        Assert.True((await stocktakes.AddProductAsync(sheet.Id!.Value, product)).Success);
        Assert.True((await stocktakes.ApproveAsync(sheet.Id.Value)).IsSuccess);

        // What the warehouse pages read besides their own services.
        Assert.NotEmpty((await stock.SearchGoodsAsync("", 10)));
        Assert.NotNull(await new ContractorService(Postgres.DbContextFactory, worker, Clock).SearchAsync(null, false, 0, 10));
        Assert.NotNull(await new CategoryService(Postgres.DbContextFactory, worker, Clock).GetAllAsync());
        Assert.NotNull(await new ProductService(Postgres.DbContextFactory, worker, Clock).GetForEditAsync(product));
        Assert.NotNull(await new AnalyticsService(Postgres.DbContextFactory, worker, Clock).GetExpiringBatchesAsync(14));
    }

    [Fact]
    public async Task A_warehouse_worker_gets_nothing_from_sales_catalog_analytics_or_users()
    {
        var (_, worker) = await SignInAsync(Roles.Warehouse);
        var documents = new StockDocumentService(Postgres.DbContextFactory, worker, Clock);
        var analytics = new AnalyticsService(Postgres.DbContextFactory, worker, Clock);
        var filter = new AnalyticsFilter(Today.AddDays(-30), Today);

        Func<Task>[] denied =
        [
            () => new InvoiceService(Postgres.DbContextFactory, worker, documents, new FakeFakturownia(), Clock).IssueAsync(new InvoiceDraft { RequestId = Guid.NewGuid() }),
            () => new InvoiceQueries(Postgres.DbContextFactory, worker).GetDetailsAsync(1),
            () => new InvoicePaymentService(Postgres.DbContextFactory, worker, new FakeFakturownia(), Clock).GetReceivablesAsync(),
            () => new CustomerOrderService(Postgres.DbContextFactory, worker, Clock).CreateAsync(new OrderDraft { RequestId = Guid.NewGuid() }),
            () => analytics.GetSummaryAsync(filter),
            () => analytics.GetSalesByProductAsync(filter),
            () => new ProductService(Postgres.DbContextFactory, worker, Clock).CreateAsync(new ProductInput()),
            () => new ProductService(Postgres.DbContextFactory, worker, Clock).SearchAsync(null, false, 0, 10),
            () => new CategoryService(Postgres.DbContextFactory, worker, Clock).SaveAsync(null, Unique("K")),
            () => new ContractorService(Postgres.DbContextFactory, worker, Clock).SaveAsync(null, new ContractorInput { Name = Unique("K") }),
            () => new LabelService(Postgres.DbContextFactory, worker).BuildAsync([]),
            () => new UserService(Postgres.DbContextFactory, worker, Clock).GetAllAsync(),
            () => new UserService(Postgres.DbContextFactory, worker, Clock).AddAsync("ktos@example.com", Roles.Administrator),
        ];

        foreach (var call in denied)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(call);
        }
    }

    [Fact]
    public async Task An_administrator_adds_a_worker_who_can_then_sign_in()
    {
        var (adminId, admin) = await SignInAsync(Roles.Administrator);
        var users = new UserService(Postgres.DbContextFactory, admin, Clock);
        var email = $"{Guid.NewGuid():N}@Example.com";

        var added = await users.AddAsync($"  {email} ", Roles.Warehouse);

        Assert.True(added.IsSuccess, added.Error);
        var login = await Postgres.CreateAuthService().AuthorizeLoginAsync(email, emailVerified: true);
        Assert.True(login.IsAllowed);
        Assert.Equal((email.ToLowerInvariant(), Roles.Warehouse), (login.User!.Email, login.User.Role));
        Assert.Contains(await users.GetAllAsync(), u => u.Id == added.Id && !u.IsMe);
        Assert.Contains(await users.GetAllAsync(), u => u.Id == adminId && u.IsMe);

        await using var db = Postgres.CreateAppDbContext();
        Assert.True(await db.AuditLog.AnyAsync(a => a.EntityType == nameof(User) && a.EntityId == added.Id.ToString() && a.UserId == adminId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bez-malpy")]
    [InlineData("dwa@adresy.pl, trzy@adresy.pl")]
    public async Task A_wrong_address_or_role_is_refused(string email)
    {
        var (_, admin) = await SignInAsync(Roles.Administrator);
        var users = new UserService(Postgres.DbContextFactory, admin, Clock);

        Assert.False((await users.AddAsync(email, Roles.Warehouse)).IsSuccess);
        Assert.False((await users.AddAsync($"{Guid.NewGuid():N}@example.com", "Kierownik")).IsSuccess);
    }

    [Fact]
    public async Task The_same_address_cannot_be_added_twice()
    {
        var (_, admin) = await SignInAsync(Roles.Administrator);
        var users = new UserService(Postgres.DbContextFactory, admin, Clock);
        var email = $"{Guid.NewGuid():N}@example.com";

        Assert.True((await users.AddAsync(email, Roles.Warehouse)).IsSuccess);
        var again = await users.AddAsync(email.ToUpperInvariant(), Roles.Administrator);

        Assert.False(again.IsSuccess);
        Assert.Contains("już na liście", again.Error);
    }

    [Fact]
    public async Task Changing_the_role_ends_the_open_session_and_nobody_can_lock_themselves_out()
    {
        var (adminId, admin) = await SignInAsync(Roles.Administrator);
        var users = new UserService(Postgres.DbContextFactory, admin, Clock);
        var added = await users.AddAsync($"{Guid.NewGuid():N}@example.com", Roles.Administrator);
        await using var db = Postgres.CreateAppDbContext();
        var before = AuthService.CreatePrincipal(await db.Users.AsNoTracking().SingleAsync(u => u.Id == added.Id), null);
        var auth = Postgres.CreateAuthService();
        Assert.True(await auth.IsSessionValidAsync(before));

        Assert.True((await users.SetRoleAsync(added.Id!.Value, Roles.Warehouse)).IsSuccess);
        Assert.False(await auth.IsSessionValidAsync(before));

        Assert.False((await users.SetRoleAsync(adminId, Roles.Warehouse)).IsSuccess);
        Assert.False((await users.SetActiveAsync(adminId, false)).IsSuccess);
        var me = await db.Users.AsNoTracking().SingleAsync(u => u.Id == adminId);
        Assert.Equal((Roles.Administrator, true), (me.Role, me.IsActive));

        Assert.True((await users.SetActiveAsync(added.Id.Value, false)).IsSuccess);
        Assert.False((await auth.AuthorizeLoginAsync((await users.GetAllAsync()).Single(u => u.Id == added.Id).Email, emailVerified: true)).IsAllowed);
    }
}
