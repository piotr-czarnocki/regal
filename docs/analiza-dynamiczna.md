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
| Jednostkowe | `tests/Regal.UnitTests` | 293 | Czyste reguły (średnia, FEFO, ABC, EAN, NIP, walidacja dokumentu, kwoty faktur), klient Fakturowni z podstawionym `HttpMessageHandler`, komponenty Blazora w bUnit z podmienionym JavaScriptem | Bazy danych, przeglądarki |
| Integracyjne | `tests/Regal.IntegrationTests` | 242 | Serwisy na prawdziwym PostgreSQL 18.6 w Testcontainers z rolą o ograniczonych uprawnieniach; strony renderowane przez aplikację w teście (`WebApplicationFactory`) | Kliknięć w przeglądarce |
| Funkcjonalne | `tests/Regal.E2ETests` | 13 | Cała aplikacja na Kestrelu, Chromium sterowany przez Playwright, logowanie kontem testowym, udawana Fakturownia | Prawdziwego Google i Fakturowni |
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
| `ProductPickerTests` (bUnit) | 22 | Pole produktu: Enter szuka EAN, potem SKU, potem fragmentu; kolejka skanów; szybkie skany podczas ustawiania fokusu; sygnał zakończenia obsługi dla testów w przeglądarce |
| `StockDocumentEditorTests` (bUnit) | 16 | Formularz dokumentu: pola zależne od typu, przecinek dziesiętny w polskiej kulturze, Enter nie zatwierdza dokumentu |
| `FakturowniaClientTests` | 20 | Odczyt płatności z przecinkiem lub kropką, wysyłka mailem z zakodowanym adresem; Nagłówek `Authorization: Bearer`, token nigdy w adresie, `oid_unique`, klasyfikacja błędów (5xx/429/408/sieć → przejściowy, 4xx → odmowa), dokładne dopasowanie `oid` |
| `BatchAllocatorTests` | 15 | FEFO: najkrótsza ważność, partie bez daty na końcu, remis według wieku partii, kilka partii na pozycję, przeterminowane dla WZ i RW |
| `NipTests` | 14 | Suma kontrolna NIP i normalizacja zapisu |
| `BarcodeScannerTests` (bUnit) | 12 | Okno skanera: odczyt, odrzucenie niedozwolonych znaków, brak aparatu, odmowa dostępu |
| `OrderRulesTests` | 13 | Zamówienie: nabywca, 1–200 pozycji, produkt raz, uwagi do 500 znaków, numer `ZK/n/rok`, nazwy statusów |
| `CorrectionRulesTests` | 15 | Korekta: przyczyna, ilość nieujemna, cena i rabat, pozycja raz; proforma nie z zamówienia; rodzaj i stany „przed/po” w pakiecie dla Fakturowni |
| `VatRegistryTests` | 14 | Biała lista VAT: adres zapytania, odczyt nazwy i adresu (także osoby fizycznej), nieznany NIP, odmowa z kodem, błędy serwera i sieci, tylko znormalizowany NIP w adresie; wyszukiwanie: normalizacja, data w Polsce, niepoprawny NIP nie jest wysyłany |
| `PlanningRulesTests` | 19 | Porównanie okresów (poprzedni, rok wcześniej, 29 lutego), zmiana w procentach, średnia dzienna, zapas w dniach, sugerowane zamówienie zaokrąglane w górę, zakres dni |
| `AccessPolicyTests` | 10 | Lista dozwolonych adresów, nieaktywny użytkownik |
| `AverageCostTests` | 9 | Średnia ważona przy przyjęciu i korekcie, zaokrąglenie do 4 miejsc, przycięcie do zera |
| `InvoiceEditorTests` (bUnit) | 7 | Formularz faktury: sumy na bieżąco, usługi bez stanu |
| `AbcClassifierTests` | 7 | Granice 80% i 95%, produkt przekraczający granicę, pierwszy zawsze w A, remisy |
| `FormDialogTests` (bUnit) | 6 | Okna kartotek |
| `WarehouseTimeTests` | 5 | Data i rok dokumentu według Europe/Warsaw, przełom roku w UTC |
| `CurrentUserTests` | 3 | Rola z tożsamości |

Dane testowe pochodzą z obliczeń ręcznych zapisanych w testach (np. średnia z dwóch przyjęć) i z przypadków granicznych z `SPEC.md`. Testy z czasem używają `FakeTimeProvider`, więc są deterministyczne.

