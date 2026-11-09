# Regał

[Polski](#polski) · [English](#english)

## Polski

Regał to webowa aplikacja magazynowo-sprzedażowa dla małej firmy handlowej z własną produkcją. Projekt powstaje na zaliczenie na studiach i do portfolio. Zakres opisuje [SPEC.md](SPEC.md).

**Stan projektu:** gotowe są fundament (logowanie Google z listą dozwolonych adresów, dziennik działań, testy i CI), kartoteki, magazyn (dokumenty PZ, PW, WZ i RW, partie z datami ważności, stany, cena średnia i korekty), faktury wysyłane do Fakturowni z automatycznym WZ oraz skanowanie kodów czytnikiem USB i aparatem telefonu.

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

### Testy

```powershell
dotnet test Regal.slnx
```

Testy integracyjne same uruchamiają PostgreSQL przez Testcontainers, więc Docker musi być włączony.

### Licencja

[MIT](LICENSE)

## English

Regał is a web application for warehouse and sales management in a small trading company with its own production. It is a university project and a portfolio piece. The scope is described in [SPEC.md](SPEC.md) (in Polish). The application's interface is Polish only.

**Status:** done are the foundation (Google sign-in with an allow-list, audit log, tests and CI), catalogs, the warehouse (PZ, PW, WZ and RW documents, batches with expiry dates, stock levels, average cost and corrections), invoices sent to Fakturownia with an automatic WZ, and barcode scanning with a USB reader or a phone camera.

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

### Tests

```powershell
dotnet test Regal.slnx
```

Integration tests start PostgreSQL through Testcontainers, so Docker must be running.

### License

[MIT](LICENSE)
