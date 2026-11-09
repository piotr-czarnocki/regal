# Narzędzia i technologie

Ten dokument wymienia narzędzia użyte w projekcie, podaje ich wersje i uzasadnia wybór na tle alternatyw. Uzasadnienie narzędzi testowych to jednocześnie punkt 7 analizy dynamicznej („Dobór narzędzi”).

Wersje pochodzą z plików projektu (`global.json`, `*.csproj`, `docker-compose.yml`) w stanie z 10.11.2026.

## Aplikacja

| Narzędzie | Wersja | Do czego |
|---|---|---|
| .NET SDK, C# | 10.0.403 (LTS) | Platforma i język |
| ASP.NET Core, Blazor Web App (Interactive Server) | 10.0 | Aplikacja webowa i interfejs |
| MudBlazor | 9.12.0 | Komponenty interfejsu (tabele, formularze, okna, wykresy `MudChart`) |
| Entity Framework Core + Npgsql | 10.0.14 / 10.0.3 | Dostęp do bazy, migracje |
| PostgreSQL | 18.6 (obraz Docker) | Baza danych |
| Microsoft.AspNetCore.Authentication.Google | 10.0.14 | Logowanie Google |
| barcode-detector (ZXing-C++ w WebAssembly) | 3.2.3 | Odczyt EAN-13 z aparatu |
| ngrok | — | Tunel HTTPS do telefonu |
| Fakturownia (REST API) | — | Wystawianie faktur VAT |

### Dlaczego tak

- **Blazor Interactive Server zamiast React/Angular z osobnym API.** Jeden język i jeden projekt, logika tylko na serwerze, bez warstwy REST i kontraktów do pilnowania. Wadą jest stałe połączenie SignalR z każdym użytkownikiem i opóźnienie każdego kliknięcia, co dla kilku pracowników jednej firmy nie ma znaczenia. Druga wada wyszła w testach: zwykłe narzędzie HTTP (k6) nie klika w interfejs, więc testy obciążenia mierzą tylko pierwsze wczytanie strony (opis w `analiza-dynamiczna.md`).
- **MudBlazor zamiast Radzen, Fluent UI Blazor lub samego Bootstrapa.** Gotowe tabele z sortowaniem, okna dialogowe, pola liczbowe i wykresy w jednej bibliotece z aktywnym rozwojem. Wykresy `MudChart` wystarczyły, więc nie doszła osobna biblioteka wykresów. Pułapka: `MudNumericField` domyślnie czyta liczby w kulturze niezmiennej, co spowodowało błąd „2,50 → 250” (`przygody.md`).
- **PostgreSQL zamiast SQL Server lub SQLite.** Darmowy, działa w Dockerze na każdym systemie, ma `SELECT … FOR UPDATE SKIP LOCKED` (kolejka faktur), uprawnienia na poziomie kolumn (niezmienność faktur), indeksy częściowe i `CHECK`. SQLite nie ma blokad wierszy, więc testy współbieżności nie miałyby sensu.
- **EF Core zamiast Dappera.** Migracje, typowane zapytania i mniej ręcznego SQL. Tam, gdzie potrzebna była konkretna blokada, jest surowy SQL z parametrami (`FromSql`), więc EF nie zabrał kontroli.
- **Bez ASP.NET Core Identity.** Logowanie jest tylko przez Google, a lista dozwolonych adresów to jedna tabela. Identity dodałoby hasła, tabele i ekrany, których specyfikacja nie chce.
- **barcode-detector zamiast html5-qrcode lub QuaggaJS.** Implementuje standardowe API `BarcodeDetector`, więc tam, gdzie przeglądarka ma własne (Chrome na Androidzie), używa wbudowanego. html5-qrcode nie miało wydania od kwietnia 2023, QuaggaJS od lat. Pliki biblioteki są w repozytorium, a suma SHA-256 pliku WASM jest sprawdzona; nic nie jest pobierane z CDN.
- **ngrok zamiast Cloudflare Tunnel lub certyfikatu w sieci lokalnej.** Aparat w przeglądarce wymaga HTTPS, a logowanie Google wymaga stałego adresu przekierowania. Darmowy ngrok daje jedną stałą domenę bez konfiguracji DNS.
- **Fakturownia** to wymaganie projektu (faktury VAT przez zewnętrzny system). Klient jest ukryty za interfejsem `IFakturowniaClient`, więc testy nigdy nie łączą się z prawdziwym serwisem.

## Testy

