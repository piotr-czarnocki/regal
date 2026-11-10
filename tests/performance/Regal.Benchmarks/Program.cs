using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Regal.Web.Data;
using Regal.Web.Features.Analytics;
using Regal.Web.Features.Auth;

// Usage, with REGAL_PERF_CONNECTION pointing at a database filled by the generator:
//   dotnet run -c Release --project tests/performance/Regal.Benchmarks            -> runs the benchmarks
//   dotnet run -c Release --project tests/performance/Regal.Benchmarks -- --sql   -> prints each query's SQL, for EXPLAIN
if (args.Contains("--sql"))
{
    await AnalyticsBenchmarks.PrintSqlAsync();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(AnalyticsBenchmarks).Assembly).Run(args);

/// <summary>The analysis queries over a year of generated trade, as the pages run them.</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 10)]
public class AnalyticsBenchmarks
{
    private sealed class AnyUser : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => Task.FromResult(1);
    }

    private sealed class Factory(DbContextOptions<RegalDbContext> options) : IDbContextFactory<RegalDbContext>
    {
        public RegalDbContext CreateDbContext() => new(options);
    }

    private AnalyticsService _analytics = null!;
    private AnalyticsFilter _year = null!;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("REGAL_PERF_CONNECTION")
        ?? throw new InvalidOperationException("Set REGAL_PERF_CONNECTION to the performance database.");

    private static AnalyticsService Create(ILoggerFactory? logging = null)
    {
        var options = new DbContextOptionsBuilder<RegalDbContext>().UseNpgsql(ConnectionString);
        if (logging is not null)
        {
            options.UseLoggerFactory(logging).EnableSensitiveDataLogging();
        }

        return new AnalyticsService(new Factory(options.Options), new AnyUser(), TimeProvider.System);
    }

    [GlobalSetup]
    public void Setup()
    {
        _analytics = Create();
        var today = _analytics.Today;
        _year = new AnalyticsFilter(today.AddDays(-365), today);
    }

    [Benchmark]
    public Task<SalesSummary> Summary() => _analytics.GetSummaryAsync(_year);

    [Benchmark]
    public Task<IReadOnlyList<SalesPoint>> SalesOverTime() => _analytics.GetSalesOverTimeAsync(_year);

    [Benchmark]
    public Task<IReadOnlyList<ProductSales>> SalesByProduct() => _analytics.GetSalesByProductAsync(_year, take: 50);

    [Benchmark]
    public Task<IReadOnlyList<CategorySales>> SalesByCategory() => _analytics.GetSalesByCategoryAsync(_year);

    [Benchmark]
    public Task<IReadOnlyList<AbcRow>> Abc() => _analytics.GetAbcAsync(_year);

    [Benchmark]
    public Task<IReadOnlyList<SlowMovingRow>> SlowMoving() => _analytics.GetSlowMovingAsync(90);

    [Benchmark]
    public Task<IReadOnlyList<ExpiringBatchRow>> ExpiringBatches() => _analytics.GetExpiringBatchesAsync(14);

    public static async Task PrintSqlAsync()
    {
        using var logging = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(o => o.SingleLine = false)
            .AddFilter((category, level) => category == DbLoggerCategory.Database.Command.Name && level == LogLevel.Information));
        var analytics = Create(logging);
        var today = analytics.Today;
        var year = new AnalyticsFilter(today.AddDays(-365), today);

        await analytics.GetSummaryAsync(year);
        await analytics.GetSalesByProductAsync(year, take: 50);
        await analytics.GetAbcAsync(year);
        await analytics.GetSlowMovingAsync(90);
        await analytics.GetExpiringBatchesAsync(14);
    }
}
