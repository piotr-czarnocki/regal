# Analiza dynamiczna

Zakres według sylabusa „Dynamiczna analiza oprogramowania”, punkty 2–7, zgodnie z punktem 10 [`SPEC.md`](../SPEC.md). Wszystkie liczby pochodzą z uruchomień opisanych w tym dokumencie. Dotyczą stanu z 10.11.2026, chyba że przy danym wyniku podano inaczej.

## Spis

1. [Co mierzy każdy rodzaj testów](#1-co-mierzy-każdy-rodzaj-testów)
2. [Testy jednostkowe](#2-testy-jednostkowe)
3. [Testy integracyjne](#3-testy-integracyjne)
4. [Testy funkcjonalne](#4-testy-funkcjonalne)
5. [Testy wydajności](#5-testy-wydajności)
6. [Debugowanie](#6-debugowanie)
7. [Dobór narzędzi](#7-dobór-narzędzi)

## 1. Co mierzy każdy rodzaj testów

Regał to Blazor Web App w trybie Interactive Server: pierwsze wczytanie strony to zwykłe żądanie HTTP z wyrenderowanym HTML, a każde późniejsze kliknięcie idzie przez stałe połączenie SignalR. Dlatego każdy rodzaj testów sprawdza co innego.

| Rodzaj | Projekt | Liczba | Co uruchamia | Czego nie obejmuje |
|---|---|---|---|---|
| Jednostkowe | `tests/Regal.UnitTests` | 226 | Czyste reguły (średnia, FEFO, ABC, EAN, NIP, walidacja dokumentu, kwoty faktur), klient Fakturowni z podstawionym `HttpMessageHandler`, komponenty Blazora w bUnit z podmienionym JavaScriptem | Bazy danych, przeglądarki |
| Integracyjne | `tests/Regal.IntegrationTests` | 149 | Serwisy na prawdziwym PostgreSQL 18.6 w Testcontainers z rolą o ograniczonych uprawnieniach; strony renderowane przez aplikację w teście (`WebApplicationFactory`) | Kliknięć w przeglądarce |
| Funkcjonalne | `tests/Regal.E2ETests` | 12 | Cała aplikacja na Kestrelu, Chromium sterowany przez Playwright, logowanie kontem testowym, udawana Fakturownia | Prawdziwego Google i Fakturowni |
| Wydajności: zapytania | `tests/performance/Regal.Benchmarks` | 7 zapytań | Zapytania analiz z serwisu na bazie z rokiem wygenerowanych danych | Renderowania stron |
| Wydajności: HTTP | `tests/performance/k6`, `Regal.LoadHost --measure` | 8 stron | Pierwsze wczytanie stron (pełny HTML z serwera) przez 20 równoczesnych użytkowników | Zdarzeń SignalR po wczytaniu (kliknięcia, filtry) |

Wszystkie testy jednostkowe, integracyjne i funkcjonalne uruchamia CI (GitHub Actions) przy każdym wypchnięciu na `main` i każdym pull requeście. Testy wydajności uruchamia się ręcznie, bo wymagają dużego zestawu danych.

Testy nigdy nie łączą się z prawdziwą Fakturownią ani z Google: klient Fakturowni jest za interfejsem `IFakturowniaClient`, a host testowy podstawia schemat uwierzytelniania testowego.

## 2. Testy jednostkowe

### Zestawy

| Klasa testów | Testy | Co sprawdza |
|---|---|---|
| `StockDraftValidatorTests` | 37 | Reguły dokumentu: kontrahent dla typu, 1–200 pozycji, ilość > 0 i do 3 miejsc, liczby całkowite dla „szt”, powtórzone produkty i partie, data ważności, powód RW |
| `InvoiceRulesTests` | 27 | Kwoty pozycji z rabatem, VAT od wartości po rabacie z zaokrągleniem połowa w górę, terminy i sposoby płatności, `oid`, odstępy ponowień 1, 2, 4… ≤ 60 min |
| `Ean13Tests` | 22 | Cyfra kontrolna, prefiks wewnętrzny `040`, 12 cyfr jako EAN-13 z wiodącym zerem |
| `ProductPickerTests` (bUnit) | 21 | Pole produktu: Enter szuka EAN, potem SKU, potem fragmentu; kolejka skanów; szybkie skany podczas ustawiania fokusu |
| `StockDocumentEditorTests` (bUnit) | 16 | Formularz dokumentu: pola zależne od typu, przecinek dziesiętny w polskiej kulturze, Enter nie zatwierdza dokumentu |
| `FakturowniaClientTests` | 15 | Nagłówek `Authorization: Bearer`, token nigdy w adresie, `oid_unique`, klasyfikacja błędów (5xx/429/408/sieć → przejściowy, 4xx → odmowa), dokładne dopasowanie `oid` |
| `BatchAllocatorTests` | 15 | FEFO: najkrótsza ważność, partie bez daty na końcu, remis według wieku partii, kilka partii na pozycję, przeterminowane dla WZ i RW |
| `NipTests` | 14 | Suma kontrolna NIP i normalizacja zapisu |
| `BarcodeScannerTests` (bUnit) | 12 | Okno skanera: odczyt, odrzucenie niedozwolonych znaków, brak aparatu, odmowa dostępu |
| `AccessPolicyTests` | 10 | Lista dozwolonych adresów, nieaktywny użytkownik |
| `AverageCostTests` | 9 | Średnia ważona przy przyjęciu i korekcie, zaokrąglenie do 4 miejsc, przycięcie do zera |
| `InvoiceEditorTests` (bUnit) | 7 | Formularz faktury: sumy na bieżąco, usługi bez stanu |
| `AbcClassifierTests` | 7 | Granice 80% i 95%, produkt przekraczający granicę, pierwszy zawsze w A, remisy |
| `FormDialogTests` (bUnit) | 6 | Okna kartotek |
| `WarehouseTimeTests` | 5 | Data i rok dokumentu według Europe/Warsaw, przełom roku w UTC |
| `CurrentUserTests` | 3 | Rola z tożsamości |

Dane testowe pochodzą z obliczeń ręcznych zapisanych w testach (np. średnia z dwóch przyjęć) i z przypadków granicznych z `SPEC.md`. Testy z czasem używają `FakeTimeProvider`, więc są deterministyczne.

### Wynik

226 z 226 testów przechodzi lokalnie w 5 s i w każdym przebiegu CI od scalenia etapu „Analizy”.

### Pokrycie kodu

Pomiar: `dotnet test --collect:"XPlat Code Coverage"` (coverlet 6.0.4) osobno dla testów jednostkowych i integracyjnych. Raporty Cobertura połączono skryptem, który liczy wiersz jako pokryty, gdy wykonał go którykolwiek zestaw. Pominięto pliki migracji (kod generowany przez EF Core). Testy funkcjonalne nie są wliczone, bo aplikacja działa w nich w osobnym serwerze Kestrel.

| Obszar `src/Regal.Web` | Wiersze | Jednostkowe | Integracyjne | Razem |
|---|---:|---:|---:|---:|
| `Features/Warehouse` | 1250 | 35,6% | 79,4% | **91,1%** |
| `Features/Invoices` | 940 | 26,2% | 68,6% | **84,0%** |
| `Features/Products` | 437 | 4,3% | 56,3% | **56,3%** |
| `Data/Generator` | 353 | 0,0% | 97,7% | **97,7%** |
| `Features/Analytics` | 304 | 6,9% | 86,8% | **87,5%** |
| `Data` (DbContext, konfiguracja) | 218 | 0,0% | 94,0% | **94,0%** |
| `Features/Contractors` | 193 | 11,9% | 59,6% | **61,7%** |
| `Features/Auth` | 182 | 12,6% | 34,6% | **42,3%** |
| `Components/Shared` (skaner, okna) | 126 | 81,0% | 1,6% | **81,0%** |
| `Data/Entities` | 117 | 16,2% | 78,6% | **78,6%** |
| `Integrations/Fakturownia` | 92 | 97,8% | 3,3% | **97,8%** |
| `Program.cs` | 58 | 0,0% | 0,0% | **0,0%** |
| pozostałe (`Components`, `Components/Pages`, `Components/Layout`, `Features/Audit`, `Features/Common`) | 130 | — | — | 73,8% |
| **Razem** | **4400** | **22,8%** | **69,5%** | **81,1%** |

Interpretacja:

- Reguły biznesowe (magazyn, faktury, analizy) mają pokrycie 84–91%. Niepokryte wiersze to głównie gałęzie komunikatów o błędach w ekranach i obsługa anulowania operacji (`CancellationToken`).
- Niskie pokrycie `Features/Products` i `Features/Contractors` wynika z ekranów kartotek (pliki `.razor`), które sprawdzano ręcznie w przeglądarce podczas etapu „Kartoteki”, bez zapisanych scenariuszy. Serwisy kartotek są pokryte testami `CatalogTests`.
- `Features/Auth` to w dużej części obsługa przekierowań Google, której testy nie wywołują, bo zastępuje ją schemat testowy. Logikę listy dozwolonych adresów sprawdzają `AccessPolicyTests` i `AuthServiceTests`.
- `Program.cs` pokazuje 0%, bo coverlet nie przypisuje wierszy instrukcjom najwyższego poziomu uruchamianym przez `WebApplicationFactory`. Konfigurację aplikacji wykonuje każdy test stron i każdy test funkcjonalny.
- Pokrycie nie było celem samym w sobie. Ważniejszym sprawdzianem jakości testów są eksperymenty z punktu 3, w których celowo usunięto zabezpieczenia i sprawdzono, czy testy to wykrywają.

## 3. Testy integracyjne

### Strategia

- **Prawdziwa baza:** każdy przebieg uruchamia PostgreSQL 18.6 w kontenerze (Testcontainers). Migracje wykonuje rola właściciela, a testy działają rolą aplikacji bez `UPDATE` i `DELETE` na dokumentach, tak jak aplikacja. Dzięki temu testy `WarehouseSecurityTests` sprawdzają, że baza sama odrzuca zmianę zatwierdzonego dokumentu.
- **Izolacja:** jeden kontener na cały zestaw; każdy test tworzy własne produkty i kontrahentów z unikalnymi nazwami, więc testy nie zależą od kolejności.
- **Współbieżność:** testy z `WarehouseConcurrencyTests` i `InvoiceTests` uruchamiają operacje równolegle (`Task.Run` i `Task.WhenAll`), każdą z osobnym `DbContext` i połączeniem, żeby naprawdę się na siebie nałożyły.
- **Zewnętrzne usługi:** udawana Fakturownia (`FakeFakturownia`) potrafi zgubić odpowiedź, zwrócić 5xx, 429, odmowę i odrzucić duplikat `oid`. Liczy wszystkie żądania utworzenia faktury.
- **Strony:** `WarehousePagesTests` i `InvoicePagesTests` renderują strony przez aplikację w teście i sprawdzają wyrenderowany HTML.

| Klasa testów | Testy | Co sprawdza |
|---|---|---|
| `WarehouseDocumentTests` | 40 | Przyjęcia, wydania, FEFO na bazie, numeracja bez luk, wycofanie całego dokumentu przy błędzie pozycji, idempotencja `RequestId`, korekty, transakcja wywołującego (savepoint) |
| `InvoiceTests` | 28 | Wystawienie z WZ w jednej transakcji, wysyłka w tle, ponowienia, zgubiona odpowiedź, odmowa, anulowanie z cofnięciem WZ, równoległe procesy wysyłki |
| `WarehousePagesTests` | 16 | Ekrany magazynu: listy, szczegóły, korekta, oznaczenia „nieaktywny” i „przeterminowana” |
| `CatalogTests` | 15 | Produkty, kategorie, kontrahenci: unikalność, dezaktywacja z dokumentami, blokada zmiany „ma datę ważności” |
| `WarehouseSecurityTests` | 10 | Uprawnienia bazy: brak `UPDATE`/`DELETE` na dokumentach, ruchach, partiach, pozycjach faktur i dzienniku |
| `AnalyticsTests` | 10 | Sprzedaż i marża, filtry, ABC, zalegające, partie z bliską datą, anulowane faktury pominięte |
| `DemoDataGeneratorTests` | 9 | Generator: reguły aplikacji zachowane, faktury nigdy nie trafiają do wysyłki, ten sam seed daje te same dane, odmowa w środowisku produkcyjnym, spisanie wszystkich przeterminowanych partii |
| `WarehouseConcurrencyTests` | 8 | Równoczesne przyjęcia i wydania, numeracja, idempotencja, korekty, dezaktywacja w trakcie przyjęcia, brak zakleszczeń |
| `AuthServiceTests` | 7 | Logowanie: lista adresów, rola, wpis w dzienniku |
| `InvoicePagesTests` | 6 | Ekrany faktur |

Wynik: 149 z 149 testów przechodzi lokalnie w około 60 s (z uruchomieniem kontenera) i w CI.

### Eksperyment 1: czy testy współbieżności wykrywają brak blokady

**Cel.** Etap „Magazyn” opiera się na blokadzie `SELECT … FOR UPDATE` na wierszach `Products`. Zielone testy współbieżności niczego nie dowodzą, jeśli przechodzą także bez niej. Eksperyment sprawdza, które testy padają, gdy blokady zabraknie.

**Metoda.** W `ProductLocks.LockAsync` usunięto z zapytania same słowa `FOR UPDATE`. Reszta kodu pozostała bez zmian. Pełny zestaw testów integracyjnych (wtedy 80 testów) uruchomiono 5 razy.

**Wyniki bez blokady:**

| Test (`WarehouseConcurrencyTests`) | Przebiegi z błędem | Błąd | Co to oznacza |
|---|---|---|---|
| `Parallel_receipts_of_one_product_give_the_correct_average` | 5 z 5 | `Assert.InRange() Failure`: oczekiwano 20,2464–20,2664, otrzymano `30,0000` | Utracone aktualizacje: każda transakcja liczyła średnią ze starej wartości, wygrywał ostatni zapis |
| `Parallel_issues_within_stock_all_succeed_across_several_batches` | 5 z 5 | `Assert.All() Failure: 14 out of 15`, komunikat „Brak towaru na partii” | Fałszywe braki: wszystkie wydania wybrały tę samą partię, choć towaru wystarczało |
| `Parallel_issues_exceeding_stock_exactly_one_succeeds_and_stock_stays_valid` | 5 z 5 | `Assert.All() Failure: 2–9 out of 11`: komunikat z warunkowego `UPDATE` zamiast z przydziału | Dokładnie jedno wydanie nadal się udało. Przed zbyt dużym wydaniem chroni warunkowy `UPDATE`, ale przydział działał na nieaktualnych danych |
| `Deactivation_racing_with_a_receipt_never_leaves_an_inactive_product_with_stock` | 5 z 5 | `An inactive product received stock.` | Dezaktywacja i przyjęcie minęły się, powstał stan na nieaktywnym produkcie |

Pozostałe 76 testów przeszło we wszystkich pięciu przebiegach. Wśród nich są testy, które nie zależą od tej blokady:

- `Parallel_documents_get_consecutive_numbers_without_duplicates`: numerację chroni `INSERT … ON CONFLICT DO UPDATE` na liczniku.
- `Parallel_sends_of_one_draft_create_one_document`: chroni unikalny indeks na `RequestId`.
- `Parallel_corrections_of_one_document_create_one_correction`: chroni częściowy unikalny indeks na `ReversesDocumentId`.

**Test, który nie wykrywał problemu.** Test `Documents_listing_products_in_opposite_order_do_not_deadlock` w pierwszej wersji przeszedł bez blokady, czyli niczego nie sprawdzał.

- **Przyczyna:** używał wyłącznie dokumentów WZ. Licznik numerów jest zwiększany przed zmianą stanów, a blokada jego wiersza trwa do końca transakcji. Dokumenty tego samego typu i tak czekały więc na siebie.
- **Poprawka:** test na przemian tworzy WZ i RW, które mają osobne liczniki.
- **Wynik po poprawce, bez blokady:** 5 z 5 uruchomień tego testu kończy się błędem `Npgsql.PostgresException: 40P01: deadlock detected`.

**Przywrócenie blokady.** Do zapytania wrócił fragment `FOR UPDATE`. Polecenie `git diff --exit-code` dla `ProductLocks.cs` zakończyło się kodem 0, więc plik jest identyczny z wersją z commita. Po przebudowaniu cały zestaw integracyjny przeszedł 5 razy z rzędu (po 80 z 80), a testy jednostkowe 121 z 121.

**Wnioski:**

- Blokada produktu chroni to, czego nie wyrazi pojedynczy warunek w SQL: średnią, wybór partii i sprawdzenia poprzedzające zapis.
- Warunkowy `UPDATE` i `CHECK` są ostatnią barierą: nawet bez blokady stan nie spadł poniżej zera.
- Numeracja bez luk powoduje, że dokumenty tego samego typu zapisują się po kolei od chwili pobrania numeru. To świadomy koszt tego wymagania.
- Test współbieżności trzeba sprawdzić, usuwając to, co ma chronić. Inaczej nie wiadomo, czy potrafi się nie udać.

### Eksperyment 2: wysyłka faktur bez duplikatów

**Cel.** Wysyłkę faktur do Fakturowni chronią dwa zabezpieczenia:

- pobranie zadania z `FOR UPDATE SKIP LOCKED`, żeby dwa procesy wysyłki nie wzięły tego samego zadania;
- wyszukanie faktury po `oid` przed jej utworzeniem, żeby ponowienie po zgubionej odpowiedzi nie utworzyło duplikatu.

Udawana Fakturownia w testach, jak prawdziwa przy `oid_unique=yes`, odrzuca drugą fakturę z tym samym `oid`. Eksperyment sprawdza, czy testy wykrywają brak każdego z tych zabezpieczeń.

**Metoda.** W `InvoiceSender` na próbę usuwano po jednym zabezpieczeniu i uruchamiano 28 testów faktur 3 razy.

**Wyniki:**

| Usunięte | Pierwsza wersja testów | Po wzmocnieniu testów |
|---|---|---|
| `FOR UPDATE SKIP LOCKED` | Wszystkie testy przeszły 3 z 3 razy | `Parallel_senders_send_each_invoice_exactly_once`: 28–31 żądań utworzenia zamiast 12, 3 z 3 |
| Wyszukanie po `oid` przed utworzeniem | Przeszedł test zgubionej odpowiedzi, padły 3 inne testy, ale z innego powodu (opis niżej) | `Lost_reply_after_a_successful_create…`: 2 żądania utworzenia zamiast 1, 3 z 3 |

**Dlaczego pierwsza wersja nie wykrywała braku zabezpieczeń.** Testy sprawdzały tylko efekt końcowy: jedna faktura w Fakturowni. Ten efekt zapewniało też odrzucanie duplikatów `oid` przez udawaną Fakturownię, więc brak blokady albo wyszukania niczego nie psuł. Wzmocnione testy sprawdzają, że do Fakturowni trafia dokładnie jedno żądanie utworzenia na fakturę, łącznie z odrzuconymi.

**Trzy testy, które padły przy usuniętym wyszukaniu w pierwszej wersji,** nie wykrywały duplikatu. Dwa wstrzykiwały błąd właśnie w wyszukiwanie, którego już nie było. Trzeci zależał od kolejności wywołań udawanego klienta.

**Przywrócenie.** Usunięte fragmenty przywrócono. `git diff --exit-code` dla katalogu `src` zwrócił 0, czyli kod jest identyczny z commitem. Pełny zestaw przeszedł potem 3 razy z rzędu: 199 testów jednostkowych i 124 integracyjne.

**Wnioski:**

- Jeśli zabezpieczeń jest kilka, test efektu końcowego nie mówi, czy działa każde z nich. Trzeba mierzyć to, czemu dane zabezpieczenie zapobiega, tutaj liczbę żądań.
- Działanie `oid_unique` w prawdziwej Fakturowni nie jest jeszcze potwierdzone (scenariusz F6 na koncie testowym). Do tego czasu duplikatom zapobiegają blokada i wyszukanie, a nie Fakturownia.

## 4. Testy funkcjonalne

### Metoda

Testy w `tests/Regal.E2ETests` uruchamiają całą aplikację na Kestrelu z losowym portem, z bazą w Testcontainers i udawaną Fakturownią. Chromium sterowany przez Playwright 1.64.1 loguje się ciasteczkiem schematu testowego, klika i wpisuje jak człowiek, a każdy krok czeka, aż strona stanie się interaktywna (`body[data-interactive='true']`, ustawiane po nawiązaniu połączenia Blazora). Wynik jest sprawdzany na ekranie, a tam, gdzie ekran nie pokazuje wszystkiego (np. średnia z 4 miejscami), także w bazie. Przy błędzie test zapisuje zrzut ekranu.

Scenariusze odpowiadają scenariuszom ręcznym z [`scenariusze-testowe.md`](scenariusze-testowe.md).

| Test | Scenariusz | Dane | Oczekiwany wynik |
|---|---|---|---|
| `Anonymous_visitor_is_sent_to_the_login_page` ×3 | — | `/`, `/stock-documents/new`, `/invoices` | Przekierowanie na stronę logowania, brak danych |
| `Deactivated_user_gets_no_data` | — | Użytkownik z listy, potem dezaktywowany | Strona odmowy dostępu |
| `Signed_in_administrator_sees_the_dashboard_and_the_menu` | A2 (pulpit) | Aktywny administrator | Pulpit i pełne menu |
| `Two_receipts_of_one_batch_give_the_weighted_average` ×3 | S1 | 100 × „2,50” + 50 × „3,10”; 10 × 5 + 10 × 7; 3 × „0,99” + 1 × 100 | Stan 150 / 20 / 4, średnia 2,7000 / 6,0000 / 25,7425 |
| `Issue_takes_the_batch_expiring_first_and_shows_it_in_the_preview` | S4 | Dwie partie z różną ważnością, wydanie WZ | Podgląd i dokument schodzą z partii o krótszej ważności |
| `Enter_adds_lines_and_never_approves_the_document` | S8 | Kody wpisane z Enterem, w tym „1” i „10” | Każdy Enter dodaje pozycję, dokument nie zostaje zatwierdzony |
| `Stock_page_fits_a_phone_screen` | S10 | Ekran 390 × 844 | Brak przewijania w poziomie |
| `Invoice_with_goods_and_a_service_issues_a_wz_and_is_sent_in_the_background` | F1 | Towar z rabatem i usługa | WZ tylko dla towaru, status „wysłana” z numerem z udawanej Fakturowni |

### Wynik

12 z 12 testów przechodzi lokalnie i w CI, gdzie przeglądarkę instaluje krok `playwright.ps1 install --with-deps chromium`.

Testy funkcjonalne znalazły błąd, którego nie widziały 300 innych testów: cena „2,50” wpisana w pole zapisywała się jako 250 (punkt 6, błąd 1). Pierwszy zestaw danych S1 był czerwony, dopóki pola liczbowe nie dostały polskiej kultury.

Ręcznie, bez automatyzacji, pozostają scenariusze wymagające prawdziwych usług i urządzeń: logowanie Google, skanowanie aparatem telefonu przez tunel, czytnik USB oraz F6 i F7 na koncie testowym Fakturowni.

## 5. Testy wydajności

*Sekcja w przygotowaniu: wyniki pomiarów na bazie z rokiem wygenerowanych danych.*

## 6. Debugowanie

Opisane są prawdziwe błędy z tego projektu: objaw, jak go namierzono, przyczyna i poprawka. Pełna lista problemów jest w [`przygody.md`](przygody.md).

### Błąd 1: cena „2,50” zapisywała się jako 250

- **Objaw:** test funkcjonalny S1 po dwóch przyjęciach po 2,50 i 3,10 zł dostał cenę średnią 270,0000 zamiast 2,7000. Liczby całkowite działały. Testy jednostkowe i integracyjne były zielone.
- **Namierzanie:** wynik dało się odtworzyć rachunkiem: (100 × 250 + 50 × 310) / 150 = 270. Obie ceny zostały więc odczytane bez przecinka, jako 250 i 310. Serwis dostaje liczbę `decimal`, a nie tekst, więc błąd musiał powstać przy odczycie pola. Test bUnit samego formularza z wpisanym „2,50” i polską kulturą odtworzył go bez przeglądarki i bez bazy: wartość pozycji wynosiła 250. Dokumentacja i kod źródłowy MudBlazor 9 potwierdziły przyczynę: `MudNumericField` czyta liczby w `InvariantCulture`, chyba że ustawiono `Culture`, `Format` albo `Pattern`, więc przecinek był separatorem tysięcy.
- **Dlaczego wcześniej niewidoczny:** pole ceny w kartotece produktów ma `Format="F2"`, więc tam działało. Testy bUnit wpisywały liczby całkowite, a testy serwisów nie przechodzą przez pole.
- **Poprawka:** wszystkie pola liczbowe mają `Culture="CultureInfo.CurrentCulture"` (kultura polska z `UseRequestLocalization`). Test bUnit z polską kulturą: bez poprawki 250, z poprawką 2,50.

### Błąd 2: Enter na kodzie „1” nic nie robił, a szybkie skany ginęły

- **Objaw:** w polu produktu na nowym dokumencie „1” i Enter nie dodawały pozycji ani komunikatu; „10” i Enter działały. Przy szybkim skanowaniu kilku kodów czasem dodawał się tylko pierwszy.
- **Namierzanie:** testy bUnit odtworzyły trzy osobne przyczyny: tekst krótszy niż 2 znaki nie był wyszukiwany, kilka wyników dawało ciszę, a Enter korzystał z wyszukiwania opóźnionego o 250 ms, które szybki Enter anulował. Zgubione skany były trudniejsze. Dwa razy wskazałem złą przyczynę, bo w bUnit zdarzenia są wysyłane bez czekania na obsługę, więc drugi „skan” w teście trafiał w trwające przerenderowanie i ginął w samym teście. Pomogło dopiero odczytanie stanu komponentu (kolejka skanów, flaga `_resolving`) po każdym kroku zamiast zgadywania. Okazało się, że skan, który przyszedł w czasie `FocusAsync` po dodaniu pozycji, trafiał do kolejki, ale pętla obsługi już się kończyła.
- **Poprawka:** Enter sam szuka w kolejności: dokładny EAN, dokładny SKU, fragment. Kolejka skanów jest obsługiwana w pętli `do … while`, która po `FocusAsync` sprawdza, czy coś nie doszło. Testy `ProductPickerTests` obejmują każdą z tych przyczyn, w tym skan w trakcie ustawiania fokusu. Przypadek „1”/„10” jest w `scenariusze-testowe.md` (S8) i w teście funkcjonalnym.

### Błąd 3: generator danych wystawiał trzy razy mniej faktur, niż powinien

- **Objaw:** generator dla testów wydajności miał dać około 100 faktur w dzień roboczy, a dał 12 206 faktur w roku i 54 640 ruchów magazynowych zamiast ponad 100 000. Nie zgłosił błędu.
- **Namierzanie:** dziennik generatora (co 30 dni liczba faktur i dokumentów) pokazał, że w drugiej połowie roku powstawało około 24 faktur dziennie, o połowę mniej niż w pierwszych miesiącach. Zapytania do bazy wydajnościowej wykazały 4545 partii po dacie ważności, które nadal mają stan, z liczbą rosnącą co tydzień, i 9722 dokumenty RW, czyli prawie dokładnie 200 na tydzień. To wskazało na stronicowanie w cotygodniowym spisaniu.
- **Przyczyna:** spisanie brało jedną stronę 200 partii na tydzień, każdą na osobnym RW. Przy 10 000 produktów wygasało więcej partii, niż spisywano. Generator liczył ten towar jako dostępny, wybierał go do faktur, a serwis słusznie odmawiał wydania przeterminowanej partii. Odmowa była traktowana jak zwykły brak towaru i po cichu pomijana. Przy okazji: strona była pobierana bez `ORDER BY`, więc wynik zależał od planu zapytania, a nie od seeda.
- **Poprawka:** spisanie czyta kolejne strony w stałej kolejności, aż nic nie zostanie, i daje do 200 produktów na jeden RW. Wielkość strony jest parametrem, więc test integracyjny odtwarza przypadek na 300 produktach ze stroną 5: przed poprawką zostawiał 53 przeterminowane partie, po poprawce zero. Ponowne wygenerowanie danych: wyniki w punkcie 5.

### Błąd 4: generator zapisał dane do niewłaściwej bazy

- **Objaw:** generator uruchomiony z `ConnectionStrings__Regal` wskazującym bazę wydajnościową zostawił ją pustą i skończył się błędem `relation "Invoices" does not exist`.
- **Namierzanie:** stos wywołań wskazywał pierwszą fakturę (`InvoiceService.FindByOidAsync`), ale baza wydajnościowa miała wszystkie migracje i była pusta. Generator musiał więc łączyć się z inną bazą, w której brakowało migracji `AddInvoices`. Taką bazą była lokalna baza deweloperska z User Secrets, i to w niej znalazły się produkty `PERF-…`.
- **Przyczyna:** narzędzie dodawało User Secrets projektu webowego po domyślnych źródłach konfiguracji, w tym po zmiennych środowiskowych. Późniejsze źródło wygrywa, więc sekrety nadpisywały jawnie podane połączenie.
- **Poprawka:** konfiguracja budowana ręcznie w ustalonej kolejności (wiersz poleceń > zmienne środowiskowe > User Secrets), a przed zapisem generator wypisuje bazę docelową, bez hasła. Bazę deweloperską wyczyszczono w porozumieniu z autorem.

### Ślad wykonania (`dotnet-trace`)

*Opis w przygotowaniu razem z testami wydajności.*

### Wnioski z debugowania

- Trzy z czterech błędów nie dawały wyjątku, tylko złe liczby. Dlatego testy sprawdzają wartości (średnią, liczbę żądań, liczbę faktur), a nie tylko brak błędu.
- W testach komponentów trzeba rozumieć, jak narzędzie wysyła zdarzenia, bo inaczej szuka się błędu w aplikacji, który jest w teście.
- Narzędzie, które pisze do bazy, powinno najpierw pokazać, dokąd pisze.

## 7. Dobór narzędzi

Porównanie wybranych narzędzi z alternatywami i uzasadnienie wyboru jest w [`narzedzia.md`](narzedzia.md), w sekcji „Testy”. W skrócie:

- **xUnit + bUnit + FakeTimeProvider** dla reguł i komponentów: szybkie, deterministyczne, bez przeglądarki.
- **Testcontainers z PostgreSQL**, a nie baza w pamięci, bo testowane mechanizmy (`FOR UPDATE`, `SKIP LOCKED`, uprawnienia, `CHECK`) istnieją tylko w prawdziwej bazie. Eksperymenty z punktu 3 pokazują, że te testy potrafią się nie udać.
- **Playwright**, bo automatycznie czeka na elementy. Ma to znaczenie w Blazorze, gdzie strona staje się interaktywna chwilę po wczytaniu.
- **BenchmarkDotNet i `EXPLAIN (ANALYZE, BUFFERS)`** dla zapytań; **k6** i równoważny pomiar w C# dla HTTP.
- **coverlet** dla pokrycia; **debugger, logi i `dotnet-trace`** do debugowania.
