using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;

namespace Regal.Web.Features.Products;

public sealed class ProductInput
{
    public string? Sku { get; set; }
    public string? Ean { get; set; }
    public string? Name { get; set; }
    public int? CategoryId { get; set; }
    public string? Unit { get; set; } = "szt";
    public ProductKind Kind { get; set; } = ProductKind.Goods;
    public decimal NetPrice { get; set; }
    public string VatRate { get; set; } = VatRates.Default;
    public bool HasExpiry { get; set; }

    /// <summary>For display only; saving ignores it.</summary>
    public decimal AverageCost { get; set; }
}

public sealed record ProductListItem(
    int Id,
    string Sku,
    string? Ean,
    string Name,
    string CategoryName,
    string Unit,
    ProductKind Kind,
    decimal NetPrice,
    string VatRate,
    bool IsActive);

public sealed class ProductService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private const string UniqueSkuIndex = "IX_Products_Sku";
    private const string UniqueEanIndex = "IX_Products_Ean";

    public async Task<PagedResult<ProductListItem>> SearchAsync(
        string? term, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Products.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (SearchPattern.Contains(term) is { } pattern)
        {
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(p.Sku, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(p.Ean!, pattern, SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(p => new ProductListItem(
                p.Id, p.Sku, p.Ean, p.Name, p.Category!.Name, p.Unit, p.Kind, p.NetPrice, p.VatRate, p.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductListItem>(items, total);
    }

    public async Task<ProductInput?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Products.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new ProductInput
            {
                Sku = p.Sku,
                Ean = p.Ean,
                Name = p.Name,
                CategoryId = p.CategoryId,
                Unit = p.Unit,
                Kind = p.Kind,
                NetPrice = p.NetPrice,
                VatRate = p.VatRate,
                HasExpiry = p.HasExpiry,
                AverageCost = p.AverageCost,
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SaveResult> CreateAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);

        if (Validate(input) is { } error)
        {
            return SaveResult.Fail(error);
        }

        var ean = NormalizeEan(input.Ean);
        if (ValidateEan(input.Kind, ean, currentEan: null) is { } eanError)
        {
            return SaveResult.Fail(eanError);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId, cancellationToken))
        {
            return SaveResult.Fail("Wybrana kategoria nie istnieje.");
        }

        if (input.Kind == ProductKind.Goods && ean is null)
        {
            ean = await NextInternalEanAsync(db, cancellationToken);
        }

        var product = new Product
        {
            Sku = NormalizeSku(input.Sku),
            Ean = ean,
            Name = input.Name!.Trim(),
            CategoryId = input.CategoryId!.Value,
            Unit = input.Unit!.Trim(),
            Kind = input.Kind,
            NetPrice = input.NetPrice,
            VatRate = input.VatRate,
            HasExpiry = input.Kind == ProductKind.Goods && input.HasExpiry,
        };

        try
        {
            var id = await AuditTrail.InsertAsync(db, product, p => p.Id, userId, timeProvider, cancellationToken);
            return SaveResult.Ok(id);
        }
        catch (DbUpdateException exception) when (DuplicateMessage(exception) is not null)
        {
            return SaveResult.Fail(DuplicateMessage(exception)!);
        }
    }

    /// <summary>The kind is fixed at creation; AverageCost and activity are not changed here.</summary>
    public async Task<SaveResult> UpdateAsync(int id, ProductInput input, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return SaveResult.Fail("Nie znaleziono produktu.");
        }

        input.Kind = product.Kind;
        if (Validate(input) is { } error)
        {
            return SaveResult.Fail(error);
        }

        var ean = NormalizeEan(input.Ean);
        if (ValidateEan(product.Kind, ean, product.Ean) is { } eanError)
        {
            return SaveResult.Fail(eanError);
        }

        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId, cancellationToken))
        {
            return SaveResult.Fail("Wybrana kategoria nie istnieje.");
        }

        product.Sku = NormalizeSku(input.Sku);
        // A cleared field keeps the current code: goods must always have one.
        product.Ean = ean ?? product.Ean;
        product.Name = input.Name!.Trim();
        product.CategoryId = input.CategoryId!.Value;
        product.Unit = input.Unit!.Trim();
        product.NetPrice = input.NetPrice;
        product.VatRate = input.VatRate;
        product.HasExpiry = product.Kind == ProductKind.Goods && input.HasExpiry;

        try
        {
            if (AuditTrail.AddUpdate(db, product, product.Id, userId, timeProvider))
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return SaveResult.Ok(product.Id);
        }
        catch (DbUpdateException exception) when (DuplicateMessage(exception) is not null)
        {
            return SaveResult.Fail(DuplicateMessage(exception)!);
        }
    }

    public async Task<SaveResult> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return SaveResult.Fail("Nie znaleziono produktu.");
        }

        if (product.IsActive != isActive)
        {
            product.IsActive = isActive;
            AuditTrail.AddActivation<Product>(db, isActive, product.Id, userId, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
        }

        return SaveResult.Ok(product.Id);
    }

    private static string NormalizeSku(string? sku) => sku!.Trim().ToUpperInvariant();

    private static string? NormalizeEan(string? ean) => string.IsNullOrWhiteSpace(ean) ? null : ean.Trim();

    private static string? Validate(ProductInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Sku))
        {
            return "Podaj SKU.";
        }

        if (input.Sku.Trim().Length > 50)
        {
            return "SKU może mieć najwyżej 50 znaków.";
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return "Podaj nazwę.";
        }

        if (input.Name.Trim().Length > 200)
        {
            return "Nazwa może mieć najwyżej 200 znaków.";
        }

        if (input.CategoryId is null)
        {
            return "Wybierz kategorię.";
        }

        if (string.IsNullOrWhiteSpace(input.Unit))
        {
            return "Podaj jednostkę.";
        }

        if (input.Unit.Trim().Length > 20)
        {
            return "Jednostka może mieć najwyżej 20 znaków.";
        }

        if (input.NetPrice < 0 || input.NetPrice >= 10_000_000_000_000_000m || decimal.Round(input.NetPrice, 2) != input.NetPrice)
        {
            return "Cena netto musi być nieujemna i mieć najwyżej dwa miejsca po przecinku.";
        }

        if (!VatRates.All.Contains(input.VatRate))
        {
            return "Wybierz stawkę VAT z listy.";
        }

        return null;
    }

    private static string? ValidateEan(ProductKind kind, string? ean, string? currentEan)
    {
        if (ean is null || ean == currentEan)
        {
            return null;
        }

        if (kind == ProductKind.Service)
        {
            return "Usługa nie może mieć kodu EAN.";
        }

        if (!Ean13.IsValid(ean))
        {
            return "Kod EAN musi mieć 13 cyfr i poprawną cyfrę kontrolną.";
        }

        return Ean13.IsInReservedInternalRange(ean)
            ? "Kody EAN zaczynające się od 04 są zarezerwowane dla kodów wewnętrznych. Zostaw pole puste, aby wygenerować kod."
            : null;
    }

    private static async Task<string> NextInternalEanAsync(RegalDbContext db, CancellationToken cancellationToken)
    {
        var number = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('\"{RegalDbContext.InternalEanSequence}\"') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return Ean13.CreateInternal(number);
    }

    private static string? DuplicateMessage(DbUpdateException exception) => DbErrors.UniqueViolation(exception) switch
    {
        UniqueSkuIndex => "Produkt o tym SKU już istnieje.",
        UniqueEanIndex => "Produkt o tym kodzie EAN już istnieje.",
        _ => null,
    };
}
