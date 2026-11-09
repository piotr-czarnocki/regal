using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Common;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Warehouse;

namespace Regal.UnitTests;

public sealed class StockDocumentEditorTests : IAsyncLifetime
{
    private const string Code = "5901234123457";

    private static readonly ProductPick Mleko = new(1, "MLEKO", Code, "Mleko", "szt", false, 3m);

    private readonly BunitContext _context = new();
    private readonly List<StockDocumentDraft> _posts = [];
    private readonly List<int> _posted = [];
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;
    private Func<Task<SaveResult>> _postResult = () => Task.FromResult(SaveResult.Ok(42));
    private IReadOnlyList<IssuePreviewLine> _issuePreview = [];
    private readonly List<StockDocumentDraft> _previewDrafts = [];
    private int _lookups;
    private Task<ProductPick?>? _firstLookup;

    public StockDocumentEditorTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Render<MudPopoverProvider>();
        _dialogs = _context.Render<MudDialogProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private IRenderedComponent<StockDocumentEditor> Render(StockDocumentType type) => _context.Render<StockDocumentEditor>(parameters => parameters
        .Add(x => x.InitialType, type)
        .Add(x => x.SearchProducts, (_, _) => Task.FromResult<IReadOnlyList<ProductPick>>([]))
        .Add(x => x.FindByCode, (_, _) =>
        {
            var first = Interlocked.Increment(ref _lookups) == 1;
            return first && _firstLookup is not null ? _firstLookup : Task.FromResult<ProductPick?>(Mleko);
        })
        .Add(x => x.LoadBatches, _ => Task.FromResult<IReadOnlyList<StockBatchRow>>([]))
        .Add(x => x.SearchContractors, (_, _) => Task.FromResult<IReadOnlyList<ContractorListItem>>([]))
        .Add(x => x.PreviewIssue, draft =>
        {
            _previewDrafts.Add(draft);
            return Task.FromResult(_issuePreview);
        })
        .Add(x => x.Post, draft =>
        {
            _posts.Add(draft);
            return _postResult();
        })
        .Add(x => x.Posted, EventCallback.Factory.Create<int>(this, _posted.Add)));

