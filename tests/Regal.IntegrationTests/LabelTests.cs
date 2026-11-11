using System.Text;
using System.Text.RegularExpressions;
using Regal.Web.Features.Products;

namespace Regal.IntegrationTests;

/// <summary>Label sheets in PDF (SPEC 5.18).</summary>
[Collection(PostgresCollection.Name)]
public class LabelTests(PostgresFixture postgres) : WarehouseTestBase(postgres)
{
    private LabelService Labels => new(Postgres.DbContextFactory, new FixedUser(UserId));

    private static int PageCount(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;

    [Fact]
    public async Task Labels_fill_a4_sheets_of_24_including_polish_names()
    {
        var bread = await Products.CreateAsync(new ProductInput
        {
            Sku = Unique("CHL"), Name = Unique("Chleb żytni ŻÓŁĆ"), CategoryId = CategoryId, Unit = "szt", NetPrice = 4.50m,
        });
        var other = await NewGoodsAsync();

        var result = await Labels.BuildAsync([new LabelRequest(bread.Id!.Value, 20), new LabelRequest(other, 5)]);

        Assert.Null(result.Error);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(result.Pdf!, 0, 4));
        Assert.Equal(2, PageCount(result.Pdf!)); // 25 labels, 24 per sheet
    }

    [Theory]
    [InlineData(0, "od 1 do 100")]
    [InlineData(101, "od 1 do 100")]
    public async Task Copies_are_limited(int copies, string message)
    {
        var product = await NewGoodsAsync();

        var result = await Labels.BuildAsync([new LabelRequest(product, copies)]);

        Assert.Contains(message, result.Error);
        Assert.Null(result.Pdf);
    }

    [Fact]
    public async Task Nothing_selected_and_too_many_labels_are_refused()
    {
        var products = new List<LabelRequest>();
        for (var i = 0; i < 5; i++)
        {
            products.Add(new LabelRequest(await NewGoodsAsync(), 100));
        }

        Assert.Contains("Zaznacz produkty", (await Labels.BuildAsync([])).Error);
        Assert.Contains("najwyżej 480", (await Labels.BuildAsync(products)).Error);
    }

    [Fact]
    public async Task An_unknown_product_is_refused() =>
        Assert.Contains("Nie znaleziono", (await Labels.BuildAsync([new LabelRequest(int.MaxValue, 1)])).Error);
}
