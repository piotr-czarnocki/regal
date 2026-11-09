# Zasady pracy w tym repozytorium

## Poświadczenia

Nigdy nie odczytuj poświadczeń ani sekretów, także po to, żeby coś sprawdzić, i także z maskowaniem wyniku. Dotyczy to w szczególności:

- `git credential` i pomocników poświadczeń Gita (`git credential-manager` i podobnych),
- `dotnet user-secrets list`,
- pliku `.env`.

Reguły `deny` w `.claude/settings.json` blokują te polecenia, ale nie są granicą bezpieczeństwa: nie obejmują np. wywołania przez `sh -c` ani skryptu. Obowiązuje zasada, a nie tylko blokada.

Gdy potrzebne jest coś, co wymaga sekretu, użyj zwykłego polecenia, które samo korzysta z poświadczeń (np. `git push`). Jeśli to nie zadziała, przekaż użytkownikowi polecenie do wykonania, zamiast sprawdzać, co jest zapisane.