### Wynik

293 z 293 testów przechodzi lokalnie w 5 s i w każdym przebiegu CI od scalenia etapu „Analizy”.

### Pokrycie kodu

Pomiar: `dotnet test --collect:"XPlat Code Coverage"` (coverlet 6.0.4) osobno dla testów jednostkowych i integracyjnych. Raporty Cobertura połączono skryptem, który liczy wiersz jako pokryty, gdy wykonał go którykolwiek zestaw. Pominięto pliki migracji (kod generowany przez EF Core). Testy funkcjonalne nie są wliczone, bo aplikacja działa w nich w osobnym serwerze Kestrel. Pomiar pochodzi z 10.11.2026, sprzed dodania zamówień klientów (poziom 2), więc nie obejmuje modułu `Features/Orders`.

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
| `AnalyticsTests` | 11 | Sprzedaż i marża, filtry, ABC, zalegające (także z anulowaną nowszą sprzedażą), partie z bliską datą, anulowane faktury pominięte |
| `DemoDataGeneratorTests` | 9 | Generator: reguły aplikacji zachowane, faktury nigdy nie trafiają do wysyłki, ten sam seed daje te same dane, odmowa w środowisku produkcyjnym, spisanie wszystkich przeterminowanych partii |
| `WarehouseConcurrencyTests` | 8 | Równoczesne przyjęcia i wydania, numeracja, idempotencja, korekty, dezaktywacja w trakcie przyjęcia, brak zakleszczeń |
| `AuthServiceTests` | 7 | Logowanie: lista adresów, rola, wpis w dzienniku |
| `InvoicePagesTests` | 8 | Ekrany faktur, w tym korekta wystawiona z formularza oraz sprawdzenie płatności i wysyłka mailem |
| `OrderTests` | 27 | Zamówienia: rezerwacja, brak przy zapisie, przeterminowane partie, WZ i faktura nie biorą cudzej rezerwacji, RW ją ignoruje, faktura z zamówienia, anulowanie, idempotencja, uprawnienia bazy, trzy testy współbieżności |
| `OrderPagesTests` | 5 | Ekrany zamówień i faktura wypełniona z zamówienia |
| `CorrectionTests` | 17 | Proforma bez WZ i poza sprzedażą; korekta: zwrot na te same partie, różnice w pozycjach, sprzedaż i marża po korekcie, pakiet, tylko wysłana faktura VAT i raz, walidacja, idempotencja, dwa testy współbieżności, ograniczenie w bazie |
| `PaymentTests` | 16 | Płatności: odczyt statusu, kwoty i daty, mapowanie statusów, przebieg w tle (kolejność, odstęp, przerwanie przy awarii, odmowa), wysyłka mailem z zapisem i dziennikiem, odrzucanie adresów, należności po terminie, uprawnienia kolumn |
| `StocktakeTests` | 14 | Inwentaryzacja: skan liczy sztuki, partie na arkuszu, RW niedoborów i PW nadwyżek na tych samych partiach, kilka RW dla jednego produktu, różnica od stanu przy zatwierdzeniu, warunki zatwierdzenia, odmowa nadwyżki na przeterminowanej partii, walidacja, zamknięcie w serwisie i wyzwalaczem w bazie, anulowanie, współbieżność i kolejność blokad |
| `StocktakePagesTests` | 3 | Ekrany inwentaryzacji: skanowanie, liczenie i zatwierdzenie z potwierdzeniem |
| `CompanyLookupTests` | 2 | Okno kontrahenta wypełnione z udawanej białej listy i zapisane; niedostępny wykaz nie zmienia wpisanych danych |
| `PlanningTests` | 6 | Klienci (kolejność, faktury, marża, ostatni zakup, korekta), porównanie z poprzednim okresem i rokiem wcześniej, prognoza z rezerwacjami, zakres dni, wczytanie stron |

Wynik: 242 z 242 testów przechodzi lokalnie w około 90 s (z uruchomieniem kontenera) i w CI.

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

### Eksperyment 3: rezerwacje w zamówieniach klientów

**Cel.** Zamówienia (poziom 2, `SPEC.md` 5.11) opierają się na dwóch blokadach: zapis zamówienia blokuje wiersze produktów, tak jak dokumenty, a faktura z zamówienia i anulowanie blokują wiersz zamówienia. Nowe testy przeszły za pierwszym razem, więc trzeba było sprawdzić, czy w ogóle potrafią się nie udać.