| Narzędzie | Wersja | Rodzaj testów |
|---|---|---|
| xUnit | 2.9.3 | Testy jednostkowe i integracyjne |
| bUnit | 2.14.0 | Testy komponentów Blazora bez przeglądarki |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 | Sterowany zegar (`FakeTimeProvider`) |
| Testcontainers.PostgreSql | 4.16.0 | Prawdziwy PostgreSQL w kontenerze na czas testów |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | Aplikacja uruchomiona w teście (`WebApplicationFactory`) |
| Microsoft.Playwright | 1.64.1 | Testy funkcjonalne w przeglądarce (Chromium) |
| coverlet.collector | 6.0.4 | Pokrycie kodu |
| BenchmarkDotNet | 0.15.8 | Pomiar czasu zapytań analiz |
| k6 | — | Test obciążenia HTTP (skrypt w `tests/performance/k6`) |
| `EXPLAIN (ANALYZE, BUFFERS)` w PostgreSQL | 18.6 | Plany zapytań |

### Porównanie z alternatywami

| Potrzeba | Wybrane | Rozważane | Dlaczego wybrane |
|---|---|---|---|
| Szkielet testów | xUnit | NUnit, MSTest | Domyślny w szablonach .NET, osobna instancja klasy na test (brak wspólnego stanu), dobre wsparcie dla `IAsyncLifetime` i wspólnych fikstur (kontener bazy). NUnit byłby równie dobry; zdecydowała zgodność z bUnit i przykładami Testcontainers. |
| Komponenty Blazora | bUnit | Tylko Playwright | bUnit renderuje komponent w milisekundach i pozwala podmienić JavaScript (`JSInterop`). Dzięki temu odtworzono wyścig skanera i błąd przecinka bez przeglądarki. Playwright sprawdza całość, ale jest 100 razy wolniejszy. |
| Baza w testach | Testcontainers z PostgreSQL | EF Core InMemory, SQLite | InMemory i SQLite nie mają `FOR UPDATE`, `SKIP LOCKED`, uprawnień ani tych samych typów `numeric`. Test równoczesnych wydań na InMemory przechodziłby zawsze, także bez blokady, czyli niczego by nie dowodził. |
| Czas w testach | `FakeTimeProvider` | `DateTime.Now`, własny interfejs | Wbudowany w .NET `TimeProvider`, bez własnej abstrakcji. Testy dat ważności, numeracji na przełomie roku i ponowień wysyłki są deterministyczne. |
| Przeglądarka | Playwright | Selenium, bUnit | Automatyczne czekanie na elementy, jedna komenda instalacji przeglądarki także w CI, nagrywanie śladów. Selenium wymaga osobnego sterownika i ręcznego czekania, co przy Blazorze (strona staje się interaktywna chwilę po wczytaniu) dawałoby niestabilne testy. |
| Obciążenie HTTP | k6 + własny pomiar w C# | JMeter, NBomber, Locust | k6 to jeden plik wykonywalny i skrypt w JavaScripcie z progami (`thresholds`), które same oceniają wynik. JMeter jest cięższy i konfigurowany w GUI. Dodatkowo `Regal.LoadHost --measure` mierzy to samo w C#, żeby wynik dało się odtworzyć bez instalowania k6. |
| Mikrobenchmarki | BenchmarkDotNet | `Stopwatch` w pętli | Rozgrzewka, wiele iteracji, odchylenie standardowe i statystyka; ręczny pomiar myli pierwsze wywołanie (kompilacja zapytania, zimna pamięć podręczna) z typowym. |
| Plany zapytań | `EXPLAIN (ANALYZE, BUFFERS)` | pg_stat_statements, auto_explain | Wystarcza do kilku zapytań analiz i pokazuje rzeczywiste czasy i odczyty stron. Pozostałe są lepsze do długotrwałej obserwacji produkcji, której tu nie ma. |
| Pokrycie | coverlet | dotCover, Visual Studio Enterprise | Darmowy, działa z `dotnet test` i w CI, format Cobertura. |
| Debugowanie | Debugger Visual Studio/VS Code, logi `ILogger`, `dotnet-trace` | — | Opis użycia na prawdziwych błędach w `analiza-dynamiczna.md`. |

## Infrastruktura i proces

| Narzędzie | Do czego |
|---|---|
| Docker Desktop, Docker Compose | Lokalna baza PostgreSQL (wystawiona tylko na `127.0.0.1`) i kontenery testów |
| Git, GitHub (repozytorium publiczne, MIT) | Kod, gałęzie funkcji, scalanie bez przewijania (`--no-ff`) |
| GitHub Actions | CI: kompilacja Release, wszystkie testy (także w przeglądarce), adnotacje z nieudanymi testami |
| CodeQL | Statyczna analiza bezpieczeństwa kodu C# |
| Dependabot | Aktualizacje pakietów NuGet, akcji GitHuba i obrazu w `docker-compose.yml` |
| Ochrona przed wysłaniem sekretów (GitHub push protection) | Blokada wypchnięcia tokenów |
| User Secrets (.NET) | Sekrety lokalne poza repozytorium |
| DBeaver | Podgląd bazy przez połączenie tylko do odczytu |
| Claude Code | Pomoc przy kodzie, testach i dokumentacji; autor odpowiada za kod i go wyjaśnia (`wklad.md`) |
