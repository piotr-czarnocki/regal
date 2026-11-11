# Regał

[Polski](#polski) · [English](#english)

## Polski

Regał to webowa aplikacja magazynowo-sprzedażowa dla małej firmy handlowej z własną produkcją. Projekt powstaje na zaliczenie na studiach i do portfolio. Zakres opisuje [SPEC.md](SPEC.md).

**Stan projektu:** gotowe są fundament (logowanie Google z listą dozwolonych adresów, dziennik działań, testy i CI), kartoteki, magazyn (dokumenty PZ, PW, WZ i RW, partie z datami ważności, stany, cena średnia i korekty), faktury wysyłane do Fakturowni z automatycznym WZ, skanowanie kodów czytnikiem USB i aparatem telefonu, pulpit z analizami (sprzedaż i marża, ABC, towary zalegające), generator przykładowych danych, a z poziomu 2 zamówienia klientów z rezerwacją towaru, proformy i faktury korygujące, wysyłka faktur mailem ze statusem płatności, inwentaryzacja ze skanowaniem, dane firmy po NIP-ie z białej listy VAT, analizy klientów, porównanie okresów i prognoza zamówień, eksport analiz do Excela oraz etykiety z kodami w PDF — cały poziom 2 specyfikacji.

### Wymagania

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Docker z Docker Compose
- Klient OAuth 2.0 w [Google Cloud](https://console.cloud.google.com/apis/credentials) (typ „Aplikacja internetowa”) z adresem przekierowania `https://localhost:7180/signin-google`

### Uruchomienie

1. Przygotuj sekrety bazy. Skopiuj `.env.example` do `.env` i wpisz w nim dwa różne, mocne hasła:

   ```powershell
   Copy-Item .env.example .env
   ```

2. Uruchom bazę:

   ```powershell
   docker compose up -d --wait
   ```

3. Zapisz konfigurację w User Secrets, poza repozytorium. Zastąp wartości w nawiasach ostrych:

   ```powershell
   dotnet user-secrets --project src/Regal.Web set "ConnectionStrings:Migrations" "Host=127.0.0.1;Database=regal;Username=regal_owner;Password=<POSTGRES_PASSWORD>"
   dotnet user-secrets --project src/Regal.Web set "ConnectionStrings:Regal" "Host=127.0.0.1;Database=regal;Username=regal_app;Password=<REGAL_APP_PASSWORD>"
   dotnet user-secrets --project src/Regal.Web set "Authentication:Google:ClientId" "<client id>"
   dotnet user-secrets --project src/Regal.Web set "Authentication:Google:ClientSecret" "<client secret>"
   dotnet user-secrets --project src/Regal.Web set "Regal:InitialAdminEmail" "<twój adres Gmail>"
   ```

4. Utwórz tabele i uruchom aplikację:

   ```powershell
   dotnet tool restore
   dotnet ef database update --project src/Regal.Web
   dotnet dev-certs https --trust
   dotnet run --project src/Regal.Web --launch-profile https
   ```

Aplikacja działa pod adresem <https://localhost:7180>.

Po pobraniu nowych zmian z repozytorium zaktualizuj bazę, zanim uruchomisz aplikację:

```powershell
dotnet ef database update --project src/Regal.Web
```

### Fakturownia

Bez tej konfiguracji faktury czekają w kolejce, a reszta aplikacji działa normalnie.

1. Załóż konto testowe w Fakturowni, bez włączonej integracji z KSeF.
2. Skopiuj kod autoryzacyjny API (Ustawienia → Ustawienia konta → Integracja → Kod autoryzacyjny API).
3. Zapisz domenę (część przed `.fakturownia.pl`) i token w User Secrets:

   ```powershell
   dotnet user-secrets --project src/Regal.Web set "Fakturownia:Domain" "<twoja-domena>"
   dotnet user-secrets --project src/Regal.Web set "Fakturownia:ApiToken" "<token>"
   ```

Token trafia wyłącznie do nagłówka `Authorization`, nigdy do adresu ani do logów.

### Telefon i skanowanie aparatem

Aparat w przeglądarce działa tylko przez HTTPS, dlatego telefon łączy się przez tunel ngrok.

1. Załóż konto ngrok, zapisz jego token poleceniem `ngrok config add-authtoken <token>` i zarezerwuj w panelu darmową stałą domenę.
2. W Google Cloud, w kliencie OAuth, dodaj adres przekierowania `https://<domena-ngrok>/signin-google`.
3. Uruchom aplikację w profilu tunelu i tunel w drugim oknie:

   ```powershell
   dotnet run --project src/Regal.Web --launch-profile tunnel
   ngrok http 5180 --url https://<domena-ngrok>
   ```

4. Na telefonie otwórz `https://<domena-ngrok>`. Przycisk aparatu jest przy polu produktu i przy wyszukiwarkach.

Tunel uruchamiaj tylko na czas testów: aplikacja jest wtedy dostępna z internetu (za logowaniem Google). Profil `tunnel` ufa nagłówkom przekazywanym wyłącznie z tego komputera.

### Przykładowe dane

Generator tworzy około roku wymyślonej działalności: produkty, kontrahentów, dokumenty magazynowe i faktury. Faktury nie są wysyłane do Fakturowni. Działa tylko w środowisku `Development` lub `Testing`, z tą samą bazą co aplikacja. Uruchamiaj go przy zatrzymanej aplikacji:

```powershell
dotnet run --project tools/Regal.DataGenerator -- --environment Development
```

Pozostałe parametry: `--products`, `--customers`, `--suppliers`, `--days`, `--invoices-per-day`, `--seed`, `--prefix`. Przed zapisem generator wypisuje bazę docelową; sprawdź ją. Duży zestaw do testów wydajności generuj w osobnej bazie (niżej), nie w deweloperskiej.

### Testy

```powershell
dotnet test Regal.slnx
```

Testy integracyjne i testy w przeglądarce same uruchamiają PostgreSQL przez Testcontainers, więc Docker musi być włączony. Testy w przeglądarce przy pierwszym uruchomieniu same pobierają Chromium (potrzebny internet); na Linuksie biblioteki systemowe przeglądarki trzeba doinstalować tak jak w CI (`playwright.ps1 install --with-deps chromium`).

### Testy wydajności

Wymagają osobnej, jednorazowej bazy z rokiem danych. Kontener słucha tylko na `127.0.0.1` i nie ma haseł (`trust`), więc nie używaj go do niczego innego:

```powershell
docker run -d --name regal-perf -p 127.0.0.1:55432:5432 -e POSTGRES_DB=regal -e POSTGRES_USER=regal_owner `
  -e POSTGRES_HOST_AUTH_METHOD=trust -e REGAL_APP_PASSWORD=not-used-with-trust `
  -v "${PWD}/docker/postgres/init:/docker-entrypoint-initdb.d:ro" postgres:18.6
dotnet ef database update --project src/Regal.Web --connection "Host=127.0.0.1;Port=55432;Database=regal;Username=regal_owner"
$env:ConnectionStrings__Regal = "Host=127.0.0.1;Port=55432;Database=regal;Username=regal_app"
dotnet run --project tools/Regal.DataGenerator -c Release -- --environment Testing --products 10000 --customers 400 --suppliers 20 --invoices-per-day 100 --seed 2026 --prefix PERF
```

Generowanie trwa około pół godziny. Potem:

```powershell
$env:REGAL_PERF_CONNECTION = "Host=127.0.0.1;Port=55432;Database=regal;Username=regal_app"
dotnet run -c Release --project tests/performance/Regal.Benchmarks -- --filter *        # zapytania analiz
dotnet run -c Release --project tests/performance/Regal.Benchmarks -- --sql             # ich SQL do EXPLAIN
dotnet run -c Release --project tests/performance/Regal.LoadHost -- --measure 20 120    # 20 użytkowników, 120 s
```

Ten sam test obciążenia w k6: uruchom `dotnet run -c Release --project tests/performance/Regal.LoadHost`, a w drugim oknie `k6 run -e BASE_URL=<adres> -e USER_ID=<id> tests/performance/k6/pages.js` z wartościami, które wypisze host. Wyniki i ich omówienie są w [docs/analiza-dynamiczna.md](docs/analiza-dynamiczna.md).

### Dokumentacja

| Plik | Treść |
|---|---|
| [SPEC.md](SPEC.md) | Specyfikacja i decyzje |
| [docs/funkcjonalnosci.md](docs/funkcjonalnosci.md) | Opis funkcjonalności |
| [docs/narzedzia.md](docs/narzedzia.md) | Narzędzia i technologie, uzasadnienie wyboru |
| [docs/analiza-dynamiczna.md](docs/analiza-dynamiczna.md) | Testy, pokrycie, wydajność, debugowanie |
| [docs/scenariusze-testowe.md](docs/scenariusze-testowe.md) | Scenariusze testów ręcznych |
| [docs/przygody.md](docs/przygody.md) | Napotkane problemy |
| [docs/wklad.md](docs/wklad.md) | Wkład autora i czego się nauczył |
| [docs/prezentacja/](docs/prezentacja/) | Konspekt slajdów i scenariusz pokazu |

### Licencja

[MIT](LICENSE)

## English

Regał is a web application for warehouse and sales management in a small trading company with its own production. It is a university project and a portfolio piece. The scope is described in [SPEC.md](SPEC.md) (in Polish). The application's interface is Polish only.

**Status:** done are the foundation (Google sign-in with an allow-list, audit log, tests and CI), catalogs, the warehouse (PZ, PW, WZ and RW documents, batches with expiry dates, stock levels, average cost and corrections), invoices sent to Fakturownia with an automatic WZ, barcode scanning with a USB reader or a phone camera, a dashboard with analytics (sales and margin, ABC, slow-moving goods), a sample data generator and, from level 2, customer orders that reserve goods, proforma and correcting invoices, invoices emailed with their payment status followed, stocktaking by scanning, company details fetched by tax number from the VAT register, customer analytics, period comparison, an order forecast, Excel export of the analyses, and barcode labels in PDF — all of level 2 of the specification.

### Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Docker with Docker Compose
- An OAuth 2.0 client in [Google Cloud](https://console.cloud.google.com/apis/credentials) (type "Web application") with the redirect URI `https://localhost:7180/signin-google`

### Getting started

1. Prepare the database secrets. Copy `.env.example` to `.env` and set two different, strong passwords:

   ```powershell
   Copy-Item .env.example .env
   ```

2. Start the database:

   ```powershell
   docker compose up -d --wait
   ```

3. Store the configuration in User Secrets, outside the repository. Replace the values in angle brackets:

   ```powershell
   dotnet user-secrets --project src/Regal.Web set "ConnectionStrings:Migrations" "Host=127.0.0.1;Database=regal;Username=regal_owner;Password=<POSTGRES_PASSWORD>"
   dotnet user-secrets --project src/Regal.Web set "ConnectionStrings:Regal" "Host=127.0.0.1;Database=regal;Username=regal_app;Password=<REGAL_APP_PASSWORD>"
   dotnet user-secrets --project src/Regal.Web set "Authentication:Google:ClientId" "<client id>"
   dotnet user-secrets --project src/Regal.Web set "Authentication:Google:ClientSecret" "<client secret>"
   dotnet user-secrets --project src/Regal.Web set "Regal:InitialAdminEmail" "<your Gmail address>"
   ```

4. Create the tables and run the application:

   ```powershell
   dotnet tool restore
   dotnet ef database update --project src/Regal.Web
   dotnet dev-certs https --trust
   dotnet run --project src/Regal.Web --launch-profile https
   ```

The application is available at <https://localhost:7180>.

After pulling new changes, update the database before starting the application:

```powershell
dotnet ef database update --project src/Regal.Web
```

### Fakturownia

Without this configuration invoices wait in the queue and the rest of the application works normally.

1. Create a Fakturownia test account without the KSeF integration.
2. Copy the API authorization code (Settings → Account settings → Integration → API authorization code).
3. Store the domain (the part before `.fakturownia.pl`) and the token in User Secrets:

   ```powershell
   dotnet user-secrets --project src/Regal.Web set "Fakturownia:Domain" "<your-domain>"
   dotnet user-secrets --project src/Regal.Web set "Fakturownia:ApiToken" "<token>"
   ```

The token is sent only in the `Authorization` header, never in a URL or a log.

### Phone and camera scanning

The browser camera works only over HTTPS, so the phone connects through an ngrok tunnel.

1. Create an ngrok account, store its token with `ngrok config add-authtoken <token>` and reserve the free static domain in the dashboard.
2. In Google Cloud, add the redirect URI `https://<ngrok-domain>/signin-google` to the OAuth client.
3. Start the application with the tunnel profile and the tunnel in a second window:

   ```powershell
   dotnet run --project src/Regal.Web --launch-profile tunnel
   ngrok http 5180 --url https://<ngrok-domain>
   ```

4. On the phone, open `https://<ngrok-domain>`. The camera button is next to the product field and the searches.

Run the tunnel only while testing: the application is then reachable from the internet (behind Google sign-in). The `tunnel` profile trusts forwarded headers only from this machine.

### Sample data

The generator creates about a year of made-up trade: products, contractors, stock documents and invoices. The invoices are not sent to Fakturownia. It runs only in the `Development` or `Testing` environment, against the application's database. Run it with the application stopped:

```powershell
dotnet run --project tools/Regal.DataGenerator -- --environment Development
```

Other options: `--products`, `--customers`, `--suppliers`, `--days`, `--invoices-per-day`, `--seed`, `--prefix`. Before writing, the generator prints the target database; check it. Generate the large performance data set in a separate database (below), not in the development one.

### Tests

```powershell
dotnet test Regal.slnx
```

Integration and browser tests start PostgreSQL through Testcontainers, so Docker must be running. Browser tests download Chromium themselves on the first run (internet needed); on Linux, install the browser's system libraries as CI does (`playwright.ps1 install --with-deps chromium`).

### Performance tests

They need a separate, throwaway database with a year of data. The container listens only on `127.0.0.1` and has no passwords (`trust`), so do not use it for anything else:

```powershell
docker run -d --name regal-perf -p 127.0.0.1:55432:5432 -e POSTGRES_DB=regal -e POSTGRES_USER=regal_owner `
  -e POSTGRES_HOST_AUTH_METHOD=trust -e REGAL_APP_PASSWORD=not-used-with-trust `
  -v "${PWD}/docker/postgres/init:/docker-entrypoint-initdb.d:ro" postgres:18.6
dotnet ef database update --project src/Regal.Web --connection "Host=127.0.0.1;Port=55432;Database=regal;Username=regal_owner"
$env:ConnectionStrings__Regal = "Host=127.0.0.1;Port=55432;Database=regal;Username=regal_app"
dotnet run --project tools/Regal.DataGenerator -c Release -- --environment Testing --products 10000 --customers 400 --suppliers 20 --invoices-per-day 100 --seed 2026 --prefix PERF
```

Generating takes about half an hour. Then:

```powershell
$env:REGAL_PERF_CONNECTION = "Host=127.0.0.1;Port=55432;Database=regal;Username=regal_app"
dotnet run -c Release --project tests/performance/Regal.Benchmarks -- --filter *        # analysis queries
dotnet run -c Release --project tests/performance/Regal.Benchmarks -- --sql             # their SQL, for EXPLAIN
dotnet run -c Release --project tests/performance/Regal.LoadHost -- --measure 20 120    # 20 users, 120 s
```

The same load test in k6: start `dotnet run -c Release --project tests/performance/Regal.LoadHost`, and in a second window run `k6 run -e BASE_URL=<address> -e USER_ID=<id> tests/performance/k6/pages.js` with the values the host prints. Results and their discussion are in [docs/analiza-dynamiczna.md](docs/analiza-dynamiczna.md) (in Polish).

### Documentation (in Polish)

[SPEC.md](SPEC.md) (specification), [features](docs/funkcjonalnosci.md), [tools](docs/narzedzia.md), [dynamic analysis](docs/analiza-dynamiczna.md), [test scenarios](docs/scenariusze-testowe.md), [problems met](docs/przygody.md), [contribution](docs/wklad.md), [presentation](docs/prezentacja/).

### License

[MIT](LICENSE)
