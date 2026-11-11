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

### Generator zapisał dane do bazy deweloperskiej zamiast do testowej

- **Objaw:** generator uruchomiony dla osobnej bazy wydajnościowej (zmienna `ConnectionStrings__Regal`) zostawił ją pustą i zakończył się błędem `relation "Invoices" does not exist`.
- **Przyczyna:** narzędzie dodawało User Secrets projektu webowego po domyślnych źródłach konfiguracji, więc miały one najwyższy priorytet. Generator połączył się z lokalną bazą deweloperską z User Secrets i zdążył w niej utworzyć produkty `PERF-…`, kontrahentów „(PERF n)”, użytkownika `dane-testowe@regal.invalid` oraz dokumenty przyjęcia z pierwszego dnia symulacji. Przerwał przy pierwszej fakturze, bo ta baza nie miała jeszcze migracji `AddInvoices`.
- **Rozwiązanie:** konfiguracja generatora jest budowana ręcznie w kolejności: wiersz poleceń, zmienne środowiskowe, User Secrets. Przed zapisem generator wypisuje bazę docelową (host, port, nazwa, użytkownik, bez hasła). Danych w bazie deweloperskiej nie da się usunąć rolą aplikacji, bo dokumenty są niezmienne; sprzątanie wymaga roli właściciela albo wyczyszczenia bazy deweloperskiej.
- **Wniosek:** narzędzie, które pisze do bazy, powinno pokazać, dokąd pisze, zanim cokolwiek zrobi. Kolejność źródeł konfiguracji trzeba ustalać jawnie, a nie zakładać.

### Testy w przeglądarce raz przechodziły, raz nie

- **Objaw:** w pełnym zestawie testów (wszystkie projekty naraz) w 2 z 4 przebiegów padały 1–2 testy przeglądarkowe: przyjęcie towaru i wydanie FEFO. Uruchomione osobno przechodziły zawsze.
- **Ślad:** raport TRX pokazał `Timeout 15000ms exceeded … waiting for GetByLabel("Ilość (szt)")`, a zrzut ekranu z testu FEFO: „7” w polu produktu i ilość nadal 1. Tekst przeznaczony dla pola ilości trafił do pola produktu.
- **Przyczyna:** po dodaniu pozycji pole produktu przywraca sobie fokus wywołaniem JavaScript z serwera. Nowa pozycja renderuje się wcześniej, a wywołanie fokusu przychodzi osobno. Gdy komputer był obciążony testami integracyjnymi, test zdążył zacząć wpisywanie w pole ilości, zanim fokus wrócił do pola produktu. Playwright ustawia fokus i wstawia tekst w dwóch krokach, więc tekst wylądował tam, gdzie akurat był fokus. W teście przyjęcia podobnie: kod wpisywany zaraz po wyborze dostawcy trafiał do pola dostawcy, które jeszcze kończyło wybór.
- **Rozwiązanie:** pole produktu ma licznik `data-settled`, zwiększany dopiero po zakończeniu wywołania fokusu, i test czeka na jego zmianę. Wybór z listy podpowiedzi czeka, aż lista się zamknie, a pole pokaże wybraną wartość. Nowy test bUnit sprawdza, że licznik nie zmienia się, dopóki fokus nie wrócił; z licznikiem przesuniętym przed wywołanie fokusu ten test pada.
- **Wniosek:** test przeglądarkowy musi czekać na stan, który widzi człowiek (tu: kursor wrócił do pola), a nie tylko na pojawienie się elementu. Ten sam wyścig może dotknąć użytkownika telefonu przy dużych opóźnieniach tunelu: kto kliknie w ilość, zanim kursor wróci do pola produktu, może zobaczyć, że fokus mu „uciekł”.

### Raport zalegających zatykał wszystkie strony pod obciążeniem

- **Objaw:** przy 20 użytkownikach p95 wszystkich stron wynosił 1905 ms, a pulpit miał medianę 1,7 s. W benchmarku raport zalegających trwał 586 ms, czyli mieścił się w celu „poniżej 1 s”.
- **Przyczyna:** stan i data ostatniej sprzedaży były w LINQ właściwościami każdego produktu. EF Core zrobił z nich podzapytania skorelowane i powtórzył je w filtrze i sortowaniu, więc baza wykonywała je dziesiątki tysięcy razy (830 tysięcy odczytów stron). Pod obciążeniem równoczesne kopie zajmowały procesory bazy.
- **Rozwiązanie:** dwa zestawienia `GROUP BY` złączone z produktami. Za pierwszym razem EF nadal robił podzapytanie, bo w `Max` była nawigacja; pomogło jawne złączenie. Zapytanie trwa 37 ms, a p95 stron 286 ms. Wynik porównałem wiersz po wierszu ze starym.

