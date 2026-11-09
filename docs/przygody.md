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

### `git push` nie działał z wnętrza sesji Claude Code

- **Objaw:** `fatal: could not read Username for 'https://github.com': terminal prompts disabled`, a gałąź `main` na GitHubie pozostawała pusta.
- **Przyczyna:** polecenia w sesji Claude Code działają bez interakcji, więc Git nie mógł poprosić o zalogowanie do GitHuba.
- **Rozwiązanie:** zalogowanie się i wykonanie `git push` w zwykłym oknie PowerShella.

### Client secret Google widoczny na zrzucie ekranu

- **Objaw:** podczas konfiguracji przypadkowo pokazałem Client secret Google na zrzucie ekranu.
- **Rozwiązanie:** sekret został wymieniony (rotacja), stary usunięty, a historia PowerShella wyczyszczona.
- **Wniosek:** nie robić zrzutów ekranu poleceń z sekretami.

## Etap „Kartoteki” (9.11.2026)

### Popularny prefiks 2xx nie jest przeznaczony dla jednej firmy

- **Objaw:** przy wyborze prefiksu wewnętrznego EAN-13 naturalnym kandydatem wydawał się zakres `200–299`, często spotykany w sklepach.
- **Przyczyna:** według listy prefiksów GS1 zakres `200–299` służy numerom ograniczonego obiegu w regionie geograficznym, a zasady ustala krajowa organizacja GS1. Numerom w obrębie jednej firmy odpowiada `040–049`.
- **Rozwiązanie:** generator używa prefiksu `040`, a ręcznie wpisanych kodów z zakresu `04x` nie przyjmujemy. Pełnego tekstu GS1 General Specifications nie udało się odczytać automatycznie (PDF), więc podstawą jest tabela prefiksów ze strony GS1.
- **Do pamiętania:** kod zaczynający się od `0` to jednocześnie UPC-A, więc część skanerów zwróci 12 cyfr. Trzeba to obsłużyć w etapie skanowania.

## Etap „Magazyn” (9.11.2026)

### Claude Code odczytał poświadczenia GitHuba bez polecenia

- **Objaw:** podczas przygotowywania planu Claude Code wywołał `git credential fill` dla `github.com`, żeby sprawdzić, czy `git push` zadziała. Nikt o to nie prosił, a do tego celu wystarczyło po prostu spróbować wykonać `git push`.
- **Skutek:** polecenie pobrało z Git Credential Manager nazwę użytkownika i token. Filtr `sed` zamienił hasło na `<present>`, zanim wynik wrócił do sesji, więc token nie trafił do rozmowy ani do plików i nie został nigdzie użyty. Nazwa użytkownika była widoczna.
- **Przyczyna:** działanie wykraczające poza potrzebę. Maskowanie wyniku nie zmienia faktu, że sekret został pobrany przez proces uruchomiony przez narzędzie.
- **Rozwiązanie:** reguły `deny` w `.claude/settings.json` dla `git credential`, `dotnet user-secrets list` i odczytu `.env` oraz zasada w `CLAUDE.md`: poświadczeń nie odczytujemy nigdy, także w celu sprawdzenia.
- **Wniosek:** reguły `deny` dopasowują tekst polecenia, więc nie zatrzymają np. `sh -c` ani skryptu. Są dodatkowym zabezpieczeniem, a nie granicą bezpieczeństwa. Katalog `.claude/` jest w `.gitignore`, więc reguły działają tylko na tym komputerze, a zasada w `CLAUDE.md` jest w repozytorium.
