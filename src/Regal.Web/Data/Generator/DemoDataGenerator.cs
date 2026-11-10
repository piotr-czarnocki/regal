using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.Web.Data.Generator;

public sealed class GeneratorOptions
{
    public int Products { get; set; } = 200;

    public int Customers { get; set; } = 60;

    public int Suppliers { get; set; } = 8;

    public int Days { get; set; } = 365;

    public double InvoicesPerDay { get; set; } = 8;

    public int Seed { get; set; } = 1;

    /// <summary>Starts every generated SKU; a second run needs a different one.</summary>
    public string SkuPrefix { get; set; } = "DEMO";

    /// <summary>
    /// Expired batches read and written off at a time. Tests lower it to reach the
    /// "more than one page a week" case without a catalog of thousands of products.
    /// </summary>
    public int WriteOffPageSize { get; set; } = StockDraftValidator.MaxLines;
}

public sealed record GeneratorReport(int Products, int Contractors, int StockDocuments, int Invoices, int CancelledInvoices, long Movements);

/// <summary>
/// Creates about a year of made-up trade through the real services, with a clock moved day by day,
/// so the data obeys every rule the application does. Only for development and test databases.
/// Generated invoices are marked as sent with a DEMO number and are never sent to Fakturownia.
/// </summary>
public sealed class DemoDataGenerator(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    GeneratorOptions options,
    TimeProvider timeProvider,
    Action<string>? log = null)
{
    public const string UserEmail = "dane-testowe@regal.invalid";

    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    private static readonly (string Name, string Unit, string Vat, bool HasExpiry, bool Produced, string[] Bases, string[] Variants)[] Catalog =
    [
        ("Nabiał", "szt", "5", true, false, ["Mleko", "Jogurt", "Kefir", "Twaróg", "Masło", "Śmietana", "Ser żółty"], ["2%", "3,2%", "naturalny", "owocowy", "bez laktozy", "ekologiczny"]),
        ("Pieczywo", "szt", "5", true, true, ["Chleb", "Bułka", "Bagietka", "Rogal", "Chałka"], ["pszenny", "żytni", "razowy", "z ziarnami", "maślany"]),
        ("Napoje", "szt", "23", true, false, ["Sok", "Woda", "Napój", "Lemoniada", "Herbata mrożona"], ["jabłkowy", "pomarańczowy", "gazowana", "niegazowana", "cytrynowa"]),
        ("Słodycze", "szt", "5", true, false, ["Czekolada", "Baton", "Herbatniki", "Wafelki", "Cukierki"], ["mleczna", "gorzka", "orzechowe", "kakaowe", "owocowe"]),
        ("Przyprawy", "szt", "5", false, false, ["Pieprz", "Papryka", "Oregano", "Bazylia", "Curry"], ["mielony", "słodka", "suszone", "w słoiku", "ostra"]),
        ("Konserwy", "szt", "5", true, false, ["Groszek", "Fasola", "Kukurydza", "Pomidory", "Tuńczyk"], ["konserwowy", "czerwona", "słodka", "krojone", "w oleju"]),
        ("Mrożonki", "kg", "5", true, false, ["Warzywa", "Pierogi", "Frytki", "Truskawki", "Szpinak"], ["na patelnię", "ruskie", "karbowane", "mrożone", "rozdrobniony"]),
        ("Chemia", "szt", "23", false, false, ["Płyn do naczyń", "Proszek", "Mydło", "Szampon", "Płyn do szyb"], ["cytrynowy", "do kolorów", "w płynie", "rumiankowy", "z alkoholem"]),
        ("Opakowania", "szt", "23", false, false, ["Torba", "Pudełko", "Folia", "Tacka", "Kubek"], ["papierowa", "kartonowe", "spożywcza", "styropianowa", "jednorazowy"]),
        ("Usługi", "h", "23", false, false, ["Dostawa", "Montaż regału", "Doradztwo", "Szkolenie"], ["lokalna", "standard", "godzinowe", "dla personelu"]),
    ];

    private sealed class SimulatedClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class GeneratorUser(int id) : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => Task.FromResult(id);
    }

    /// <summary>Not configured, so nothing can ever be sent from here.</summary>
    private sealed class NoFakturownia : IFakturowniaClient
    {
        public bool IsConfigured => false;

        public Task<RemoteInvoice?> FindByOidAsync(string oid, CancellationToken cancellationToken) =>
            throw new FakturowniaUnavailableException("Generator nie łączy się z Fakturownią.");

        public Task<RemoteInvoice> CreateAsync(string requestJson, CancellationToken cancellationToken) =>
            throw new FakturowniaUnavailableException("Generator nie łączy się z Fakturownią.");

        public Task<RemotePayment> GetPaymentAsync(long fakturowniaId, CancellationToken cancellationToken) =>
            throw new FakturowniaUnavailableException("Generator nie łączy się z Fakturownią.");

        public Task SendByEmailAsync(long fakturowniaId, string email, CancellationToken cancellationToken) =>
            throw new FakturowniaUnavailableException("Generator nie łączy się z Fakturownią.");
    }

    private sealed record GeneratedProduct(int Id, string Unit, bool IsService, bool HasExpiry, bool Produced, decimal Cost, double Popularity, int SupplierIndex);

    private readonly Random _random = new(options.Seed);
    private readonly Dictionary<int, decimal> _stock = [];
    private SimulatedClock _clock = null!;
    private int _demoInvoiceNumber;

    public static void EnsureAllowed(string environmentName)
    {
        if (environmentName is not ("Development" or "Testing"))
        {
            throw new InvalidOperationException(
                $"Generator danych działa tylko w środowisku Development albo Testing, a bieżące to {environmentName}.");
        }
    }

    public async Task<GeneratorReport> RunAsync(CancellationToken cancellationToken = default)
    {
        // The simulated year ends today, at 7:00 UTC each day, inside Polish working hours.
        var start = new DateTimeOffset(timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(-options.Days), TimeSpan.Zero).AddHours(7);
        _clock = new SimulatedClock(start);

        var userId = await EnsureUserAsync(cancellationToken);
        var user = new GeneratorUser(userId);
        var products = new ProductService(dbContextFactory, user, _clock);
        var categories = new CategoryService(dbContextFactory, user, _clock);
        var contractors = new ContractorService(dbContextFactory, user, _clock);
        var documents = new StockDocumentService(dbContextFactory, user, _clock);
        var invoices = new InvoiceService(dbContextFactory, user, documents, new NoFakturownia(), _clock);

        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var firstSku = $"{options.SkuPrefix}-00001";
            if (await db.Products.AnyAsync(p => p.Sku == firstSku, cancellationToken))
            {
                throw new InvalidOperationException($"Dane z prefiksem {options.SkuPrefix} już istnieją. Użyj innego prefiksu albo pustej bazy.");
            }
        }

        var catalog = await CreateProductsAsync(products, categories, cancellationToken);
        var suppliers = await CreateContractorsAsync(contractors, options.Suppliers, isSupplier: true, cancellationToken);
        var customers = await CreateContractorsAsync(contractors, options.Customers, isSupplier: false, cancellationToken);
        log?.Invoke($"Kartoteki: {catalog.Count} produktów, {suppliers.Count + customers.Count} kontrahentów.");

        var stockDocuments = 0;
        var issued = 0;
        var cancelled = 0;
        for (var day = 0; day < options.Days; day++)
        {
            _clock.Now = start.AddDays(day);
            stockDocuments += await RestockAsync(documents, catalog, suppliers, cancellationToken);
            stockDocuments += await WriteOffExpiredAsync(documents, dbContextFactory, cancellationToken);

            var weekend = _clock.Now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var count = Poisson(options.InvoicesPerDay * (weekend ? 0.3 : 1));
            for (var i = 0; i < count; i++)
            {
                _clock.Now = _clock.Now.AddMinutes(_random.Next(5, 40));
                var outcome = await SellAsync(invoices, catalog, customers, userId, cancellationToken);
                issued += outcome.Issued ? 1 : 0;
                cancelled += outcome.Cancelled ? 1 : 0;
            }

            if ((day + 1) % 30 == 0)
            {
                log?.Invoke($"Dzień {day + 1} z {options.Days}: {issued} faktur, {stockDocuments} dokumentów przyjęcia i rozchodu.");
            }
        }

        await using var summary = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var movements = await summary.StockMovements.LongCountAsync(cancellationToken);
        return new GeneratorReport(catalog.Count, suppliers.Count + customers.Count, stockDocuments, issued, cancelled, movements);
    }

    private async Task<int> EnsureUserAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Users.Where(u => u.Email == UserEmail).Select(u => (int?)u.Id).SingleOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            return id;
        }

        // Inactive: it owns the generated records but can never sign in.
        var user = new User { Email = UserEmail, Role = Roles.Administrator, IsActive = false };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    private async Task<List<GeneratedProduct>> CreateProductsAsync(ProductService products, CategoryService categories, CancellationToken cancellationToken)
    {
        var categoryIds = new Dictionary<string, int>();
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            foreach (var entry in Catalog)
            {
                var id = await db.Categories.Where(c => c.Name == entry.Name).Select(c => (int?)c.Id).SingleOrDefaultAsync(cancellationToken)
                    ?? (await categories.SaveAsync(null, entry.Name, cancellationToken)).Id!.Value;
                categoryIds[entry.Name] = id;
            }
        }

        var names = new HashSet<string>();
        var result = new List<GeneratedProduct>(options.Products);
        for (var i = 1; i <= options.Products; i++)
        {
            // Services are rare, as in a shop: about one product in twenty.
            var entry = _random.NextDouble() < 0.05 ? Catalog[^1] : Catalog[_random.Next(Catalog.Length - 1)];
            var isService = entry.Name == "Usługi";
            var name = $"{entry.Bases[_random.Next(entry.Bases.Length)]} {entry.Variants[_random.Next(entry.Variants.Length)]}";
            var unique = name;
            for (var n = 2; !names.Add(unique); n++)
            {
                unique = $"{name} {n}";
            }

            var price = decimal.Round((decimal)(2 + (Math.Pow(_random.NextDouble(), 2) * 120)), 2);
            var created = await products.CreateAsync(new ProductInput
            {
                Sku = $"{options.SkuPrefix}-{i:00000}",
                Name = unique,
                CategoryId = categoryIds[entry.Name],
                Unit = entry.Unit,
                Kind = isService ? ProductKind.Service : ProductKind.Goods,
                NetPrice = isService ? price * 5 : price,
                VatRate = entry.Vat,
                HasExpiry = entry.HasExpiry,
            }, cancellationToken);

            // About one product in seven never sells, so slow-moving goods appear.
            var popularity = _random.NextDouble() < 0.15 ? 0 : 1 / Math.Pow(1 + _random.Next(options.Products), 0.9);
            var cost = decimal.Round(price * (decimal)(0.55 + (_random.NextDouble() * 0.25)), 4);
            result.Add(new GeneratedProduct(created.Id!.Value, entry.Unit, isService, entry.HasExpiry, entry.Produced, cost, popularity, i % Math.Max(options.Suppliers, 1)));
        }

        return result;
    }

    private async Task<List<int>> CreateContractorsAsync(ContractorService contractors, int count, bool isSupplier, CancellationToken cancellationToken)
    {
        string[] companies = ["Hurtownia", "Spółdzielnia", "Delikatesy", "Sklep", "Market", "Piekarnia", "Bar", "Hotel"];
        string[] names = ["Pod Lipą", "Smakosz", "Zielony Koszyk", "Jutrzenka", "Pod Dębem", "Mazowsze", "Kłos", "Ania", "Bratek", "Sokół"];
        string[] people = ["Anna Nowak", "Jan Kowalski", "Maria Wiśniewska", "Piotr Wójcik", "Katarzyna Kamińska", "Tomasz Lewandowski"];
        string[] cities = ["Warszawa", "Kraków", "Łódź", "Radom", "Płock", "Siedlce"];

        var ids = new List<int>(count);
        for (var i = 1; i <= count; i++)
        {
            var person = !isSupplier && _random.NextDouble() < 0.3;
            var name = person
                ? $"{people[_random.Next(people.Length)]} ({options.SkuPrefix} {i})"
                : $"{(isSupplier ? "Hurtownia" : companies[_random.Next(companies.Length)])} {names[_random.Next(names.Length)]} ({options.SkuPrefix} {i})";
            var created = await contractors.SaveAsync(null, new ContractorInput
            {
                Name = name,
                Nip = person ? null : RandomNip(),
                Address = $"ul. Polna {_random.Next(1, 120)}, {_random.Next(10, 99)}-{_random.Next(100, 999)} {cities[_random.Next(cities.Length)]}",
            }, cancellationToken);
            ids.Add(created.Id!.Value);
        }

        return ids;
    }

    private string RandomNip()
    {
        int[] weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        while (true)
        {
            var digits = Enumerable.Range(0, 9).Select(i => i == 0 ? _random.Next(1, 10) : _random.Next(10)).ToArray();
            var check = digits.Select((d, i) => d * weights[i]).Sum() % 11;
            if (check < 10)
            {
                return string.Concat(digits) + check.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Orders goods whose estimated stock falls below a week of sales; bread comes from own production.</summary>
    private async Task<int> RestockAsync(StockDocumentService documents, List<GeneratedProduct> catalog, List<int> suppliers, CancellationToken cancellationToken)
    {
        var due = catalog
            .Where(p => !p.IsService && _stock.GetValueOrDefault(p.Id) < Reorder(p))
            .ToList();

        var posted = 0;
        foreach (var group in due.GroupBy(p => p.Produced ? -1 : p.SupplierIndex))
        {
            foreach (var chunk in group.Chunk(StockDraftValidator.MaxLines))
            {
                var draft = new StockDocumentDraft
                {
                    RequestId = Guid.NewGuid(),
                    Type = group.Key == -1 ? StockDocumentType.PW : StockDocumentType.PZ,
                    ContractorId = group.Key == -1 ? null : suppliers[group.Key % suppliers.Count],
                    Lines = chunk.Select(p => new StockDocumentLine
                    {
                        ProductId = p.Id,
                        Quantity = Quantity(p, Reorder(p) * 3),
                        UnitCost = decimal.Round(p.Cost * (decimal)(0.9 + (_random.NextDouble() * 0.2)), 4),
                        ExpiryDate = p.HasExpiry ? WarehouseTime.DateOf(_clock.Now).AddDays(_random.Next(20, 120)) : null,
                    }).ToList(),
                };

                if ((await documents.PostAsync(draft, cancellationToken)).IsSuccess)
                {
                    posted++;
                    foreach (var line in draft.Lines)
                    {
                        _stock[line.ProductId] = _stock.GetValueOrDefault(line.ProductId) + line.Quantity;
                    }
                }

                _clock.Now = _clock.Now.AddMinutes(1);
            }
        }

        return posted;
    }

    /// <summary>Once a week, expired goods are written off with RW.</summary>
    private async Task<int> WriteOffExpiredAsync(StockDocumentService documents, IDbContextFactory<RegalDbContext> factory, CancellationToken cancellationToken)
    {
        if (_clock.Now.DayOfWeek != DayOfWeek.Monday)
        {
            return 0;
        }

        var today = WarehouseTime.DateOf(_clock.Now);
        var prefix = options.SkuPrefix + "-";
        var posted = 0;

        // Page by page until nothing expired is left. A product may appear on a document once,
        // so a product's second expired batch waits for the next document.
        while (true)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var expired = await db.Stock.AsNoTracking()
                .Where(s => s.Quantity > 0 && s.Batch!.ExpiryDate < today && s.Batch.Product!.Sku.StartsWith(prefix))
                .OrderBy(s => s.BatchId)
                .Select(s => new { s.BatchId, s.Batch!.ProductId, s.Quantity })
                .Take(options.WriteOffPageSize)
                .ToListAsync(cancellationToken);

            var progress = false;
            var pending = expired;
            while (pending.Count > 0)
            {
                var lines = pending.GroupBy(b => b.ProductId).Select(g => g.First()).Take(StockDraftValidator.MaxLines).ToList();
                pending = pending.Except(lines).ToList();

                var draft = new StockDocumentDraft
                {
                    RequestId = Guid.NewGuid(),
                    Type = StockDocumentType.RW,
                    Notes = "Przeterminowany towar",
                    Lines = lines.Select(b => new StockDocumentLine { ProductId = b.ProductId, Quantity = b.Quantity, BatchId = b.BatchId }).ToList(),
                };

                if ((await documents.PostAsync(draft, cancellationToken)).IsSuccess)
                {
                    posted++;
                    progress = true;
                    foreach (var batch in lines)
                    {
                        _stock[batch.ProductId] = Math.Max(0, _stock.GetValueOrDefault(batch.ProductId) - batch.Quantity);
                    }
                }

                _clock.Now = _clock.Now.AddMinutes(1);
            }

            // A refused document would be read again forever; leave it for next week.
            if (!progress || expired.Count < options.WriteOffPageSize)
            {
                return posted;
            }
        }
    }

    private async Task<(bool Issued, bool Cancelled)> SellAsync(
        InvoiceService invoices, List<GeneratedProduct> catalog, List<int> customers, int userId, CancellationToken cancellationToken)
    {
        var lineCount = 1 + Math.Min(5, Poisson(1.5));
        var chosen = new List<GeneratedProduct>();
        for (var attempt = 0; attempt < lineCount * 3 && chosen.Count < lineCount; attempt++)
        {
            var product = PickByPopularity(catalog);
            if (product is not null && !chosen.Contains(product) && (product.IsService || _stock.GetValueOrDefault(product.Id) >= 1))
            {
                chosen.Add(product);
            }
        }

        if (chosen.Count == 0)
        {
            return (false, false);
        }

        var draft = new InvoiceDraft
        {
            RequestId = Guid.NewGuid(),
            ContractorId = customers[_random.Next(customers.Count)],
            PaymentMethod = InvoicePaymentMethods.All[_random.Next(InvoicePaymentMethods.All.Count)],
            Lines = chosen.Select(p => new InvoiceDraftLine
            {
                ProductId = p.Id,
                Quantity = p.IsService ? _random.Next(1, 4) : Math.Min(Quantity(p, _random.Next(1, 10)), Whole(p, _stock.GetValueOrDefault(p.Id))),
                DiscountPercent = _random.NextDouble() < 0.15 ? _random.Next(1, 4) * 5 : 0,
            }).Where(l => l.Quantity > 0).ToList(),
        };

        var result = await invoices.IssueAsync(draft, cancellationToken);
        if (!result.IsSuccess)
        {
            return (false, false); // usually stock that expired after the estimate
        }

        foreach (var line in draft.Lines)
        {
            _stock[line.ProductId] = Math.Max(0, _stock.GetValueOrDefault(line.ProductId) - line.Quantity);
        }

        if (_random.NextDouble() < 0.01)
        {
            var cancelledByUser = await CancelAsync(result.Id!.Value, userId, cancellationToken);
            return (true, cancelledByUser);
        }

        await MarkAsDemoSentAsync(result.Id!.Value, cancellationToken);
        return (true, false);
    }

    /// <summary>The usual cancel checks Fakturownia, which the generator never contacts; here the invoice is known to exist nowhere.</summary>
    private async Task<bool> CancelAsync(int invoiceId, int userId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId, cancellationToken);
        invoice.Status = InvoiceStatus.Cancelled;
        await db.OutboxMessages.Where(m => m.InvoiceId == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, (DateTime?)null), cancellationToken);
        Features.Audit.AuditTrail.AddInvoice(db, Features.Audit.AuditActions.InvoiceCancelled, invoiceId, userId, "generator", _clock);
        await db.SaveChangesAsync(cancellationToken);

        if (invoice.StockDocumentId is { } wz)
        {
            var documents = new StockDocumentService(dbContextFactory, new GeneratorUser(userId), _clock);
            if (!(await documents.ReverseInTransactionAsync(db, wz, Guid.NewGuid(), cancellationToken)).IsSuccess)
            {
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task MarkAsDemoSentAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var year = WarehouseTime.DateOf(_clock.Now).Year;
        var number = string.Create(Polish, $"DEMO {++_demoInvoiceNumber}/{year}");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Invoices.Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Sent).SetProperty(i => i.Number, number), cancellationToken);
        await db.OutboxMessages.Where(m => m.InvoiceId == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, (DateTime?)null), cancellationToken);
    }

    private GeneratedProduct? PickByPopularity(List<GeneratedProduct> catalog)
    {
        var total = catalog.Sum(p => p.Popularity);
        var target = _random.NextDouble() * total;
        foreach (var product in catalog)
        {
            target -= product.Popularity;
            if (target <= 0 && product.Popularity > 0)
            {
                return product;
            }
        }

        return null;
    }

    private decimal Reorder(GeneratedProduct product) =>
        product.Popularity == 0 ? 2 : Math.Max(5, decimal.Round((decimal)(product.Popularity * options.InvoicesPerDay * 7 * 6), 0));

    private decimal Quantity(GeneratedProduct product, decimal value) =>
        product.Unit == "szt" || product.Unit == "h" ? Math.Max(1, decimal.Round(value, 0)) : Math.Max(0.1m, decimal.Round(value * (decimal)(0.5 + _random.NextDouble()), 1));

    private static decimal Whole(GeneratedProduct product, decimal value) =>
        product.Unit == "szt" ? decimal.Floor(value) : decimal.Round(value, 3);

    private int Poisson(double mean)
    {
        var limit = Math.Exp(-mean);
        var k = 0;
        var p = _random.NextDouble();
        while (p > limit)
        {
            k++;
            p *= _random.NextDouble();
        }

        return k;
    }
}