### Test obciążenia mierzył odśmiecacz zamiast aplikacji

- **Objaw:** strona ABC miała p95 1,5 s, choć jej zapytanie trwa 44 ms, a dwa przebiegi tego samego testu różniły się o 130 ms.
- **Przyczyna:** `dotnet-counters` pokazał, że przerwy GC zajmują 30% czasu. Host testu obciążenia jest aplikacją konsolową, więc działał z Workstation GC, a prawdziwa aplikacja (SDK webowe) ma Server GC.
- **Rozwiązanie:** `ServerGarbageCollection=true` w projekcie hosta. Przerwy GC zajmują teraz 1,5% czasu.

### Host testu obciążenia nie startował przez `dotnet run`

- **Objaw:** `DirectoryNotFoundException: C:\Users\piotr\projekty\regal\Regal.Web\`, choć projekt jest w `src/Regal.Web`, a testy integracyjne z tym samym hostem działały.
- **Przyczyna:** `WebApplicationFactory` szuka pliku `MvcTestingAppManifest.json` w bieżącym katalogu. Runner testów startuje w katalogu wyjściowym, gdzie ten plik jest, a `dotnet run` w katalogu repozytorium. Bez pliku fabryka zgaduje `<katalog rozwiązania>/<nazwa projektu>`.
- **Rozwiązanie:** host przed startem przechodzi do `AppContext.BaseDirectory`. Przyczynę ustaliłem z kodu źródłowego `WebApplicationFactory`, bo komunikat błędu wskazywał tylko na zły katalog.

### Przeterminowany towar zalegał w dużym zestawie danych

- **Objaw:** generator miał dać około 100 faktur dziennie, a dał 12 206 faktur w roku, czyli około 33 dziennie, i 54 640 ruchów zamiast ponad 100 000. Nie zgłosił żadnego błędu.
- **Ślad:** zapytanie do bazy wydajnościowej pokazało 4545 partii po dacie ważności, które nadal miały stan. Liczba nie spisanych partii rosła z tygodnia na tydzień. Dokumentów RW było 9722, czyli prawie dokładnie 200 na tydzień.
- **Przyczyna:** cotygodniowe spisanie przeterminowanego towaru brało najwyżej 200 partii (jedna strona zapytania) i dawało każdą na osobne RW. Przy 30 produktach z testów to wystarczało. Przy 10 000 produktów wygasało więcej partii, niż spisywano. Generator nadal liczył ten towar jako dostępny, więc wybierał go do faktur, a serwis słusznie odmawiał wydania przeterminowanej partii. Odmowę generator traktował jak zwykły brak towaru. Przy okazji wyszło, że strona była pobierana bez `ORDER BY`, więc wybór partii zależał od planu zapytania, a nie od seeda.
- **Rozwiązanie:** spisanie czyta kolejne strony w stałej kolejności, aż nic nie zostanie, i daje do 200 produktów na jeden RW. Wielkość strony jest parametrem, dzięki czemu test integracyjny pokazuje ten przypadek na 300 produktach ze stroną 5. Przed poprawką test zostawiał 53 przeterminowane partie, po poprawce zero.
- **Wniosek:** generator danych też jest programem i trzeba go testować na danych takich, do jakich służy. Ciche „pominięcie” nieudanej operacji ukryło błąd, który był widoczny dopiero w liczbach.

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

## Poziom 2: zamówienia klientów (10.11.2026)

### Test kodów „1” i „10” padał raz na kilkanaście przebiegów

- **Objaw:** `Overlapping_codes_1_and_10_and_a_full_ean_each_add_their_own_product` raz nie przeszedł, a w kolejnych 9 przebiegach był zielony. Padał szybko, w 73 ms.
- **Przyczyna:** test sprawdzał `Assert.DoesNotContain("1212", page.Markup)`, czyli szukał nazwy drugiego produktu w całym HTML strony. Tymczasowy test wypisujący atrybuty `id` pokazał, że MudBlazor nadaje elementom losowe identyfikatory, m.in. GUID w `popover-38d3a56b-…`. Taki identyfikator czasem zawiera „1212”.
- **Rozwiązanie:** test porównuje dokładną listę nazw pozycji dokumentu (`["BOGDAN"]`, potem `["BOGDAN", "1212"]`). To mocniejsze sprawdzenie niż brak tekstu w całym HTML i nie zależy od losowych identyfikatorów. Pozostałe takie asercje w testach szukają polskich słów albo unikalnych nazw, których w identyfikatorach być nie może.
- **Wniosek:** asercja „czegoś nie ma na stronie” musi dotyczyć konkretnego miejsca, a nie całego dokumentu.

### Faktura z zamówienia pokazywała ilość „8,000”

- **Objaw:** w teście w przeglądarce pole ilości faktury utworzonej z zamówienia miało wartość „8,000” zamiast „8”.
- **Przyczyna:** PostgreSQL zwraca kolumnę `numeric(18,3)` z trzema miejscami po przecinku, a `decimal` w .NET pamięta tę skalę (8.000 to nie to samo co 8 przy wyświetlaniu).
- **Rozwiązanie:** ilości z zamówienia są normalizowane przy odczycie (dzielenie przez `1.000…0m` usuwa zbędne zera).

## Poziom 2: proforma i faktura korygująca (10.11.2026)

### Pozycja korekty nie mieściła się w ograniczeniu bazy

- **Problem:** pozycje faktur mają w bazie `CHECK (Quantity > 0)`, a pozycja korekty zapisuje różnicę ilości, która jest ujemna albo zerowa (korekta samej ceny).
- **Rozwiązanie:** pozycja korekty ma powiązanie z pozycją faktury (`CorrectsLineId`), a ograniczenie wymaga ilości dodatniej od zwykłych pozycji i niedodatniej od pozycji korekty. Gwarancja dla zwykłych faktur została, a korekta nie może w bazie dodać ilości. Sprawdza to test, który próbuje zapisać taką pozycję z pominięciem serwisu.

### Test blokady przechodził bez blokady

- **Objaw:** test równoległych korekt jednej faktury przechodził także po usunięciu `FOR UPDATE` na korygowanej fakturze.
- **Przyczyna:** korekta ze zwrotem towaru przechodzi przez korektę WZ, a ta blokuje wiersze produktów. To ta blokada ustawiała korekty w kolejce.
- **Rozwiązanie:** drugi test, dla korekty samej ceny, gdzie żaden produkt nie jest blokowany. Bez `FOR UPDATE` pada 5 na 5 razy z błędem unikalnego indeksu (opis w `analiza-dynamiczna.md`, eksperyment 4).

### Nowy test raz przechodził, raz nie

- **Objaw:** test równoległych korekt ceny przechodził osobno, a w pełnym zestawie czasem nie.
- **Przyczyna:** raport TRX pokazał komunikat „Korekta niczego nie zmienia” zamiast „ma już korektę”. Szkice miały ceny 15–20 zł, a faktura 20 zł, więc jeden szkic nic nie zmieniał. Gdy to on pierwszy dostał blokadę, zwracał inny komunikat. Błąd był w danych testu, a od kolejności wątków zależało tylko, czy wyjdzie na jaw.
- **Rozwiązanie:** ceny 10–15 zł.

### Ostrzeżenie analizatora ukryte przez kompilację przyrostową

- **Objaw:** po pełnej przebudowie (`--no-incremental`) pojawiło się ostrzeżenie `MUD0012` w teście stron zamówień, choć wcześniejsze kompilacje pokazywały 0 ostrzeżeń.
- **Przyczyna:** kompilacja przyrostowa nie budowała ponownie projektu testów, więc nie uruchamiała analizatora MudBlazor, który zabrania czytania parametru `Value` komponentu z zewnątrz.
- **Rozwiązanie:** test czyta wartość z pola `input`, jak inne testy. Przed scaleniem sprawdzam teraz liczbę ostrzeżeń po pełnej przebudowie.

### Docker Desktop przestał działać w trakcie testów

- **Objaw:** wszystkie 200 testów integracyjnych padło w 1 ms z `DockerApiException: … InternalServerError`.
- **Przyczyna:** Docker Desktop zgłaszał „Docker Desktop is unable to start”, a dystrybucja WSL `docker-desktop` była zatrzymana.
- **Rozwiązanie:** `docker desktop restart`. Wolumeny zostały nienaruszone, a baza deweloperska wystartowała sama. Wniosek: gdy padają wszystkie testy naraz i w 1 ms, przyczyną jest środowisko, a nie kod.

## Poziom 2: inwentaryzacja (10.11.2026)

### Blokady, której nie dało się złapać wyścigiem

- **Objaw:** po usunięciu blokady wszystkich produktów arkusza przechodziły i test zgodności stanów, i losowy wyścig zatwierdzeń z dokumentami WZ (8 par, 5 przebiegów).
- **Przyczyna:** test zgodności sprawdzał liczby, a te są poprawne także bez blokady. Wyścig trafiał w okno kilku milisekund, a dwa zatwierdzenia i tak szły po kolei przez licznik numerów INW.
- **Rozwiązanie:** test deterministyczny. Test sam blokuje produkt X, uruchamia zatwierdzenie i prosi o produkt Y. Bez blokady z góry baza zgłasza zakleszczenie 5 na 5 razy (`analiza-dynamiczna.md`, eksperyment 5).

### Ten sam błąd w teście drugi raz tego samego dnia

- **Objaw:** test strony inwentaryzacji przechodził pierwsze asercje (`"-4"`, `"-3"` w HTML), a potem nie znajdował pola ilości, bo pozycji jeszcze nie było.
- **Przyczyna:** jak przy teście kodów „1” i „10”: GUID-y w losowych identyfikatorach MudBlazor zawierają fragmenty w rodzaju „-4”, więc asercja na całym HTML przechodziła od razu.
- **Rozwiązanie:** test czyta komórki kolumny „Różnica” (`td[data-label="Różnica"]`). Wniosek z poprzedniego wpisu trzeba stosować przy pisaniu nowych testów, nie tylko przy naprawie starych.

### Drugi skan ginął w teście strony arkusza

- **Objaw:** test strony arkusza czasem kończył się różnicą −4 zamiast −3, tylko w pełnym zestawie testów. Raport TRX: „Check count: 1”, czyli po drugim skanie strona nie przerenderowała się ani razu.
- **Przyczyna:** bUnit wysyła zdarzenia bez czekania na ich obsługę. Drugi skan trafiał w trwającą obsługę pierwszego (zapis w bazie i odświeżenie arkusza) i ginął w samym teście. To ten sam efekt, który wcześniej dwa razy zmylił przy błędzie z Enterem.
- **Rozwiązanie:** pomocnicza metoda skanu czeka na zmianę licznika `data-settled` pola produktu, tego samego, na który czekają testy w przeglądarce. Oczekiwanie na operacje na bazie wydłużone do 10 s.

### Test zielony lokalnie, czerwony w CI

- **Objaw:** po scaleniu danych po NIP-ie CI zgłosiło błąd starego testu `Inactive_product_is_not_added_by_ean_or_by_fragment` (strona nowego dokumentu, bUnit): „The assertion did not pass within the timeout period”. Lokalnie przechodził.
- **Przyczyna:** testy stron czekają na odświeżenie, które wymaga zapytań do bazy, a domyślny limit oczekiwania bUnit to 1 s. Maszyna CI jest wolniejsza, a testów przybyło, więc czasem nie zdążyła.
- **Rozwiązanie:** domyślny limit 10 s dla całego projektu testów integracyjnych, ustawiany w fiksturze bazy. Udany warunek kończy oczekiwanie od razu, więc zielone testy nie zwalniają. Nie zmieniałem metody skanu, bo część testów celowo skanuje szybko, bez czekania.

## Poziom 2: analizy, eksport i etykiety (11.11.2026)

### Docker Desktop nie wstał po ponownym uruchomieniu komputera

- **Objaw:** testy integracyjne padły z `DockerUnavailableException`, a `docker ps` zgłaszał brak potoku `dockerDesktopLinuxEngine`.
- **Rozwiązanie:** `docker desktop start`. Baza deweloperska wstała sama, bo ma `restart: unless-stopped`.

### Zła liczba w moim teście prognozy

- **Objaw:** test prognozy oczekiwał średniej 1 sztuki dziennie, a dostał 0,75.
- **Przyczyna:** zegar testu przesuwał się o 7 dni po każdej sprzedaży, więc pierwsza wypadła poza 28-dniowe okno historii. Kod liczył dobrze, test zakładał co innego, niż ustawił.
- **Rozwiązanie:** przesunięcia o 6 dni, wszystkie cztery sprzedaże w oknie.

### Etykiety po jednym rzędzie na stronę

- **Objaw:** 25 etykiet dało 10 stron PDF zamiast 2.
- **Przyczyna:** dwie naraz. QuestPDF domyślnie skaluje SVG do szerokości, więc kod kreskowy był za wysoki. Do tego długie SKU z testu (z GUID-em, bez spacji) zawijało się na kilka wierszy. Etykieta nie mieściła się w 37 mm, a tabela przenosiła ją na następną stronę.
- **Rozwiązanie:** kod w obszarze 37 × 13 mm (`FitArea`), SKU w jednym wierszu z wielokropkiem, cena obok. Bez narzędzia do podglądu PDF układ sprawdziłem, każąc QuestPDF chwilowo zapisać obraz strony. Ten podgląd pokazał też, że kod był za wąski (moduł około 0,2 mm, poniżej minimum normy), więc ma teraz około 0,32 mm.
