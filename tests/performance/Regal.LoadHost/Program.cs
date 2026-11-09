using System.Diagnostics;
using System.Globalization;
using System.Net;
using Regal.TestHost;

// Runs the real application for load tests: the test sign-in, a fake Fakturownia, and the
// database from REGAL_PERF_CONNECTION (the restricted application role).
//   dotnet run -c Release --project tests/performance/Regal.LoadHost -- [port]
//       keeps the application running for k6 (tests/performance/k6/pages.js); stop with Ctrl+C
//   dotnet run -c Release --project tests/performance/Regal.LoadHost -- --measure [users] [seconds]
//       measures the same pages itself: 20 users for 120 s by default, as the k6 script does
var connectionString = Environment.GetEnvironmentVariable("REGAL_PERF_CONNECTION")
    ?? throw new InvalidOperationException("Set REGAL_PERF_CONNECTION to the performance database.");
var measure = args.Contains("--measure");
var numbers = args.Where(a => a != "--measure").Select(a => int.Parse(a, CultureInfo.InvariantCulture)).ToArray();

await using var host = new RegalTestHost(connectionString).Start(measure ? 0 : numbers.FirstOrDefault(5190));
var userId = await host.CreateUserAsync();
var baseUrl = host.BaseAddress.ToString().TrimEnd('/');

if (measure)
{
    await LoadTest.RunAsync(baseUrl, userId, users: numbers.ElementAtOrDefault(0) is > 0 and var u ? u : 20, seconds: numbers.ElementAtOrDefault(1) is > 0 and var s ? s : 120);
    return;
}

Console.WriteLine($"BASE_URL={baseUrl}");
Console.WriteLine($"USER_ID={userId}");
Console.WriteLine("Ready.");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, stop.Token);
}
catch (OperationCanceledException)
{
    // Ctrl+C
}

/// <summary>The k6 scenario rewritten in C#: each user requests the pages in turn with a short pause.</summary>
internal static class LoadTest
{
    private static readonly string[] Pages =
    [
        "/", "/stock", "/stock-documents", "/invoices", "/products", "/analytics/sales", "/analytics/abc", "/analytics/slow-moving",
    ];

    public static async Task RunAsync(string baseUrl, int userId, int users, int seconds)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<(string Page, double Milliseconds, bool Ok)>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));

        // Warm-up: the first request of each page compiles queries and fills caches.
        await RequestAllAsync(baseUrl, userId, null, CancellationToken.None);

        Console.WriteLine($"{users} users for {seconds} s against {baseUrl}");
        await Task.WhenAll(Enumerable.Range(0, users).Select(_ => Task.Run(async () =>
        {
            while (!deadline.IsCancellationRequested)
            {
                await RequestAllAsync(baseUrl, userId, results, deadline.Token);
            }
        })));

        Console.WriteLine($"{"Page",-24}{"Requests",10}{"p50 ms",10}{"p95 ms",10}{"p99 ms",10}{"Errors",8}");
        foreach (var group in results.GroupBy(r => r.Page).OrderBy(g => Array.IndexOf(Pages, g.Key)).Append(results.GroupBy(_ => "(all)").Single()))
        {
            var times = group.Select(r => r.Milliseconds).Order().ToArray();
            Console.WriteLine($"{group.Key,-24}{times.Length,10}{Percentile(times, 50),10:0}{Percentile(times, 95),10:0}{Percentile(times, 99),10:0}{group.Count(r => !r.Ok),8}");
        }

        var all = results.Select(r => r.Milliseconds).Order().ToArray();
        var passed = Percentile(all, 95) < 500 && results.All(r => r.Ok);
        Console.WriteLine(passed ? "Criteria met: p95 < 500 ms, no errors." : "Criteria NOT met.");
    }

    private static async Task RequestAllAsync(
        string baseUrl,
        int userId,
        System.Collections.Concurrent.ConcurrentBag<(string, double, bool)>? results,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        handler.CookieContainer.Add(new Uri(baseUrl), new Cookie(TestAuthHandler.CookieName, userId.ToString(CultureInfo.InvariantCulture)));
        using var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };

        foreach (var page in Pages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var watch = Stopwatch.StartNew();
            var ok = false;
            try
            {
                using var response = await client.GetAsync(page, CancellationToken.None);
                var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
                ok = response.StatusCode == HttpStatusCode.OK && body.Contains("Wyloguj", StringComparison.Ordinal);
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            results?.Add((page, watch.Elapsed.TotalMilliseconds, ok));
            try
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private static double Percentile(double[] sorted, int percentile) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1, 0, sorted.Length - 1)];
}
