using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Common;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Warehouse;

namespace Regal.UnitTests;

public sealed class InvoiceEditorTests : IAsyncLifetime
{
    private static readonly ProductPick Mleko = new(1, "MLEKO", "5901234123457", "Mleko", "szt", false, 3m, true, ProductKind.Goods, 4.50m, "5");
    private static readonly ProductPick Montaz = new(2, "MONTAZ", null, "Montaż", "h", false, 0m, true, ProductKind.Service, 100m, "23");
    private static readonly ContractorListItem Buyer = new(7, "Sklep Pod Lipą", "1234563218", null, null, null, true);

    private readonly BunitContext _context = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;
    private readonly List<InvoiceDraft> _issued = [];
    private readonly List<int> _issuedIds = [];
    private Func<Task<SaveResult>> _issueResult = () => Task.FromResult(SaveResult.Ok(42));
    private IReadOnlyList<IssuePreviewLine> _stockPreview = [];

    public InvoiceEditorTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Render<MudPopoverProvider>();
        _dialogs = _context.Render<MudDialogProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private IRenderedComponent<InvoiceEditor> Render(ContractorListItem? buyer = null) => _context.Render<InvoiceEditor>(parameters => parameters
        .Add(x => x.InitialContractor, buyer ?? Buyer)
        .Add(x => x.SearchProducts, (_, _) => Task.FromResult<IReadOnlyList<ProductPick>>([]))
        .Add(x => x.FindByCode, (code, _) => Task.FromResult<ProductPick?>(code == "MONTAZ" ? Montaz : Mleko))
        .Add(x => x.SearchContractors, (_, _) => Task.FromResult<IReadOnlyList<ContractorListItem>>([]))
        .Add(x => x.PreviewIssue, _ => Task.FromResult(_stockPreview))
        .Add(x => x.Issue, draft =>
        {
            _issued.Add(draft);
            return _issueResult();
        })
        .Add(x => x.Issued, EventCallback.Factory.Create<int>(this, _issuedIds.Add)));

    private static void Scan(IRenderedComponent<InvoiceEditor> editor, string code)
    {
        var picker = editor.FindComponent<ProductPicker>();
        picker.Find("input").Input(code);
        picker.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        picker.WaitForAssertion(() => Assert.Equal(string.Empty, picker.Find("input").GetAttribute("value") ?? string.Empty));
    }

    private static AngleSharp.Dom.IElement ButtonIn<T>(IRenderedComponent<T> fragment, string text)
        where T : IComponent =>
        fragment.FindAll("button").Single(button => button.TextContent.Contains(text));

    private static bool IssueDisabled(IRenderedComponent<InvoiceEditor> editor) => ButtonIn(editor, "Wystaw fakturę").HasAttribute("disabled");

    private IRenderedComponent<InvoiceEditor> ReadyToIssue()
    {
        _stockPreview = [new IssuePreviewLine(1, "Mleko", [new BatchAllocation(new BatchAvailability(3, "L-3", null, 9), 1)], null)];
        var editor = Render();
        Scan(editor, Mleko.Ean!);
        Scan(editor, "MONTAZ");
        editor.WaitForAssertion(() => Assert.Contains("Montaż", editor.Markup));
        ButtonIn(editor, "Pokaż podgląd").Click();
        editor.WaitForAssertion(() => Assert.False(IssueDisabled(editor)));
        return editor;
    }

    private void ClickInDialog(string text)
    {
        _dialogs.WaitForAssertion(() => Assert.Contains(text, _dialogs.Markup));
        ButtonIn(_dialogs, text).Click();
    }

    [Fact]
    public void Enter_after_a_scan_adds_a_line_and_never_issues()
    {
        var editor = Render();

        Scan(editor, Mleko.Ean!);

        editor.WaitForAssertion(() => Assert.Contains("Mleko", editor.Markup));
        Assert.Empty(_issued);
        Assert.Empty(editor.FindAll("form"));
        Assert.Empty(editor.FindAll("button[type=submit]"));
    }