    private static void Scan(IRenderedComponent<StockDocumentEditor> editor)
    {
        var picker = editor.FindComponent<ProductPicker>();
        picker.Find("input").Input(Code);
        picker.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static void ScanAndWait(IRenderedComponent<StockDocumentEditor> editor)
    {
        Scan(editor);
        editor.WaitForAssertion(() => Assert.Contains("Mleko", editor.Markup));
    }

    private static string? QuantityOf(IRenderedComponent<StockDocumentEditor> editor) =>
        editor.FindComponent<MudNumericField<decimal?>>().Find("input").GetAttribute("value");

    private static AngleSharp.Dom.IElement ButtonIn<T>(IRenderedComponent<T> fragment, string text)
        where T : IComponent =>
        fragment.FindAll("button").Single(button => button.TextContent.Contains(text));

    private static void Preview(IRenderedComponent<StockDocumentEditor> editor)
    {
        ButtonIn(editor, "Pokaż podgląd").Click();
        editor.WaitForAssertion(() => Assert.Contains("Podgląd", editor.Markup));
    }

    private static bool ApproveDisabled(IRenderedComponent<StockDocumentEditor> editor) =>
        ButtonIn(editor, "Zatwierdź").HasAttribute("disabled");

    /// <summary>RW has no required contractor or cost, so one scan makes a valid draft.</summary>
    private IRenderedComponent<StockDocumentEditor> ReadyToApprove(StockDocumentType type)
    {
        _issuePreview = [new IssuePreviewLine(1, "Mleko", [new BatchAllocation(new BatchAvailability(7, "L-7", null, 5), 1)], null)];
        var editor = Render(type);
        ScanAndWait(editor);
        if (type == StockDocumentType.RW)
        {
            editor.FindComponents<MudTextField<string>>()[0].Find("input").Change("Strata");
        }

        Preview(editor);
        editor.WaitForAssertion(() => Assert.False(ApproveDisabled(editor)));
        return editor;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private void ClickInDialog(string text)
    {
        _dialogs.WaitForAssertion(() => Assert.Contains(text, _dialogs.Markup));
        ButtonIn(_dialogs, text).Click();
    }

    [Fact]
    public void Enter_after_a_scan_adds_a_line_and_never_posts()
    {
        var editor = Render(StockDocumentType.WZ);

        ScanAndWait(editor);

        Assert.Empty(_posts);
        Assert.Equal("1", QuantityOf(editor));
    }

    [Fact]
    public void Enter_in_a_line_field_posts_nothing()
    {
        var editor = Render(StockDocumentType.WZ);
        ScanAndWait(editor);

        editor.FindComponent<MudNumericField<decimal?>>().Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(_posts);
        Assert.DoesNotContain("Podgląd", editor.Markup);
    }

    [Fact]
    public void There_is_no_form_and_no_submit_button_for_enter_to_reach()
    {
        var editor = Render(StockDocumentType.PZ);
        ScanAndWait(editor);

        Assert.Empty(editor.FindAll("form"));
        Assert.Empty(editor.FindAll("button[type=submit]"));
    }

    [Fact]
    public void Scanning_the_same_code_again_adds_one_to_the_quantity()
    {
        var editor = Render(StockDocumentType.WZ);
        ScanAndWait(editor);

        Scan(editor);

        editor.WaitForAssertion(() => Assert.Equal("2", QuantityOf(editor)));
        Assert.Single(editor.FindComponents<MudNumericField<decimal?>>());
    }

    [Fact]
    public async Task A_scan_arriving_during_the_previous_lookup_makes_one_line_with_quantity_two()
    {
        var firstLookup = new TaskCompletionSource<ProductPick?>();
        _firstLookup = firstLookup.Task;
        var editor = Render(StockDocumentType.WZ);
        var picker = editor.FindComponent<ProductPicker>();

        Scan(editor);
        picker.WaitForAssertion(() => Assert.Equal(string.Empty, picker.Find("input").GetAttribute("value") ?? string.Empty));
        await UntilAsync(() => Volatile.Read(ref _lookups) == 1);
        Scan(editor);
        firstLookup.SetResult(Mleko);
        await UntilAsync(() => Volatile.Read(ref _lookups) == 2);
        editor.WaitForAssertion(() => Assert.Contains("Mleko", editor.Markup));

        // Reads the draft the editor would send, because a numeric field read early can show stale markup.
        ButtonIn(editor, "Pokaż podgląd").Click();
        editor.WaitForAssertion(() => Assert.Single(_previewDrafts));
        var line = Assert.Single(_previewDrafts[0].Lines);
        Assert.Equal(2m, line.Quantity);
        Assert.Empty(_posts);
    }

    [Fact]
    public void Approval_is_blocked_until_the_preview_is_shown()
    {
        var editor = Render(StockDocumentType.WZ);
        ScanAndWait(editor);

        Assert.True(ApproveDisabled(editor));
    }

    [Fact]
    public void Issue_preview_shows_the_batches_it_would_take()
    {
        var editor = ReadyToApprove(StockDocumentType.WZ);

        Assert.Contains("L-7", editor.Markup);
    }

    [Fact]
    public void Shortage_in_the_preview_keeps_approval_blocked()
    {
        _issuePreview = [new IssuePreviewLine(1, "Mleko", [], "brak towaru: dostępne 0, potrzeba 1.")];
        var editor = Render(StockDocumentType.WZ);
        ScanAndWait(editor);

        Preview(editor);

        editor.WaitForAssertion(() => Assert.Contains("brak towaru", editor.Markup));
        Assert.True(ApproveDisabled(editor));
    }

    [Fact]
    public void Changing_a_line_requires_a_new_preview()
    {
        var editor = ReadyToApprove(StockDocumentType.WZ);

        Scan(editor);

        editor.WaitForAssertion(() => Assert.True(ApproveDisabled(editor)));
    }

    [Fact]
    public void Validation_problem_is_shown_instead_of_a_preview()
    {
        var editor = Render(StockDocumentType.PZ);
        ScanAndWait(editor);

        ButtonIn(editor, "Pokaż podgląd").Click();

        editor.WaitForAssertion(() => Assert.Contains("Wybierz dostawcę.", editor.Markup));
        Assert.True(ApproveDisabled(editor));
    }

    [Fact]
    public void Approval_asks_first_and_then_posts_and_reports_the_document()
    {
        var editor = ReadyToApprove(StockDocumentType.WZ);

        ButtonIn(editor, "Zatwierdź").Click();
        Assert.Empty(_posts);
        ClickInDialog("Zatwierdź");

        editor.WaitForAssertion(() => Assert.Equal([42], _posted));
        Assert.Single(_posts);
    }

    [Fact]
    public void Declining_the_confirmation_posts_nothing_and_allows_another_try()
    {
        var editor = ReadyToApprove(StockDocumentType.WZ);

        ButtonIn(editor, "Zatwierdź").Click();
        ClickInDialog("Wróć");

        editor.WaitForAssertion(() => Assert.False(ApproveDisabled(editor)));
        Assert.Empty(_posts);
    }

    [Fact]
    public void Button_is_blocked_during_the_save_and_only_one_post_is_sent()
    {
        var pending = new TaskCompletionSource<SaveResult>();
        _postResult = () => pending.Task;
        var editor = ReadyToApprove(StockDocumentType.WZ);

        ButtonIn(editor, "Zatwierdź").Click();
        ClickInDialog("Zatwierdź");
        editor.WaitForAssertion(() => Assert.Single(_posts));

        Assert.True(ApproveDisabled(editor));

        pending.SetResult(SaveResult.Ok(1));
        editor.WaitForAssertion(() => Assert.Equal([1], _posted));
        Assert.Single(_posts);
    }

    [Fact]
    public void Failed_post_shows_the_message_needs_a_new_preview_and_retries_with_the_same_request_id()
    {
        var attempts = 0;
        _postResult = () => Task.FromResult(++attempts == 1 ? SaveResult.Fail("Mleko: brak towaru.") : SaveResult.Ok(9));
        var editor = ReadyToApprove(StockDocumentType.WZ);

        ButtonIn(editor, "Zatwierdź").Click();
        ClickInDialog("Zatwierdź");
        editor.WaitForAssertion(() => Assert.Contains("Mleko: brak towaru.", editor.Markup));
        Assert.True(ApproveDisabled(editor));

        Preview(editor);
        editor.WaitForAssertion(() => Assert.False(ApproveDisabled(editor)));
        ButtonIn(editor, "Zatwierdź").Click();
        ClickInDialog("Zatwierdź");

        editor.WaitForAssertion(() => Assert.Equal([9], _posted));
        Assert.Equal(2, _posts.Count);
        Assert.Equal(_posts[0].RequestId, _posts[1].RequestId);
        Assert.NotEqual(Guid.Empty, _posts[0].RequestId);
    }

    [Fact]
    public void Decimal_comma_is_read_as_in_poland()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            var editor = Render(StockDocumentType.PW);
            ScanAndWait(editor);

            editor.FindComponents<MudNumericField<decimal?>>()[0].Find("input").Change("3");
            editor.FindComponents<MudNumericField<decimal?>>()[1].Find("input").Change("2,50");
            Preview(editor);
            editor.WaitForAssertion(() => Assert.False(ApproveDisabled(editor)));
            ButtonIn(editor, "Zatwierdź").Click();
            ClickInDialog("Zatwierdź");

            editor.WaitForAssertion(() => Assert.Single(_posts));
            var line = Assert.Single(_posts[0].Lines);
            Assert.Equal((3m, 2.50m), (line.Quantity, line.UnitCost!.Value));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Draft_carries_the_lines_type_and_reason()
    {
        var editor = ReadyToApprove(StockDocumentType.RW);

        ButtonIn(editor, "Zatwierdź").Click();
        ClickInDialog("Zatwierdź");

        editor.WaitForAssertion(() => Assert.Single(_posts));
        var draft = _posts[0];
        Assert.Equal(StockDocumentType.RW, draft.Type);
        Assert.Equal("Strata", draft.Notes);
        var line = Assert.Single(draft.Lines);
        Assert.Equal(Mleko.Id, line.ProductId);
        Assert.Equal(1m, line.Quantity);
    }
}
