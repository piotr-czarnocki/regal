// Marks the page once the Blazor circuit runs, so browser tests know clicks and typing will be handled.
export function markInteractive() {
    document.body.dataset.interactive = 'true';
}
