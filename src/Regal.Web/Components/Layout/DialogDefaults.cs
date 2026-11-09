using MudBlazor;

namespace Regal.Web.Components.Layout;

public static class DialogDefaults
{
    // FullWidth keeps forms usable on a phone, MaxWidth keeps them readable on a desktop.
    public static readonly DialogOptions Form = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = true,
        BackdropClick = false,
    };
}
