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

### Test zakleszczenia przechodził także bez blokady

- **Objaw:** po usunięciu `FOR UPDATE` padły cztery testy współbieżności, ale test dokumentów z produktami w odwrotnej kolejności nadal był zielony.
- **Przyczyna:** test tworzył same WZ. Licznik numerów jest zwiększany przed zmianą stanów i blokuje swój wiersz do końca transakcji, więc dokumenty jednego typu nigdy nie zmieniały stanów jednocześnie.
- **Rozwiązanie:** test miesza WZ i RW, które mają osobne liczniki. Bez blokady kończy się teraz błędem `40P01: deadlock detected`. Szczegóły są w `analiza-dynamiczna.md`.

### Działająca aplikacja blokowała build

- **Objaw:** `dotnet test` w konfiguracji Debug kończył się błędem `MSB3027`: nie można skopiować `Regal.Web.exe`, bo plik jest zajęty.
- **Przyczyna:** uruchomiona aplikacja trzymała otwarty plik w `bin/Debug`.
- **Rozwiązanie:** zatrzymanie aplikacji przed budowaniem. Doraźnie wystarcza `-c Release`, który ma osobny katalog wyjściowy.

### Czyszczenie śledzenia zmian w cudzej transakcji

- **Objaw:** przy przeglądzie kodu okazało się, że odrzucenie partii z inną datą ważności wywołuje `ChangeTracker.Clear()`.
- **Przyczyna:** w transakcji własnej to nieszkodliwe, ale `PostInTransactionAsync` działa na kontekście wywołującego. Faktura z etapu 3 straciłaby śledzone encje i po cichu ich nie zapisała.
- **Rozwiązanie:** usunięcie tego wywołania. Po błędzie odłączane są tylko encje dokumentu, a test sprawdza, że transakcja wywołującego nadal działa.

### Nazwa folderu zasłoniła encję

- **Objaw:** w folderze `Features/Warehouse` nazwa `Warehouse` oznacza przestrzeń nazw, więc `Warehouse.MainWarehouseId` nie wskazuje encji.
- **Rozwiązanie:** mała klasa `MainWarehouse` z identyfikatorem magazynu, zamiast zmiany nazwy folderu ustalonej w SPEC.

### bUnit 2.x nie ma już `IRenderedFragment`

- **Objaw:** błąd `CS0246` w testach komponentów napisanych według starszych przykładów.
- **Rozwiązanie:** metody pomocnicze przyjmują `IRenderedComponent<T>`.

### Enter na kodzie „1” nic nie robił

- **Objaw:** w polu produktu „1” i Enter nie dodawał pozycji ani nie pokazywał komunikatu, a „10” i Enter działało.
- **Przyczyna:** trzy błędy naraz. Tekst krótszy niż 2 znaki nie był w ogóle wyszukiwany. Kilka wyników kończyło się ciszą. Enter korzystał z wyników wyszukiwania opóźnionego o 250 ms, więc szybki Enter, na przykład ze skanera, je anulował.
- **Rozwiązanie:** Enter sam szuka w kolejności: dokładny EAN, dokładny SKU, fragment. Skany są obsługiwane po kolei, a każdy ma swój wynik.
- **Przy okazji:** dwa razy wskazałem błędną przyczynę zgubionych skanów. W testach bUnit zdarzenia są wysyłane bez czekania na obsługę, więc drugi „skan” trafiał w przerenderowanie i ginął w samym teście. Pomogło dopiero odczytanie stanu komponentu zamiast zgadywania.

## Etap „Faktury i telefon” (9.11.2026)

### Testy wysyłki faktur przechodziły bez zabezpieczeń

- **Objaw:** po usunięciu blokady `FOR UPDATE SKIP LOCKED` albo wyszukania po `oid` testy faktur nadal przechodziły.
- **Przyczyna:** udawana Fakturownia, tak jak prawdziwa przy `oid_unique`, odrzucała duplikaty. Testy sprawdzały tylko efekt końcowy, który zapewniała już ta druga warstwa.
- **Rozwiązanie:** testy liczą żądania utworzenia, także odrzucone. Szczegóły są w `analiza-dynamiczna.md`.

### PowerShell 5.1 zepsuł polskie znaki

- **Objaw:** po zmianie nazwy pliku przez `Get-Content -Raw` i `Set-Content` komunikaty zawierały „dziaĹ‚a” zamiast „działa”, a test przestał znajdować tekst.
- **Przyczyna:** Windows PowerShell 5.1 czyta pliki UTF-8 bez BOM jako stronę kodową systemu.
- **Rozwiązanie:** pliki z polskimi znakami zapisuję narzędziem do plików, a nie przez PowerShell. Wyszukanie wzorców `Ĺ|Ä…|Ă` potwierdziło, że inne pliki nie ucierpiały.

### Atrapa wywołania JavaScript nie działała w teście