**Metoda.** Na próbę usuwano po jednej blokadzie: w `CustomerOrderService` zamiast `ProductLocks.LockAsync` zwykły odczyt produktów, a w drugim kroku `FOR UPDATE` przy wierszu zamówienia (w `InvoiceService` i w anulowaniu). Testy współbieżności uruchamiano odpowiednio 5 i 10 razy.

| Usunięte | Test | Wynik |
|---|---|---|
| Blokada produktów przy zapisie zamówienia | `Parallel_orders_never_reserve_more_than_the_stock` | 5 z 5 błędów: 8 zamówień po 3 szt. zamiast 3, czyli 24 szt. zarezerwowane przy stanie 10 |
| | `Parallel_orders_and_wz_never_promise_goods_that_left` | 5 z 5 błędów: zarezerwowano 10 szt., a na stanie zostały 2 albo 0 |
| `FOR UPDATE` na wierszu zamówienia | `Cancelling_and_invoicing_one_order_at_the_same_time_leaves_one_outcome` | 10 z 10 błędów: anulowanie i faktura kończyły się obie sukcesem, czyli klient widział anulowane zamówienie, a towar wydano |

**Przywrócenie.** Oba pliki skopiowano z kopii sprzed eksperymentu; porównanie (`cmp`) nie wykazało różnic. Wszystkie 26 testów zamówień przeszły potem 3 razy z rzędu.

**Wnioski:**

- Rezerwacja to odczyt („ile jest wolne”) i zapis („zarezerwuj”) rozdzielone w czasie, więc bez blokady dwa zamówienia widzą ten sam wolny towar. Ten sam błąd, który chroni blokada dokumentów magazynowych, pojawia się w każdym nowym miejscu, które decyduje na podstawie stanu.
- Stała kolejność blokad (wiersz zamówienia, potem produkty) wystarcza, żeby nowa blokada nie tworzyła zakleszczeń z istniejącymi.

### Eksperyment 4: korekta faktury bez blokady korygowanej faktury

**Cel.** Wystawienie korekty (`SPEC.md` 5.12) zaczyna się od `SELECT … FOR UPDATE` na wierszu korygowanej faktury. Eksperyment sprawdza, czy ta blokada jest potrzebna, skoro bazę i tak chroni unikalny indeks `IX_Invoices_CorrectsInvoiceId`.

**Metoda i wyniki.** W `InvoiceService.CorrectCoreAsync` usunięto `FOR UPDATE` i uruchamiano testy równoległych korekt po 5 razy.

| Test | Bez blokady | Wyjaśnienie |
|---|---|---|
| `Parallel_corrections_of_one_invoice_create_one` (zwrot towaru) | 5 z 5 przechodzi | Zwrot przechodzi przez korektę WZ, która blokuje wiersze produktów i sprawdza, czy WZ ma już korektę. To ona ustawiała korekty w kolejce |
| `Parallel_price_corrections_of_one_invoice_create_one_and_refuse_the_rest_cleanly` (sama cena) | 5 z 5 pada: `DbUpdateException … 23505 … IX_Invoices_CorrectsInvoiceId` | Bez zwrotu towaru nie ma blokady produktów. Druga korekta nie zobaczyła pierwszej, a unikalny indeks zatrzymał ją dopiero przy zapisie, jako błąd zamiast komunikatu |

Drugi test powstał w trakcie eksperymentu, bo pierwszy nie potrafił się nie udać. Plik przywrócono z kopii, `cmp` nie wykazał różnic, a wszystkie testy korekt przeszły.

**Wnioski:**

- Unikalny indeks chroni dane, ale nie użytkownika: bez blokady druga osoba dostałaby stronę błędu zamiast komunikatu „Faktura ma już korektę”.
- Test współbieżności musi pokrywać ścieżkę, na której dane zabezpieczenie jest jedyne. Przy zwrocie towaru inna blokada maskowała brak tej, którą sprawdzano.

### Eksperyment 5: inwentaryzacja i kolejność blokad

**Cel.** Zatwierdzenie arkusza spisu (`SPEC.md` 5.14) księguje w jednej transakcji kilka dokumentów (PW nadwyżek, potem RW niedoborów). Każdy dokument blokuje swoje produkty, więc bez blokady wszystkich produktów arkusza z góry transakcja mogłaby brać blokady nie rosnąco: najpierw produkt z nadwyżką, potem produkt z niedoborem. Zatwierdzenie blokuje więc wszystkie produkty arkusza na początku. Eksperyment sprawdza, czy testy to wykrywają.