    [Fact]
    public void Issuing_needs_a_preview_that_shows_batches_for_goods_and_marks_services()
    {
        var editor = ReadyToIssue();

        Assert.Contains("L-3", editor.Markup);
        Assert.Contains("usługa, bez wydania", editor.Markup);
    }

    [Fact]
    public void Shortage_keeps_issuing_blocked()
    {
        _stockPreview = [new IssuePreviewLine(1, "Mleko", [], "brak towaru: dostępne 0, potrzeba 1.")];
        var editor = Render();
        Scan(editor, Mleko.Ean!);

        ButtonIn(editor, "Pokaż podgląd").Click();

        editor.WaitForAssertion(() => Assert.Contains("brak towaru", editor.Markup));
        Assert.True(IssueDisabled(editor));
    }

    [Fact]
    public void Missing_buyer_is_reported_instead_of_a_preview()
    {
        var editor = _context.Render<InvoiceEditor>(parameters => parameters
            .Add(x => x.SearchProducts, (_, _) => Task.FromResult<IReadOnlyList<ProductPick>>([]))
            .Add(x => x.FindByCode, (_, _) => Task.FromResult<ProductPick?>(Montaz))
            .Add(x => x.SearchContractors, (_, _) => Task.FromResult<IReadOnlyList<ContractorListItem>>([]))
            .Add(x => x.PreviewIssue, _ => Task.FromResult(_stockPreview))
            .Add(x => x.Issue, _ => Task.FromResult(SaveResult.Ok(1)))
            .Add(x => x.Issued, EventCallback.Factory.Create<int>(this, _issuedIds.Add)));
        Scan(editor, "MONTAZ");

        ButtonIn(editor, "Pokaż podgląd").Click();

        editor.WaitForAssertion(() => Assert.Contains("Wybierz nabywcę.", editor.Markup));
        Assert.True(IssueDisabled(editor));
    }

    [Fact]
    public void Issuing_asks_first_then_sends_the_draft_once()
    {
        var editor = ReadyToIssue();

        ButtonIn(editor, "Wystaw fakturę").Click();
        Assert.Empty(_issued);
        ClickInDialog("Wystaw");

        editor.WaitForAssertion(() => Assert.Equal([42], _issuedIds));
        var draft = Assert.Single(_issued);
        Assert.Equal(Buyer.Id, draft.ContractorId);
        Assert.Equal(InvoiceRules.DefaultPaymentDays, draft.PaymentDays);
        Assert.Equal("transfer", draft.PaymentMethod);
        Assert.Equal([(1, 1m, 4.50m), (2, 1m, 100m)], draft.Lines.Select(l => (l.ProductId, l.Quantity, l.NetPrice!.Value)));
    }

    [Fact]
    public void Declining_the_confirmation_issues_nothing()
    {
        var editor = ReadyToIssue();

        ButtonIn(editor, "Wystaw fakturę").Click();
        ClickInDialog("Wróć");

        editor.WaitForAssertion(() => Assert.False(IssueDisabled(editor)));
        Assert.Empty(_issued);
    }

    [Fact]
    public void Failed_issue_needs_a_new_preview_and_retries_with_the_same_request_id()
    {
        var attempts = 0;
        _issueResult = () => Task.FromResult(++attempts == 1 ? SaveResult.Fail("Mleko: brak towaru.") : SaveResult.Ok(5));
        var editor = ReadyToIssue();

        ButtonIn(editor, "Wystaw fakturę").Click();
        ClickInDialog("Wystaw");
        editor.WaitForAssertion(() => Assert.Contains("Mleko: brak towaru.", editor.Markup));
        Assert.True(IssueDisabled(editor));

        ButtonIn(editor, "Pokaż podgląd").Click();
        editor.WaitForAssertion(() => Assert.False(IssueDisabled(editor)));
        ButtonIn(editor, "Wystaw fakturę").Click();
        ClickInDialog("Wystaw");

        editor.WaitForAssertion(() => Assert.Equal([5], _issuedIds));
        Assert.Equal(_issued[0].RequestId, _issued[1].RequestId);
    }
}
