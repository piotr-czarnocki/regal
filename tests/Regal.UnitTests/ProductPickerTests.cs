using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Features.Warehouse;

namespace Regal.UnitTests;

public sealed class ProductPickerTests : IAsyncLifetime
{
    private static readonly ProductPick Mleko = new(1, "MLEKO", "5901234123457", "Mleko", "szt", false, 3m);
    private static readonly ProductPick Maslo = new(2, "MASLO", "5901234123464", "Masło", "szt", false, 7m);

    private readonly BunitContext _context = new();
    private readonly List<ProductPick> _picked = [];
    private readonly List<string> _codes = [];
    private IReadOnlyList<ProductPick> _searchResults = [];

    public ProductPickerTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private IRenderedComponent<ProductPicker> Render() => _context.Render<ProductPicker>(parameters => parameters
        .Add(x => x.Search, (_, _) => Task.FromResult(_searchResults))
        .Add(x => x.FindByCode, (code, _) =>
        {
            _codes.Add(code);
            return Task.FromResult<ProductPick?>(code is "5901234123457" or "901234123457" ? Mleko : null);
        })
        .Add(x => x.Picked, EventCallback.Factory.Create<ProductPick>(this, _picked.Add)));

    private static void Type(IRenderedComponent<ProductPicker> picker, string text) => picker.Find("input").Input(text);

    private static void PressEnter(IRenderedComponent<ProductPicker> picker) =>
        picker.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

    [Fact]
    public void Enter_after_a_full_code_picks_the_product_and_clears_the_field()
    {
        var picker = Render();

        Type(picker, "5901234123457");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Mleko], _picked));
        Assert.Equal(string.Empty, picker.Find("input").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void Twelve_digits_from_a_scanner_are_looked_up_as_a_code()
    {
        var picker = Render();

        Type(picker, "901234123457");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Mleko], _picked));
        Assert.Equal(["901234123457"], _codes);
    }

    [Fact]
    public void Unknown_code_shows_a_message_and_picks_nothing()
    {
        var picker = Render();

        Type(picker, "5900000000000");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Contains("Nie znaleziono aktywnego towaru o kodzie 5900000000000", picker.Markup));
        Assert.Empty(_picked);
    }

    [Fact]
    public void Enter_on_an_empty_field_does_nothing()
    {
        var picker = Render();

        PressEnter(picker);

        Assert.Empty(_codes);
        Assert.Empty(_picked);
    }

    [Fact]
    public void Typing_shows_matches_and_clicking_one_picks_it()
    {
        _searchResults = [Mleko, Maslo];
        var picker = Render();

        Type(picker, "ma");

        picker.WaitForAssertion(() => Assert.Contains("Masło", picker.Markup));
        picker.FindAll(".mud-list-item").Single(item => item.TextContent.Contains("Masło")).Click();

        picker.WaitForAssertion(() => Assert.Equal([Maslo], _picked));
        Assert.DoesNotContain("Masło", picker.Markup);
    }

    [Fact]
    public void Enter_on_a_name_picks_the_only_match()
    {
        _searchResults = [Mleko];
        var picker = Render();

        Type(picker, "mle");
        picker.WaitForAssertion(() => Assert.Contains("Mleko", picker.Markup));
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Mleko], _picked));
    }

    [Fact]
    public void Enter_on_a_name_with_many_matches_waits_for_a_choice()
    {
        _searchResults = [Mleko, Maslo];
        var picker = Render();

        Type(picker, "m ");
        Type(picker, "ma");
        picker.WaitForAssertion(() => Assert.Contains("Masło", picker.Markup));
        PressEnter(picker);

        Assert.Empty(_picked);
    }
}