**Metoda.** W `StocktakeService.ApproveCoreAsync` zamieniono `ProductLocks.LockAsync` na zwykły odczyt produktów i uruchamiano testy po 5 razy.

| Test | Bez blokady z góry | Wyjaśnienie |
|---|---|---|
| `Parallel_approvals_and_issues_keep_stock_consistent` | 5 z 5 przechodzi | Bez blokady „stan oczekiwany” to stan z chwili odczytu, a RW i tak blokuje produkt przed zmianą stanu, więc liczby się zgadzają. Test nie sprawdzał tego, przed czym blokada chroni |
| Losowy wyścig 8 zatwierdzeń z 8 dokumentami WZ tych samych produktów | 5 z 5 przechodzi | Okno między blokadą produktu z nadwyżką a produktu z niedoborem trwa milisekundy. Dwa zatwierdzenia między sobą i tak idą po kolei przez licznik numerów INW |
| `Approval_locks_all_its_products_before_posting_so_it_cannot_deadlock_with_an_issue` | 5 z 5 pada: `40P01: deadlock detected` | Test sam blokuje produkt X, uruchamia zatwierdzenie (nadwyżka na Y, niedobór na X), a potem prosi o Y. Bez blokady z góry zatwierdzenie trzyma Y i czeka na X; z nią czeka na X, zanim dotknie Y |

Z blokadą trzeci test przeszedł 3 na 3 razy. Losowy wyścig zastąpiono testem deterministycznym. Plik przywrócono z kopii; `cmp` nie wykazał różnic.

**Wnioski:**

- Nie każdą blokadę da się sprawdzić losowym wyścigiem. Gdy okno jest wąskie, trzeba ustawić kolejność zdarzeń w teście: samemu wziąć jedną blokadę i poczekać, aż druga strona utknie.
- Zgodność liczb to nie to samo co brak zakleszczeń. Pierwszy test pilnuje wyniku, trzeci kolejności blokad.

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
| `An_order_reserves_goods_until_its_invoice_takes_them` | Z1 | Stan 10, zamówienie 8, WZ 3, faktura z zamówienia | Ręczny WZ odrzucony w podglądzie („zarezerwowano”), faktura z zamówienia wydaje 8, zamówienie zrealizowane, stan 2 |
| `Invoice_with_goods_and_a_service_issues_a_wz_and_is_sent_in_the_background` | F1 | Towar z rabatem i usługa | WZ tylko dla towaru, status „wysłana” z numerem z udawanej Fakturowni |

### Wynik

13 z 13 testów przechodzi lokalnie i w CI, gdzie przeglądarkę instaluje krok `playwright.ps1 install --with-deps chromium`.

Testy funkcjonalne znalazły błąd, którego nie widziały 300 innych testów: cena „2,50” wpisana w pole zapisywała się jako 250 (punkt 6, błąd 1). Pierwszy zestaw danych S1 był czerwony, dopóki pola liczbowe nie dostały polskiej kultury.

**Niestabilność i jej usunięcie.** Przy pełnym zestawie uruchomionym naraz (testy integracyjne i przeglądarkowe równolegle) w 2 z 4 przebiegów padały 1–2 testy przeglądarkowe, a osobno zawsze przechodziły. Raport TRX i zrzuty ekranu pokazały wyścig z przywracaniem fokusu do pola produktu: tekst dla pola ilości trafiał do pola produktu (opis w `przygody.md`). Pole produktu ma teraz licznik `data-settled`, zwiększany dopiero po powrocie fokusu, a metody pomocnicze testów czekają na niego i na zamknięcie listy podpowiedzi. Po poprawce pełny zestaw przeszedł 4 razy z rzędu, za każdym razem 389 z 389 testów.

Ręcznie, bez automatyzacji, pozostają scenariusze wymagające prawdziwych usług i urządzeń: logowanie Google, skanowanie aparatem telefonu przez tunel, czytnik USB oraz F6 i F7 na koncie testowym Fakturowni.

## 5. Testy wydajności

### Kryteria

Z punktu 10 `SPEC.md`:

1. Analiza ABC i raport sprzedaży poniżej 1 s dla 100 000 ruchów magazynowych.
2. 95% odpowiedzi HTTP poniżej 500 ms przy 20 równoczesnych użytkownikach.
3. 0% błędów podczas testu.

### Środowisko i dane

