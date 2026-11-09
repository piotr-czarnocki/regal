using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Components.Layout;
using Regal.Web.Components.Shared;
using Regal.Web.Features.Warehouse;

namespace Regal.UnitTests;

public sealed class BarcodeScannerTests : IAsyncLifetime
{
    private const string ModulePath = "./Components/Shared/BarcodeScannerDialog.razor.js";

    private readonly BunitContext _context = new();
    private readonly BunitJSModuleInterop _module;
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public BarcodeScannerTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _module = _context.JSInterop.SetupModule(ModulePath);
        _context.Services.AddMudServices();
        _context.Render<MudPopoverProvider>();
        _dialogs = _context.Render<MudDialogProvider>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private async Task<IDialogReference> OpenAsync()
    {
        IDialogReference dialog = null!;
        await _dialogs.InvokeAsync(async () =>
            dialog = await _context.Services.GetRequiredService<IDialogService>().ShowAsync<BarcodeScannerDialog>("Skanuj kod", DialogDefaults.Form));
        return dialog;
    }

    private void WaitForCameraStart() =>
        _dialogs.WaitForAssertion(() => Assert.Single(_module.Invocations, i => i.Identifier == "start"));

    [Theory]
    [InlineData("5901234123457", true)]
    [InlineData("SKU-12.A", true)]
    [InlineData("", false)]
    [InlineData("<script>", false)]
    [InlineData("kod ze spacją", false)]
    [InlineData("ąę", false)]
    public void Only_plain_codes_are_accepted(string code, bool accepted) =>
        Assert.Equal(accepted, BarcodeScannerDialog.IsAcceptable(code));

    [Fact]
    public void Code_longer_than_50_characters_is_refused() =>
        Assert.False(BarcodeScannerDialog.IsAcceptable(new string('1', 51)));

    [Fact]
    public async Task Without_https_the_dialog_explains_and_does_not_start_the_camera()
    {
        _module.Setup<bool>("cameraAvailable").SetResult(false);

        await OpenAsync();

        _dialogs.WaitForAssertion(() => Assert.Contains("HTTPS", _dialogs.Markup));
        Assert.DoesNotContain(_module.Invocations, i => i.Identifier == "start");
    }

    [Fact]
    public async Task Camera_is_started_and_a_detected_code_closes_the_dialog_with_it()
    {
        _module.Setup<bool>("cameraAvailable").SetResult(true);
        _module.SetupVoid("start").SetVoidResult();
        var dialog = await OpenAsync();
        WaitForCameraStart();

        var scanner = _dialogs.FindComponent<BarcodeScannerDialog>();
        await scanner.InvokeAsync(() => scanner.Instance.OnDetected("5901234123457"));

        var result = await dialog.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("5901234123457", result!.Data);
    }

    [Fact]
    public async Task Unacceptable_code_keeps_the_dialog_open_with_a_message()
    {
        _module.Setup<bool>("cameraAvailable").SetResult(true);
        _module.SetupVoid("start").SetVoidResult();
        var dialog = await OpenAsync();
        WaitForCameraStart();

        var scanner = _dialogs.FindComponent<BarcodeScannerDialog>();
        await scanner.InvokeAsync(() => scanner.Instance.OnDetected("<img src=x>"));

        _dialogs.WaitForAssertion(() => Assert.Contains("niedozwolone znaki", _dialogs.Markup));
        Assert.False(dialog.Result.IsCompleted);
    }

    [Fact]
    public async Task Refused_permission_is_explained()
    {
        _module.Setup<bool>("cameraAvailable").SetResult(true);
        // Matched by name only: the call carries the video element and a .NET reference.
        var start = _module.SetupVoid("start", _ => true);

        await OpenAsync();
        WaitForCameraStart();
        await _dialogs.InvokeAsync(() => start.SetException(new Microsoft.JSInterop.JSException("NotAllowedError")));

        _dialogs.WaitForAssertion(() => Assert.Contains("Brak zgody na użycie aparatu", _dialogs.Markup));
    }

    [Fact]
    public async Task Code_from_the_camera_is_added_like_one_confirmed_with_enter()
    {
        var picked = new List<ProductPick>();
        var codes = new List<string>();
        var mleko = new ProductPick(1, "MLEKO", "5901234123457", "Mleko", "szt", false, 3m);
        var picker = _context.Render<ProductPicker>(parameters => parameters
            .Add(x => x.Search, (_, _) => Task.FromResult<IReadOnlyList<ProductPick>>([]))
            .Add(x => x.FindByCode, (code, _) =>
            {
                codes.Add(code);
                return Task.FromResult<ProductPick?>(mleko);
            })
            .Add(x => x.Picked, EventCallback.Factory.Create<ProductPick>(this, picked.Add)));

        var button = picker.FindComponent<ScanButton>();
        await picker.InvokeAsync(() => button.Instance.Scanned.InvokeAsync("5901234123457"));

        picker.WaitForAssertion(() => Assert.Equal([mleko], picked));
        Assert.Equal(["5901234123457"], codes);
    }
}