- **Objaw:** test odmowy dostępu do aparatu nie widział komunikatu o błędzie.
- **Przyczyna:** `SetupVoid("start")` w bUnit pasuje tylko do wywołania bez argumentów, a komponent przekazuje element `<video>` i referencję .NET. Wywołanie trafiało do trybu domyślnego i kończyło się sukcesem.
- **Rozwiązanie:** `SetupVoid("start", _ => true)`, czyli dopasowanie po samej nazwie.

### Cudzysłowy w atrybucie komponentu Razor

- **Objaw:** dziesiątki błędów kompilacji, z których wynikało, że blok `@code` znalazł się poza klasą.
- **Przyczyna:** wyrażenie `$"..."` z cudzysłowami wewnątrz atrybutu `ToStringFunc="..."` kończyło atrybut za wcześnie.
- **Rozwiązanie:** wyrażenie przeniesione do metody.

### Biblioteka skanera pobierała WASM z CDN

- **Objaw:** domyślnie `barcode-detector` ładuje `zxing_reader.wasm` z jsDelivr.
- **Rozwiązanie:** pliki skopiowane do `wwwroot/lib`, a `locateFile` wskazuje na nie. Suma SHA-256 pliku WASM zgadza się z `ZXING_WASM_SHA256` wbudowaną w bibliotekę. Pochodzenie plików opisuje `VENDOR.md`.

### CI nie uruchamiało się na gałęziach

- **Objaw:** po wypchnięciu `feature/invoices` na GitHubie nie było przebiegu CI.
- **Przyczyna:** workflow uruchamia się tylko dla pull requestów i wypchnięć na `main`, a bez zalogowanego `gh` sesja nie zakłada PR.
- **Rozwiązanie:** przed scaleniem pełny zestaw testów lokalnie w konfiguracji Release, a zaraz po scaleniu sprawdzenie wyniku CI na `main` przez publiczne API GitHuba.

## Etap „Testy i oddanie” (10.11.2026)

### Cena „2,50” zapisywała się jako 250

- **Objaw:** test w przeglądarce (Playwright) po dwóch przyjęciach po 2,50 i 3,10 zł dostał cenę średnią 270,0000 zamiast 2,7000. Liczby całkowite działały, a testy jednostkowe i integracyjne były zielone.
- **Przyczyna:** `MudNumericField` w MudBlazor 9 czyta liczby w kulturze niezmiennej (`InvariantCulture`), chyba że poda się `Culture`, `Format` albo `Pattern`. Przecinek był więc separatorem tysięcy. Pole ceny w kartotece produktów ma `Format="F2"`, dlatego tam błąd się nie pokazał. Testy bUnit wpisywały liczby całkowite, a testy serwisów nie przechodzą przez pole formularza.
- **Rozwiązanie:** każde pole liczbowe dostało `Culture="CultureInfo.CurrentCulture"`, czyli polską kulturę ustawianą przez `UseRequestLocalization`. Test bUnit z polską kulturą wpisuje „2,50”. Bez poprawki daje 250, z poprawką 2,50.
- **Wniosek:** to błąd, który znalazły dopiero testy funkcjonalne z danymi takimi, jakie wpisze człowiek. Opis jest też w `analiza-dynamiczna.md` (debugowanie).

### Testy w przeglądarce nie widziały plików Blazora

- **Objaw:** strona w teście nie stawała się interaktywna. Przeglądarka dostawała błąd 500 dla `blazor.web.js` i plików MudBlazor.
- **Przyczyna:** statyczne pliki z pakietów i frameworka są mapowane z wyników kompilacji automatycznie tylko w środowisku Development, a host testowy działa jako „Testing”.
- **Rozwiązanie:** `UseStaticWebAssets()` w hoście testowym. Przy okazji `UseKestrel()` bez portu słuchało na stałym porcie 5000, więc host używa portu 0, który system przydziela dynamicznie.

### Metoda pomocnicza zasłonięta przez Playwright

- **Objaw:** `Unexpected token "(" while parsing css selector "Ilość (szt)"`.
- **Przyczyna:** moja metoda rozszerzająca `TypeAsync(label, text)` miała tę samą sygnaturę co przestarzała metoda `IPage.TypeAsync(selector, text)`, a metoda instancji zawsze wygrywa z rozszerzającą.
- **Rozwiązanie:** zmiana nazwy na `FillFieldAsync`.

## Etap „Analizy” (9.11.2026)

### Generator danych bez skrótów

- **Problem:** najprościej byłoby wstawić wiersze bezpośrednio do tabel, ale wtedy stany, średnie, partie i numeracja mogłyby być niespójne z regułami aplikacji, a testy wydajności mierzyłyby nierealne dane.
- **Rozwiązanie:** generator korzysta z tych samych serwisów co ekrany, z zegarem przesuwanym dzień po dniu. Kosztem jest czas: rok z 4 fakturami dziennie trwa kilkadziesiąt sekund, a duży zestaw do testów wydajności kilkanaście minut.
- **Uwaga:** faktury z generatora nie mogą trafić do Fakturowni, więc od razu dostają numer `DEMO` i nie mają zadania wysyłki. Generator ma własny klient Fakturowni, który zawsze odmawia połączenia.