- Laptop: Intel Core i5-13420H (8 rdzeni, 12 wątków), Windows 11 25H2, .NET 10.0.14, aplikacja w konfiguracji Release.
- PostgreSQL 18.6 w Docker Desktop (12 procesorów, 8 GB pamięci dla maszyny wirtualnej), ustawienia domyślne (`shared_buffers` 128 MB, `work_mem` 4 MB). Osobna, jednorazowa baza na porcie 55432.
- Aplikacja, baza i generator obciążenia działają na tym samym komputerze, więc wyniki są ostrożne: na serwerze z osobną bazą byłyby lepsze.
- Dane z generatora (`--products 10000 --customers 400 --suppliers 20 --days 365 --invoices-per-day 100 --seed 2026 --prefix PERF`), wytworzone przez prawdziwe serwisy aplikacji w 33 minuty:

| Tabela | Wierszy |
|---|---:|
| `StockMovements` (ruchy magazynowe) | **136 962** |
| `InvoiceLines` | 67 793 |
| `Batches` i `Stock` | po 41 247 |
| `StockDocuments` | 31 177 (WZ 27 488, PZ 3290, PW 219, RW 180) |
| `Invoices` | 27 459 (286 anulowanych), od 9.11.2025 do 8.11.2026 |
| `Products` | 10 000 (9543 towary, 457 usług) |
| `Contractors` | 420 |

Baza zajmuje 97 MB. Pierwsze generowanie ujawniło błąd generatora, który dawał tylko 54 640 ruchów (punkt 6, błąd 3). Liczby powyżej pochodzą z danych wygenerowanych po poprawce.

### Pomiar zapytań (BenchmarkDotNet)

`tests/performance/Regal.Benchmarks` wywołuje metody `AnalyticsService` tak jak strony, dla okresu jednego roku: 2 iteracje rozgrzewające i 10 mierzonych, `MemoryDiagnoser`. Przed pomiarem wykonano `ANALYZE`, czyli odświeżono statystyki planisty.

| Zapytanie | Przed | Po | Przydział pamięci |
|---|---:|---:|---:|
| Podsumowanie (sprzedaż, koszt, liczba faktur) | 33,5 ms | 34,1 ms | 70 KB |
| Sprzedaż w czasie | 21,4 ms | 21,7 ms | 173 KB |
| Sprzedaż według produktów (50 pierwszych) | 62,4 ms | 62,8 ms | 95 KB |
| Sprzedaż według kategorii | 30,9 ms | 31,1 ms | 61 KB |
| **Analiza ABC** (7488 produktów) | **44,2 ms** | **44,3 ms** | 3,3 MB |
| **Zalegające** (90 dni, 200 pierwszych) | **586,3 ms** | **37,3 ms** | 186 → 204 KB |
| Partie z bliską datą ważności | 26,8 ms | 27,2 ms | 155 KB |

Odchylenie standardowe każdego pomiaru było poniżej 2% średniej. Kryterium 1 jest spełnione z dużym zapasem już przed optymalizacją: ABC 44 ms, a cały raport sprzedaży (cztery zapytania strony) około 150 ms. Jedynym zapytaniem wyraźnie odstającym był raport towarów zalegających, z którego korzysta też pulpit.

### Plan zapytania i optymalizacja

`EXPLAIN (ANALYZE, BUFFERS)` dla zapytania o zalegające, z SQL-em wygenerowanym przez EF Core (wypisuje go `Regal.Benchmarks -- --sql`):

**Przed:** 700 ms, 830 581 odczytów stron z pamięci podręcznej. Zapytanie LINQ liczyło stan i datę ostatniej sprzedaży jako właściwości każdego produktu. EF Core przetłumaczył je na podzapytania skorelowane i powtórzył je w filtrze, w sortowaniu i w wyniku:

```text
Seq Scan on "Products" p  (actual time=0.220..367.640 rows=5609 loops=1)
  Filter: ((Kind = 'Goods') AND ((SubPlan 4) > 0.0) AND (((SubPlan 5) IS NULL) OR ((SubPlan 6) < '2026-08-12')))
  SubPlan 4  -> Aggregate ... loops=9543     (stan produktu, w filtrze)
  SubPlan 5  -> Aggregate ... loops=9542     (ostatnia sprzedaż, w filtrze)
  SubPlan 6  -> Aggregate ... loops=7142     (ostatnia sprzedaż, drugi raz)
  SubPlan 3  -> Aggregate ... loops=5609     (stan produktu, w kluczu sortowania)
```

