# Regał

[Polski](#polski) · [English](#english)

## Polski

Regał to webowa aplikacja magazynowo-sprzedażowa dla małej firmy handlowej z własną produkcją. Projekt powstaje na zaliczenie na studiach i do portfolio. Zakres opisuje [SPEC.md](SPEC.md).

**Stan projektu:** gotowe są fundament (logowanie Google z listą dozwolonych adresów, dziennik działań, testy i CI), kartoteki oraz magazyn: dokumenty PZ, PW, WZ i RW, partie z datami ważności, stany, cena średnia i korekty.

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

### Testy

```powershell
dotnet test Regal.slnx
```

Testy integracyjne same uruchamiają PostgreSQL przez Testcontainers, więc Docker musi być włączony.

### Licencja

[MIT](LICENSE)

## English

Regał is a web application for warehouse and sales management in a small trading company with its own production. It is a university project and a portfolio piece. The scope is described in [SPEC.md](SPEC.md) (in Polish). The application's interface is Polish only.

**Status:** the foundation (Google sign-in with an allow-list, audit log, tests and CI), catalogs and the warehouse are done: PZ, PW, WZ and RW documents, batches with expiry dates, stock levels, average cost and corrections.

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

### Tests

```powershell
dotnet test Regal.slnx
```

Integration tests start PostgreSQL through Testcontainers, so Docker must be running.

### License

[MIT](LICENSE)
