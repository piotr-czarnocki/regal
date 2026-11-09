using Microsoft.EntityFrameworkCore;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class CatalogTests(PostgresFixture postgres) : IAsyncLifetime
{
    private sealed class FixedUser(int id) : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => Task.FromResult(id);
    }

    private int _userId;
    private int _categoryId;

    private CategoryService Categories => new(postgres.DbContextFactory, new FixedUser(_userId), TimeProvider.System);
    private ProductService Products => new(postgres.DbContextFactory, new FixedUser(_userId), TimeProvider.System);
    private ContractorService Contractors => new(postgres.DbContextFactory, new FixedUser(_userId), TimeProvider.System);

    public async Task InitializeAsync()
    {
        await using var db = postgres.CreateAppDbContext();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", Role = Roles.Administrator };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        _userId = user.Id;

        _categoryId = (await Categories.SaveAsync(null, Unique("Kategoria"))).Id!.Value;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>A valid, non-internal EAN that no other test will produce.</summary>
    private static string UniqueEan()
    {
        var body = "59" + Random.Shared.NextInt64(0, 10_000_000_000).ToString("D10");
        return body + Ean13.ComputeCheckDigit(body);
    }

    private ProductInput Goods(string? sku = null, string? ean = null) => new()
    {
        Sku = sku ?? Unique("SKU"),
        Ean = ean,
        Name = Unique("Produkt"),
        CategoryId = _categoryId,
        Unit = "szt",
        NetPrice = 12.50m,
    };

    private async Task<IReadOnlyList<AuditLogEntry>> AuditFor(string entityType, int id)
    {
        var entityId = id.ToString();
        await using var db = postgres.CreateAppDbContext();
        return await db.AuditLog.Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task Goods_without_ean_get_a_valid_internal_code_from_the_sequence()
    {
        var first = await Products.CreateAsync(Goods());
        var second = await Products.CreateAsync(Goods());

        await using var db = postgres.CreateAppDbContext();
        var firstEan = await db.Products.Where(p => p.Id == first.Id).Select(p => p.Ean).SingleAsync();
        var secondEan = await db.Products.Where(p => p.Id == second.Id).Select(p => p.Ean).SingleAsync();

        Assert.True(Ean13.IsValid(firstEan));
        Assert.StartsWith("040", firstEan);
        Assert.NotEqual(firstEan, secondEan);
    }

    [Fact]
    public async Task Sku_must_be_unique_regardless_of_letter_case_and_a_failed_save_leaves_no_trace()
    {
        var sku = Unique("sku");
        Assert.True((await Products.CreateAsync(Goods(sku))).IsSuccess);

        var duplicate = await Products.CreateAsync(Goods(sku.ToUpperInvariant()));

        Assert.Equal("Produkt o tym SKU już istnieje.", duplicate.Error);
        await using var db = postgres.CreateAppDbContext();
        Assert.Equal(1, await db.Products.CountAsync(p => p.Sku == sku.ToUpperInvariant()));
    }

    [Fact]
    public async Task Ean_must_be_unique()
    {
        var ean = UniqueEan();
        Assert.True((await Products.CreateAsync(Goods(ean: ean))).IsSuccess);

        var duplicate = await Products.CreateAsync(Goods(ean: ean));

        Assert.Equal("Produkt o tym kodzie EAN już istnieje.", duplicate.Error);
    }

    [Fact]
    public async Task Edit_cannot_take_an_sku_used_by_another_product()
    {
        var taken = Unique("SKU").ToUpperInvariant();
        await Products.CreateAsync(Goods(taken));
        var other = await Products.CreateAsync(Goods());

        var result = await Products.UpdateAsync(other.Id!.Value, Goods(taken));

        Assert.Equal("Produkt o tym SKU już istnieje.", result.Error);
    }

    [Theory]
    [InlineData("5901234123458")] // wrong check digit
    [InlineData("0400000000015")] // reserved for generated codes
    public async Task Manually_entered_ean_is_validated(string ean)
    {
        var result = await Products.CreateAsync(Goods(ean: ean));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Services_have_no_ean_and_no_expiry()
    {
        var input = Goods();
        input.Kind = ProductKind.Service;
        input.HasExpiry = true;

        var result = await Products.CreateAsync(input);

        await using var db = postgres.CreateAppDbContext();
        var service = await db.Products.SingleAsync(p => p.Id == result.Id);
        Assert.Null(service.Ean);
        Assert.False(service.HasExpiry);
    }

    [Fact]
    public async Task Editing_keeps_the_generated_ean_kind_and_average_cost()
    {
        var created = await Products.CreateAsync(Goods());
        var id = created.Id!.Value;
        var input = await Products.GetForEditAsync(id);
        var ean = input!.Ean;
        input.NetPrice = 20m;
        input.Kind = ProductKind.Service;
        input.AverageCost = 999m;

        var result = await Products.UpdateAsync(id, input);

        Assert.True(result.IsSuccess);
        await using var db = postgres.CreateAppDbContext();
        var product = await db.Products.SingleAsync(p => p.Id == id);
        Assert.Equal(ean, product.Ean);
        Assert.Equal(ProductKind.Goods, product.Kind);
        Assert.Equal(0m, product.AverageCost);
        Assert.Equal(20m, product.NetPrice);
    }

    [Fact]
    public async Task Deactivation_keeps_the_product_and_hides_it_from_the_default_list()
    {
        var input = Goods();
        var id = (await Products.CreateAsync(input)).Id!.Value;

        Assert.True((await Products.SetActiveAsync(id, false)).IsSuccess);

        Assert.Empty((await Products.SearchAsync(input.Name, includeInactive: false, 0, 10)).Items);
        var listed = Assert.Single((await Products.SearchAsync(input.Name, includeInactive: true, 0, 10)).Items);
        Assert.False(listed.IsActive);
    }

    [Fact]
    public async Task Search_treats_wildcards_literally()
    {
        var input = Goods();
        input.Name = Unique("100%");
        await Products.CreateAsync(input);
        await Products.CreateAsync(Goods());

        var match = await Products.SearchAsync(input.Name[..4], includeInactive: true, 0, 100);

        Assert.All(match.Items, item => Assert.Contains("100%", item.Name));
    }

    [Fact]
    public async Task Product_changes_are_written_to_the_audit_log_with_the_user()
    {
        var input = Goods();
        var id = (await Products.CreateAsync(input)).Id!.Value;
        input.NetPrice = 30m;
        input.Ean = null;

        await Products.UpdateAsync(id, input);
        await Products.UpdateAsync(id, input); // nothing changes, so no entry
        await Products.SetActiveAsync(id, false);
        await Products.SetActiveAsync(id, false); // already inactive, so no entry
        await Products.SetActiveAsync(id, true);

        var entries = await AuditFor(nameof(Product), id);
        Assert.Equal(
            [AuditActions.EntityCreated, AuditActions.EntityUpdated, AuditActions.EntityDeactivated, AuditActions.EntityActivated],
            entries.Select(e => e.Action));
        Assert.All(entries, e => Assert.Equal(_userId, e.UserId));
        Assert.Equal("changed=NetPrice", entries[1].Details);
    }

    [Fact]
    public async Task Category_name_is_unique_regardless_of_letter_case_and_changes_are_audited()
    {
        var name = Unique("Nabiał");
        var id = (await Categories.SaveAsync(null, name)).Id!.Value;

        var duplicate = await Categories.SaveAsync(null, name.ToUpperInvariant());
        await Categories.SaveAsync(id, name + "-zmiana");

        Assert.Equal("Kategoria o tej nazwie już istnieje.", duplicate.Error);
        Assert.Equal(
            [AuditActions.EntityCreated, AuditActions.EntityUpdated],
            (await AuditFor(nameof(Category), id)).Select(e => e.Action));
    }

    [Fact]
    public async Task Contractor_nip_is_optional_and_validated_when_given()
    {
        Assert.True((await Contractors.SaveAsync(null, new ContractorInput { Name = "Jan Kowalski" })).IsSuccess);
        Assert.False((await Contractors.SaveAsync(null, new ContractorInput { Name = "Firma", Nip = "1234563219" })).IsSuccess);

        var saved = await Contractors.SaveAsync(null, new ContractorInput { Name = "Firma", Nip = "123-456-32-18" });

        await using var db = postgres.CreateAppDbContext();
        Assert.Equal("1234563218", await db.Contractors.Where(c => c.Id == saved.Id).Select(c => c.Nip).SingleAsync());
    }

    [Fact]
    public async Task Contractor_deactivation_is_audited_and_keeps_the_record()
    {
        var name = Unique("Kontrahent");
        var id = (await Contractors.SaveAsync(null, new ContractorInput { Name = name })).Id!.Value;

        await Contractors.SaveAsync(id, new ContractorInput { Name = name, Phone = "500600700" });
        await Contractors.SetActiveAsync(id, false);

        Assert.Empty((await Contractors.SearchAsync(name, includeInactive: false, 0, 10)).Items);
        Assert.Single((await Contractors.SearchAsync(name, includeInactive: true, 0, 10)).Items);
        Assert.Equal(
            [AuditActions.EntityCreated, AuditActions.EntityUpdated, AuditActions.EntityDeactivated],
            (await AuditFor(nameof(Contractor), id)).Select(e => e.Action));
    }

    [Fact]
    public async Task Main_warehouse_is_seeded()
    {
        await using var db = postgres.CreateAppDbContext();

        var warehouse = await db.Warehouses.SingleAsync();

        Assert.Equal(Warehouse.MainWarehouseId, warehouse.Id);
    }
}
