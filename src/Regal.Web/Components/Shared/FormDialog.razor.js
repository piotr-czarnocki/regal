// Puts the cursor in the first field the user can type into. MudSelect is skipped:
// its input is only a display, and a list opening on its own would get in the way.
const editable = 'input:not([type=hidden]):not([type=checkbox]):not([disabled]):not([readonly]), textarea:not([disabled]):not([readonly])';

export function focusFirstField(form) {
    // Waits for the next frame so the dialog is on screen and can take focus.
    requestAnimationFrame(() => {
        const field = [...form.querySelectorAll(editable)].find(element => !element.closest('.mud-select'));
        field?.focus();
    });
}