Dla każdego z 9543 towarów baza wykonywała kilka podzapytań z pętlą zagnieżdżoną: łącznie 41 247 odczytów stanu i 65 129 odczytów faktur w samym filtrze.

**Zmiana:** stan na produkt i ostatnia sprzedaż na produkt są liczone raz, jako dwa zestawienia (`GROUP BY`), i łączone z produktami. Przy pierwszej próbie EF Core nadal tworzył podzapytanie dla daty, bo w `Max` była nawigacja `l.Invoice.IssueDate`. Pomogło dopiero jawne złączenie z tabelą faktur (`AnalyticsService.GetSlowMovingAsync`, komentarz w kodzie).

**Po:** 54 ms, 2249 odczytów stron. Plan to złączenia haszujące i jedno sortowanie:

```text
Limit -> Sort (top-N heapsort)
  -> Hash Left Join (p.Id = s1.ProductId)  Filter: LastSale IS NULL OR LastSale < '2026-08-12'
       -> Hash Join (p.Id = s0.ProductId)
            -> Seq Scan on Products
            -> HashAggregate (GROUP BY b.ProductId) over Hash Join Stock x Batches
       -> HashAggregate (GROUP BY ProductId) over Hash Join InvoiceLines x Invoices
```

**Poprawność:** wynik nowego zapytania porównano wiersz po wierszu z wynikiem starego na tych samych danych, dla 90 i 60 dni (strona i pulpit): oba zestawy po 200 wierszy były identyczne. Wszystkie testy analiz przechodzą. Doszedł test, że anulowana nowsza sprzedaż nie zmienia daty ostatniej sprzedaży, a kolejność idzie według wartości zapasu.

**Indeksy.** `SPEC.md` zakładał dobór indeksów pod analizy na podstawie pomiarów. Pomiary pokazały, że nowe indeksy nie są potrzebne:

- Analizy roczne czytają prawie wszystkie wiersze (67 021 z 67 793 pozycji faktur), więc planista słusznie wybiera odczyt sekwencyjny ze złączeniem haszującym. Indeks nic by tu nie dał.
- Pulpit liczy bieżący miesiąc i korzysta z istniejącego indeksu `IX_Invoices_IssueDate`. Eksperyment w transakcji wycofanej na końcu: po usunięciu obu indeksów na dacie wystawienia podsumowanie miesiąca trwało 11,0 ms zamiast 7,7 ms. Indeks pomaga, ale przy 27 tysiącach faktur różnica jest niewielka.
- Problemem raportu zalegających był kształt zapytania, a nie brak indeksu: podzapytania korzystały z indeksów i i tak trwały 700 ms.

ABC grupuje 67 tysięcy wierszy z sortowaniem, które przy `work_mem` 4 MB zapisuje 340 stron na dysk tymczasowy. Przy 44 ms nie warto tego zmieniać.

### Test obciążenia HTTP

**Co mierzy.** Każdy z 20 wirtualnych użytkowników ma własne ciasteczko logowania testowego i po kolei wczytuje 8 stron (pulpit, stany, dokumenty, faktury, produkty, sprzedaż, ABC, zalegające), z przerwą 0,5 s między stronami, przez 120 s. Mierzony jest czas pełnego wczytania strony: serwer wykonuje zapytania i renderuje HTML (prerendering). Odpowiedź jest poprawna, gdy ma status 200 i zawiera przycisk „Wyloguj”. Test nie mierzy zdarzeń po wczytaniu (kliknięć, zmian filtrów), które w trybie Interactive Server idą przez SignalR; ich koszt to te same zapytania z tabeli wyżej, bez renderowania całej strony.

Strony analiz liczą cały rok, a każdy użytkownik otwiera je co kilka sekund. To obciążenie znacznie cięższe niż prawdziwa praca kilku pracowników, którzy głównie wystawiają dokumenty.

**Narzędzia.** Skrypt k6 (`tests/performance/k6/pages.js`) ma kryteria wpisane jako progi: `http_req_duration p(95)<500` i `http_req_failed rate==0`. Ten sam scenariusz mierzy `Regal.LoadHost --measure` w C#. Wyniki niżej pochodzą z `Regal.LoadHost`, bo pobranie k6 na tym komputerze nie było możliwe z sesji, w której powstawały pomiary. Uruchomienie k6 opisuje README.

