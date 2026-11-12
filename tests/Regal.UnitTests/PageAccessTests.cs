using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Regal.Web.Features.Auth;

namespace Regal.UnitTests;

/// <summary>
/// Every page is either for the administrator only or named here as open to the warehouse role
/// (SPEC 5.19). A new page that forgets the attribute fails this test instead of letting a warehouse
/// worker reach it and meet an error from the service.
/// </summary>
public sealed class PageAccessTests
{
    private static readonly HashSet<string> WarehousePages =
    [
        "Home",
        "NewStockDocumentPage",
        "ProductHistoryPage",
        "StockDocumentDetailsPage",
        "StockDocumentsPage",
        "StockPage",
        "StocktakeDetailsPage",
        "StocktakesPage",
    ];

    private static readonly HashSet<string> PublicPages = ["Error", "NotFound", "AccessDenied", "Login"];

    private static IEnumerable<Type> Pages() =>
        typeof(Roles).Assembly.GetTypes().Where(t => t.GetCustomAttributes<RouteAttribute>().Any());

    [Fact]
    public void Every_page_is_for_the_administrator_or_listed_as_open_to_the_warehouse()
    {
        var unexpected = Pages()
            .Where(page => !PublicPages.Contains(page.Name) && !WarehousePages.Contains(page.Name))
            .Where(page => !page.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Roles == Roles.Administrator))
            .Select(page => page.Name)
            .ToList();

        Assert.Empty(unexpected);
    }

    [Fact]
    public void The_lists_name_only_pages_that_exist()
    {
        var names = Pages().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain(WarehousePages.Concat(PublicPages), name => !names.Contains(name));
    }

    [Fact]
    public void Analysis_pages_including_the_new_cards_are_for_the_administrator()
    {
        var analysis = Pages().Where(p => p.Namespace == "Regal.Web.Features.Analytics").ToList();

        Assert.Contains(analysis, p => p.Name == "ProductCardPage");
        Assert.Contains(analysis, p => p.Name == "ContractorCardPage");
        Assert.All(analysis, p => Assert.Contains(p.GetCustomAttributes<AuthorizeAttribute>(), a => a.Roles == Roles.Administrator));
    }
}
