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

    /// <summary>
    /// Types into an autocomplete and picks the suggestion with this text. Returns once the list has
    /// closed and the field shows the choice, so the next step does not type while the
    /// autocomplete is still moving the focus around.
    /// </summary>
    public static async Task ChooseAsync(this IPage page, string label, string text)
    {
        var field = page.Field(label);
        await field.FillAsync(text);
        await page.Locator(OpenPopover).GetByText(text).First.ClickAsync();
        await Assertions.Expect(page.Locator(OpenPopover)).ToHaveCountAsync(0);
        // Starts with: an invoice buyer is shown with its NIP after the name.
        await Assertions.Expect(field).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(text)));
    }

    /// <summary>Opens a select and picks the item whose text starts like this.</summary>
    public static async Task SelectAsync(this IPage page, string label, string itemText)
    {
        await page.Field(label).ClickAsync();
        await page.Locator(OpenPopover).GetByText(itemText).First.ClickAsync();
    }

    /// <summary>
    /// Types a code into the product field and confirms it with Enter, as a USB scanner does.
    /// Returns once the picker has finished and moved the focus back to its field, as a person
    /// sees the cursor return. Typing earlier raced the focus call: under load the text meant
    /// for the quantity landed in the product field (docs/przygody.md).
    /// </summary>
    public static async Task ScanAsync(this IPage page, string code)
    {
        var picker = page.Locator(".product-picker");
        var settled = await picker.GetAttributeAsync("data-settled");
        var field = page.Field("Dodaj produkt: nazwa, SKU lub EAN");

        await field.FillAsync(code);
        await Assertions.Expect(field).ToHaveValueAsync(code);
        await field.PressAsync("Enter");

        await Assertions.Expect(picker).Not.ToHaveAttributeAsync("data-settled", settled!);
        await Assertions.Expect(field).ToHaveValueAsync(string.Empty);
        await Assertions.Expect(field).ToBeFocusedAsync();
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