| Strona | Przed: p50 | Przed: p95 | Po: p50 | Po: p95 | Po: p99 |
|---|---:|---:|---:|---:|---:|
| Pulpit `/` | 1659 | 2551 | 141 | 299 | 471 |
| Stany `/stock` | 12 | 28 | 17 | 52 | 81 |
| Dokumenty `/stock-documents` | 11 | 29 | 16 | 50 | 76 |
| Faktury `/invoices` | 11 | 28 | 16 | 33 | 87 |
| Produkty `/products` | 10 | 21 | 12 | 27 | 68 |
| Sprzedaż `/analytics/sales` | 310 | 485 | 222 | 371 | 413 |
| ABC `/analytics/abc` | 112 | 243 | 106 | 493 | 770 |
| Zalegające `/analytics/slow-moving` | 1486 | 2134 | 78 | 149 | 286 |
| **Wszystkie** | **46** | **1905** | **57** | **286** | **439** |
| Żądań w 120 s | | 2318 | | 3689 | |
| Błędów | | 0 | | 0 | |
| Kryteria | | **niespełnione** | | **spełnione** | |

Czasy w milisekundach. Oba przebiegi mają tę samą konfigurację (Server GC, opis niżej) i różnią się tylko zapytaniem o zalegające.

**Omówienie:**

- Pod obciążeniem wolne zapytanie kosztowało znacznie więcej niż w pojedynczym pomiarze: 586 ms w benchmarku, a mediana strony 1486 ms. Równoczesne kopie zajmowały procesory bazy i spowalniały wszystkie strony analiz. Po poprawce pulpit przyspieszył 11 razy w medianie, a serwer obsłużył o 59% więcej żądań w tym samym czasie.
- Po poprawce szybkie strony (stany, faktury) są nieco wolniejsze niż przed, bo w tym samym czasie obsługiwanych jest więcej żądań stron analiz. Wszystkie nadal mieszczą się w 100 ms dla p95.
- Najwolniejszą stroną po poprawce jest ABC (p95 493 ms). Liczy wszystkie 7488 sprzedanych produktów i przydziela 3,3 MB na żądanie. Jest tuż pod progiem, więc przy większych danych to następny kandydat do optymalizacji, np. przez liczenie klas w SQL funkcjami okna albo pamięć podręczną wyniku na kilka minut.

### Pomiar, który mierzył co innego: Workstation GC w hoście testowym

Pierwszy pomiar „po” dał p95 573 ms, a powtórzony 439 ms. Pierwszy był dodatkowo zakłócony: jedno żądanie zanotowało 1 046 221 ms w 120-sekundowym teście, co wskazuje na zatrzymanie całego komputera, np. uśpienie, więc go odrzucono. Strona ABC miała p95 1187–1542 ms, choć jej zapytanie trwa 44 ms. Wyjaśnienie dał `dotnet-counters` (punkt 6). Tu sam wynik:

| Licznik (średnio na 2 s) | Workstation GC | Server GC |
|---|---:|---:|
| Czas przerw GC | 0,60 s (30% czasu) | 0,03 s (1,5%) |
| Odśmiecania gen0 / gen1 / gen2 | 17,4 / 7,9 / 3,1 | 1,1 / 1,4 / 0,6 |
| Przydział pamięci | 161 MB | 182 MB |
| Czas procesora (użytkownika) | 3,2 s | 2,9 s |

`Regal.LoadHost` to aplikacja konsolowa, więc domyślnie działała z Workstation GC. Prawdziwa aplikacja korzysta z SDK `Microsoft.NET.Sdk.Web`, który włącza Server GC (`"System.GC.Server": true` w `Regal.Web.runtimeconfig.json`). Host testowy ma teraz `ServerGarbageCollection=true` w pliku projektu. Bez tego test mierzył przerwy odśmiecania, których działająca aplikacja nie ma.

### Wnioski

| Kryterium | Wynik | Spełnione |
|---|---|---|
| ABC i raport sprzedaży < 1 s dla 100 000 ruchów | 44 ms i ok. 150 ms przy 136 962 ruchach | Tak |
| p95 < 500 ms przy 20 użytkownikach | 286 ms (przed poprawką 1905 ms) | Tak, po optymalizacji |
| 0% błędów | 0 z 3689 żądań | Tak |

