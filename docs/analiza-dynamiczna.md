# Analiza dynamiczna

Zakres opisuje punkt 10 `SPEC.md`. Dokument jest uzupełniany wraz z kolejnymi etapami.

## Testy integracyjne: czy testy współbieżności wykrywają brak blokady

### Cel

Etap „Magazyn” opiera się na blokadzie `SELECT … FOR UPDATE` na wierszach `Products`. Zielone testy współbieżności niczego nie dowodzą, jeśli przechodzą także bez niej. Eksperyment sprawdza, które testy padają, gdy blokady zabraknie.

### Metoda

W `ProductLocks.LockAsync` usunięto z zapytania same słowa `FOR UPDATE`. Reszta kodu pozostała bez zmian. Pełny zestaw testów integracyjnych (80 testów na PostgreSQL w Testcontainers) uruchomiono 5 razy.

### Wyniki bez blokady

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

### Test, który nie wykrywał problemu

Test `Documents_listing_products_in_opposite_order_do_not_deadlock` w pierwszej wersji przeszedł bez blokady, czyli niczego nie sprawdzał.

- **Przyczyna:** używał wyłącznie dokumentów WZ. Licznik numerów jest zwiększany przed zmianą stanów, a blokada jego wiersza trwa do końca transakcji. Dokumenty tego samego typu i tak czekały więc na siebie.
- **Poprawka:** test na przemian tworzy WZ i RW, które mają osobne liczniki.
- **Wynik po poprawce, bez blokady:** 5 z 5 uruchomień tego testu kończy się błędem `Npgsql.PostgresException: 40P01: deadlock detected`.

### Przywrócenie blokady

Do zapytania wrócił fragment `FOR UPDATE`. Polecenie `git diff --exit-code` dla `ProductLocks.cs` zakończyło się kodem 0, więc plik jest identyczny z wersją z commita. Po przebudowaniu cały zestaw integracyjny przeszedł 5 razy z rzędu (po 80 z 80), a testy jednostkowe 121 z 121.

### Wnioski

- Blokada produktu chroni to, czego nie wyrazi pojedynczy warunek w SQL: średnią, wybór partii i sprawdzenia poprzedzające zapis.
- Warunkowy `UPDATE` i `CHECK` są ostatnią barierą: nawet bez blokady stan nie spadł poniżej zera.
- Numeracja bez luk powoduje, że dokumenty tego samego typu zapisują się po kolei od chwili pobrania numeru. To świadomy koszt tego wymagania.
- Test współbieżności trzeba sprawdzić, usuwając to, co ma chronić. Inaczej nie wiadomo, czy potrafi się nie udać.

## Testy integracyjne: wysyłka faktur bez duplikatów

### Cel

Wysyłkę faktur do Fakturowni chronią dwie warstwy z jednego zabezpieczenia:

- pobranie zadania z `FOR UPDATE SKIP LOCKED`, żeby dwa procesy wysyłki nie wzięły tego samego zadania;
- wyszukanie faktury po `oid` przed jej utworzeniem, żeby ponowienie po zgubionej odpowiedzi nie utworzyło duplikatu.

Udawana Fakturownia w testach, jak prawdziwa przy `oid_unique=yes`, odrzuca drugą fakturę z tym samym `oid`. Eksperyment sprawdza, czy testy wykrywają brak każdego z tych zabezpieczeń.

### Metoda

W `InvoiceSender` na próbę usuwano po jednym zabezpieczeniu i uruchamiano 28 testów faktur 3 razy.

### Wyniki

| Usunięte | Pierwsza wersja testów | Po wzmocnieniu testów |
|---|---|---|
| `FOR UPDATE SKIP LOCKED` | Wszystkie testy przeszły 3 z 3 razy | `Parallel_senders_send_each_invoice_exactly_once`: 28–31 żądań utworzenia zamiast 12, 3 z 3 |
| Wyszukanie po `oid` przed utworzeniem | Przeszedł test zgubionej odpowiedzi, padły 3 inne testy, ale z innego powodu (opis niżej) | `Lost_reply_after_a_successful_create…`: 2 żądania utworzenia zamiast 1, 3 z 3 |

**Dlaczego pierwsza wersja nie wykrywała braku zabezpieczeń.** Testy sprawdzały tylko efekt końcowy: jedna faktura w Fakturowni. Ten efekt zapewniało też odrzucanie duplikatów `oid` przez udawaną Fakturownię, więc brak blokady albo wyszukania niczego nie psuł. Wzmocnione testy sprawdzają, że do Fakturowni trafia dokładnie jedno żądanie utworzenia na fakturę, łącznie z odrzuconymi.

**Trzy testy, które padły przy usuniętym wyszukaniu w pierwszej wersji,** nie wykrywały duplikatu. Dwa wstrzykiwały błąd właśnie w wyszukiwanie, którego już nie było. Trzeci zależał od kolejności wywołań udawanego klienta.

### Przywrócenie

Usunięte fragmenty przywrócono. `git diff --exit-code` dla katalogu `src` zwrócił 0, czyli kod jest identyczny z commitem. Pełny zestaw przeszedł potem 3 razy z rzędu: 199 testów jednostkowych i 124 integracyjne.

### Wnioski

- Jeśli zabezpieczeń jest kilka, test efektu końcowego nie mówi, czy działa każde z nich. Trzeba mierzyć to, czemu dane zabezpieczenie zapobiega, tutaj liczbę żądań.
- Działanie `oid_unique` w prawdziwej Fakturowni nie jest jeszcze potwierdzone, sprawdzi to część D na koncie testowym. Do tego czasu duplikatom zapobiegają blokada i wyszukanie, a nie Fakturownia.
