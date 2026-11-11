using Microsoft.JSInterop;

namespace Regal.Web.Components.Shared;

/// <summary>Hands a file built on the server to the browser, which saves it.</summary>
public sealed class FileDownloader(IJSRuntime js)
{
    public const string ModulePath = "./js/file-download.js";

    public async Task SaveAsync(string fileName, byte[] content)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        using var stream = new MemoryStream(content);
        using var reference = new DotNetStreamReference(stream);
        await module.InvokeVoidAsync("saveFile", fileName, reference);
    }
}
