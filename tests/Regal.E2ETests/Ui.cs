using Microsoft.Playwright;

namespace Regal.E2ETests;

/// <summary>Steps written the way a person does them: by visible labels and button names.</summary>
public static class Ui
{
    private const string OpenPopover = ".mud-popover-open";

    public static ILocator Field(this IPage page, string label) => page.GetByLabel(label, new() { Exact = true });

    public static ILocator Button(this IPage page, string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    public static ILocator DialogButton(this IPage page, string name) =>
        page.Locator(".mud-dialog").GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    /// <summary>Fills a field and leaves it, so fields that update on change see the value.</summary>
    public static async Task FillFieldAsync(this IPage page, string label, string text)
    {
        var field = page.Field(label).Last;
        await field.FillAsync(text);
        await field.PressAsync("Tab");
    }

    /// <summary>Types into an autocomplete and picks the suggestion with this text.</summary>
    public static async Task ChooseAsync(this IPage page, string label, string text)
    {
        await page.Field(label).FillAsync(text);
        await page.Locator(OpenPopover).GetByText(text).First.ClickAsync();
    }

    /// <summary>Opens a select and picks the item whose text starts like this.</summary>
    public static async Task SelectAsync(this IPage page, string label, string itemText)
    {
        await page.Field(label).ClickAsync();
        await page.Locator(OpenPopover).GetByText(itemText).First.ClickAsync();
    }

    /// <summary>Types a code into the product field and confirms it with Enter, as a USB scanner does.</summary>
    public static async Task ScanAsync(this IPage page, string code)
    {
        var field = page.Field("Dodaj produkt: nazwa, SKU lub EAN");
        await field.FillAsync(code);
        await field.PressAsync("Enter");
        await Assertions.Expect(field).ToHaveValueAsync(string.Empty);
    }

    public static async Task ApproveAsync(this IPage page, string button, string confirm)
    {
        await page.Button("Pokaż podgląd").ClickAsync();
        await Assertions.Expect(page.Button(button)).ToBeEnabledAsync();
        await page.Button(button).ClickAsync();
        await page.DialogButton(confirm).ClickAsync();
    }

    /// <summary>Keeps a screenshot of the page when a step fails, next to the test output.</summary>
    public static async Task WithScreenshotOnFailureAsync(this IPage page, string name, Func<Task> steps)
    {
        try
        {
            await steps();
        }
        catch
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(folder);
            await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"{name}.png"), FullPage = true });
            throw;
        }
    }
}
