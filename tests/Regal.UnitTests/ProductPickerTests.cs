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
    private Func<string, Task<ProductPick?>>? _lookup;

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
            return _lookup?.Invoke(code) ?? Task.FromResult<ProductPick?>(code is "5901234123457" or "901234123457" ? Mleko : null);
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

        picker.WaitForAssertion(() => Assert.Contains("Nie znaleziono produktu o kodzie 5900000000000", picker.Markup));
        Assert.Empty(_picked);
    }

    private int JsCalls(string name) =>
        _context.JSInterop.Invocations.Count(call => call.Identifier.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string FieldText(IRenderedComponent<ProductPicker> picker) =>
        picker.Find("input").GetAttribute("value") ?? string.Empty;

    /// <summary>A scanner starts its next code only once the field is free for it.</summary>
    private static void PressEnterAndWaitForEmptyField(IRenderedComponent<ProductPicker> picker)
    {
        PressEnter(picker);
        picker.WaitForAssertion(() => Assert.Equal(string.Empty, FieldText(picker)));
    }

    [Fact]
    public void Enter_on_a_one_character_code_finds_its_exact_match()
    {
        _lookup = code => Task.FromResult<ProductPick?>(code == "1" ? Mleko : null);
        var picker = Render();

        Type(picker, "1");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Mleko], _picked));
    }

    [Fact]
    public void Enter_pressed_right_after_typing_does_not_depend_on_the_search_delay()
    {
        _lookup = code => Task.FromResult<ProductPick?>(code == "10" ? Maslo : null);
        var picker = Render();

        Type(picker, "10");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Maslo], _picked));
    }

    [Fact]
    public void Text_is_trimmed_before_the_lookup()
    {
        _lookup = code => Task.FromResult<ProductPick?>(code == "abc" ? Mleko : null);
        var picker = Render();

        Type(picker, "  abc ");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Mleko], _picked));
        Assert.Equal(["abc"], _codes);
    }

    [Fact]
    public void Exact_match_wins_over_matches_by_fragment()
    {
        _lookup = code => Task.FromResult<ProductPick?>(Maslo);
        _searchResults = [Mleko, Maslo];
        var picker = Render();

        Type(picker, "1");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Maslo], _picked));
    }

    [Fact]
    public void Several_matches_by_fragment_show_a_list_and_add_nothing()
    {
        _searchResults = [Mleko, Maslo];
        var picker = Render();

        Type(picker, "1");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal(2, picker.FindAll(".mud-list-item").Count));
        Assert.Empty(_picked);
    }

    [Fact]
    public void The_only_match_by_fragment_is_added_without_waiting_for_the_search_delay()
    {
        _searchResults = [Maslo];
        var picker = Render();

        Type(picker, "s");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Maslo], _picked));
    }

    [Fact]
    public void No_match_names_the_text_and_adds_nothing()
    {
        var picker = Render();

        Type(picker, "  xyz ");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Contains("Nie znaleziono produktu o kodzie xyz", picker.Markup));
        Assert.Empty(_picked);
    }

    [Fact]
    public void After_no_match_the_text_is_back_in_the_field_and_selected_for_the_next_scan()
    {
        var picker = Render();

        Type(picker, "5900000000000");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal("5900000000000", FieldText(picker)));
        picker.WaitForAssertion(() => Assert.True(JsCalls("select") > 0));
    }

    [Fact]
    public void After_adding_a_product_the_field_is_empty_and_keeps_the_focus()
    {
        var picker = Render();

        Type(picker, "5901234123457");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal([Mleko], _picked));
        Assert.Equal(string.Empty, FieldText(picker));
        picker.WaitForAssertion(() => Assert.True(JsCalls("focus") > 0));
    }

    [Fact]
    public void Focus_stays_in_the_field_after_no_match()
    {
        var picker = Render();

        Type(picker, "xyz");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.True(JsCalls("focus") > 0));
    }

    [Fact]
    public void Two_fast_scans_are_resolved_one_at_a_time_and_in_order()
    {
        var first = new TaskCompletionSource<ProductPick?>();
        _lookup = code => code == "A1" ? first.Task : Task.FromResult<ProductPick?>(Maslo);
        var picker = Render();

        Type(picker, "A1");
        PressEnterAndWaitForEmptyField(picker);
        Type(picker, "B2");
        PressEnter(picker);

        picker.WaitForAssertion(() => Assert.Equal(["A1"], _codes));
        Assert.Empty(_picked);

        first.SetResult(Mleko);

        picker.WaitForAssertion(() => Assert.Equal([Mleko, Maslo], _picked));
        Assert.Equal(["A1", "B2"], _codes);
        Assert.Equal(string.Empty, FieldText(picker));
    }

    [Fact]
    public void A_scan_arriving_while_focus_is_being_restored_is_not_lost()
    {
        var focus = _context.JSInterop.SetupVoid(call => call.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
        _lookup = _ => Task.FromResult<ProductPick?>(Mleko);
        var picker = Render();

        Type(picker, "A1");
        PressEnter(picker);
        picker.WaitForAssertion(() => Assert.True(JsCalls("focus") > 0));
        Type(picker, "B2");
        PressEnter(picker);
        picker.WaitForAssertion(() => Assert.Equal(string.Empty, FieldText(picker)));

        focus.SetVoidResult();

        picker.WaitForAssertion(() => Assert.Equal(2, _picked.Count));
        Assert.Equal(["A1", "B2"], _codes);
    }

    [Fact]
    public void The_settled_count_changes_only_after_the_focus_is_back_in_the_field()
    {
        var focus = _context.JSInterop.SetupVoid(call => call.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
        _lookup = _ => Task.FromResult<ProductPick?>(Mleko);
        var picker = Render();
        string Settled() => picker.Find(".product-picker").GetAttribute("data-settled")!;
        var before = Settled();

        Type(picker, "A1");
        PressEnter(picker);
        picker.WaitForAssertion(() => Assert.Equal([Mleko], _picked));
        picker.WaitForAssertion(() => Assert.True(JsCalls("focus") > 0));

        // Product added, focus call still on its way: a browser test must keep waiting.
        Assert.Equal(before, Settled());

        focus.SetVoidResult();

        picker.WaitForAssertion(() => Assert.NotEqual(before, Settled()));
    }

    [Fact]
    public void A_failed_scan_followed_at_once_by_a_good_one_keeps_both_outcomes()
    {
        var first = new TaskCompletionSource<ProductPick?>();
        _lookup = code => code == "ZLY" ? first.Task : Task.FromResult<ProductPick?>(Maslo);
        var picker = Render();

        Type(picker, "ZLY");
        PressEnterAndWaitForEmptyField(picker);
        Type(picker, "DOBRY");
        PressEnter(picker);

        first.SetResult(null);

        picker.WaitForAssertion(() => Assert.Equal([Maslo], _picked));
        picker.WaitForAssertion(() => Assert.Contains("Nie znaleziono produktu o kodzie ZLY", picker.Markup));
    }

    [Fact]
    public void Text_of_a_failed_scan_does_not_overwrite_the_next_scan_being_typed()
    {
        var first = new TaskCompletionSource<ProductPick?>();
        _lookup = code => code == "ZLY" ? first.Task : Task.FromResult<ProductPick?>(null);
        var picker = Render();

        Type(picker, "ZLY");
        PressEnterAndWaitForEmptyField(picker);
        Type(picker, "NOW");

        first.SetResult(null);

        picker.WaitForAssertion(() => Assert.Contains("Nie znaleziono produktu o kodzie ZLY", picker.Markup));
        Assert.Equal("NOW", FieldText(picker));
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
