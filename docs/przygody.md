# Przygody

Dziennik problemów napotkanych podczas pracy: objaw, przyczyna, rozwiązanie. Wpisy powstają na bieżąco.

## Etap „Fundament” (8.11.2026)

### Testy nie kompilowały się przez dwie wersje EF Core

- **Objaw:** błąd `CS1705` w `Regal.IntegrationTests`: `Regal.Web` używa EF Core 10.0.14, a test widzi 10.0.4.
- **Przyczyna:** pakiet `Microsoft.EntityFrameworkCore.Design` podnosił wersję EF Core w `Regal.Web`, ale jest oznaczony `PrivateAssets="all"`, więc ta informacja nie docierała do projektów testów. Te dostawały starszą wersję, wymaganą przez Npgsql.
- **Rozwiązanie:** bezpośrednie odwołanie do `Microsoft.EntityFrameworkCore.Relational` 10.0.14 w `Regal.Web`.

### Wylogowanie działało bez tokenu antyforgery

- **Objaw:** podczas ręcznego sprawdzania `POST /auth/logout` bez tokenu zwracał przekierowanie 302 zamiast odmowy. Testy automatyczne tego nie obejmowały.
- **Przyczyna:** `UseAntiforgery` sprawdza token i zapisuje wynik, ale żądanie odrzuca dopiero endpoint wiążący dane formularza. Endpoint wylogowania nie wiązał żadnych danych.
- **Rozwiązanie:** jawne wywołanie `IAntiforgery.IsRequestValidAsync` w endpointcie.