- Benchmark pojedynczego zapytania nie wystarczy: 586 ms mieści się w kryterium 1 s, a pod obciążeniem to samo zapytanie podniosło p95 wszystkich stron do 1,9 s.
- LINQ, który dobrze czyta się w C#, może dać zły SQL. Bez sprawdzenia planu nie było tego widać, bo testy sprawdzają wynik, a nie sposób jego obliczenia.
- Środowisko testu musi odpowiadać produkcji także w ustawieniach środowiska uruchomieniowego, nie tylko w kodzie.

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

### Błąd 5: skoki czasu odpowiedzi w teście obciążenia (`dotnet-counters`, `dotnet-trace`)

- **Objaw:** w teście 20 użytkowników strona ABC miała p95 1187–1542 ms, a jej zapytanie w benchmarku trwało stabilnie 44 ms. Dwa przebiegi tego samego testu dały p95 wszystkich stron 573 i 439 ms.
- **Ślad wykonania:** `dotnet-trace collect -p <pid> --profile dotnet-common,dotnet-sampled-thread-time --duration 00:00:00:25` w trakcie testu, a potem `dotnet-trace report load.nettrace topN`. Wątki puli przez 55% czasu czekały na pracę (`LowLevelLifoSemaphore.WaitForSignal`), kolejka puli była pusta, a własny kod aplikacji (np. `AbcClassifier.Classify`) zajmował poniżej 1% próbek. Procesor aplikacji nie był więc przeciążony. W śladzie widać było natomiast finalizatory i `PollGC`, czyli pracę odśmiecacza.
- **Liczniki:** `dotnet-counters collect -p <pid> --counters System.Runtime --format csv` przez 30 s pokazał 0,6 s przerw GC na każde 2 s i 3 pełne odśmiecania (gen2) na 2 s.
- **Przyczyna:** host testu obciążenia był aplikacją konsolową z Workstation GC, a `Regal.Web` działa z Server GC (`runtimeconfig.json`). Wyniki opisywały więc host testowy, a nie aplikację.
- **Poprawka:** `ServerGarbageCollection=true` w `Regal.LoadHost.csproj`. Przerwy GC spadły do 0,03 s na 2 s, p95 strony ABC do 493 ms, a wszystkich stron do 286 ms. Pomiar „przed” optymalizacją powtórzono z tym samym ustawieniem (punkt 5).
- **Przy okazji:** `Regal.LoadHost` uruchomiony przez `dotnet run` w ogóle nie startował (`DirectoryNotFoundException: …\regal\Regal.Web\`). W kodzie źródłowym `WebApplicationFactory` znalazłem przyczynę: plik `MvcTestingAppManifest.json` ze ścieżką projektu jest szukany w bieżącym katalogu, a runner testów, w odróżnieniu od `dotnet run`, startuje w katalogu wyjściowym. Host ustawia teraz bieżący katalog na `AppContext.BaseDirectory`.

### Wnioski z debugowania

- Cztery z pięciu błędów nie dawały wyjątku, tylko złe liczby. Dlatego testy sprawdzają wartości (średnią, liczbę żądań, liczbę faktur), a nie tylko brak błędu.
- W testach komponentów trzeba rozumieć, jak narzędzie wysyła zdarzenia, bo inaczej szuka się błędu w aplikacji, który jest w teście.
- Narzędzie, które pisze do bazy, powinno najpierw pokazać, dokąd pisze.

## 7. Dobór narzędzi

Porównanie wybranych narzędzi z alternatywami i uzasadnienie wyboru jest w [`narzedzia.md`](narzedzia.md), w sekcji „Testy”. W skrócie:

- **xUnit + bUnit + FakeTimeProvider** dla reguł i komponentów: szybkie, deterministyczne, bez przeglądarki.
- **Testcontainers z PostgreSQL**, a nie baza w pamięci, bo testowane mechanizmy (`FOR UPDATE`, `SKIP LOCKED`, uprawnienia, `CHECK`) istnieją tylko w prawdziwej bazie. Eksperymenty z punktu 3 pokazują, że te testy potrafią się nie udać.
- **Playwright**, bo automatycznie czeka na elementy. Ma to znaczenie w Blazorze, gdzie strona staje się interaktywna chwilę po wczytaniu.
- **BenchmarkDotNet i `EXPLAIN (ANALYZE, BUFFERS)`** dla zapytań; **k6** i równoważny pomiar w C# dla HTTP.
- **coverlet** dla pokrycia; do debugowania **testy odtwarzające błąd, logi (SQL z EF Core), zapytania do bazy, `dotnet-counters` i `dotnet-trace`**.
