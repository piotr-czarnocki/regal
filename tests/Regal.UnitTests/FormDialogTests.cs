using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Components.Layout;
using Regal.Web.Components.Shared;
using Regal.Web.Features.Common;

namespace Regal.UnitTests;

// bUnit has no browser. In a browser, Enter in a field submits the form through the form's
// submit button, so these tests fire the form's submit event and check that button is wired up.
public sealed class FormDialogTests : IAsyncLifetime
{
    private const string ModulePath = "./Components/Shared/FormDialog.razor.js";

    private readonly BunitContext _context = new();
    private readonly BunitJSModuleInterop _module;

    public FormDialogTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _module = _context.JSInterop.SetupModule(ModulePath);
        _module.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // Asynchronous on purpose: MudBlazor's services can only be disposed asynchronously.
    public async Task DisposeAsync() => await _context.DisposeAsync();

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> OpenAsync(Func<Task<SaveResult>> save)
    {
        var provider = _context.Render<MudDialogProvider>();
        var parameters = new DialogParameters<FormDialog>
        {
            { x => x.Save, save },
            { x => x.ChildContent, (RenderFragment)(builder => builder.AddMarkupContent(0, "<input id=\"field\" />")) },
        };

        IDialogReference dialog = null!;
        await provider.InvokeAsync(async () =>
            dialog = await _context.Services.GetRequiredService<IDialogService>().ShowAsync<FormDialog>("Test", parameters, DialogDefaults.Form));
        provider.WaitForElement("form");
        return (provider, dialog);
    }

    [Fact]
    public async Task Enter_submits_the_form_which_saves_once_and_closes_the_dialog()
    {
        var calls = 0;
        var (provider, dialog) = await OpenAsync(() =>
        {
            calls++;
            return Task.FromResult(SaveResult.Ok(5));
        });

        provider.Find("form").Submit();

        var result = await dialog.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        Assert.False(result!.Canceled);
        Assert.Equal(5, result.Data);
    }

    [Fact]
    public async Task Save_button_is_the_submit_button_of_the_form_so_enter_and_the_button_share_one_path()
    {
        var (provider, _) = await OpenAsync(() => Task.FromResult(SaveResult.Ok(1)));

        var button = provider.Find("button[type=submit]");

        Assert.Equal(provider.Find("form").Id, button.GetAttribute("form"));
        Assert.Contains("Zapisz", button.TextContent);
    }

    [Fact]
    public async Task Pressing_enter_twice_during_a_save_saves_once()
    {
        var calls = 0;
        var pending = new TaskCompletionSource<SaveResult>();
        var (provider, dialog) = await OpenAsync(() =>
        {
            calls++;
            return pending.Task;
        });

        provider.Find("form").Submit();
        provider.Find("form").Submit();
        pending.SetResult(SaveResult.Ok(1));

        await dialog.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Failed_save_shows_the_message_keeps_the_dialog_open_and_can_be_retried()
    {
        var calls = 0;
        var (provider, dialog) = await OpenAsync(() =>
        {
            calls++;
            return Task.FromResult(calls == 1 ? SaveResult.Fail("Podaj nazwę.") : SaveResult.Ok(1));
        });

        provider.Find("form").Submit();

        provider.WaitForAssertion(() => Assert.Contains("Podaj nazwę.", provider.Markup));
        Assert.False(dialog.Result.IsCompleted);

        provider.Find("form").Submit();

        await dialog.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task First_field_gets_focus_when_the_dialog_opens()
    {
        var (provider, _) = await OpenAsync(() => Task.FromResult(SaveResult.Ok(1)));

        provider.WaitForAssertion(() => _module.VerifyInvoke("focusFirstField"));
    }

    [Fact]
    public void Dialog_options_allow_escape_and_leave_focus_to_the_form()
    {
        Assert.True(DialogDefaults.Form.CloseOnEscapeKey);
        Assert.Equal(DefaultFocus.None, DialogDefaults.Form.DefaultFocus);
    }
}
