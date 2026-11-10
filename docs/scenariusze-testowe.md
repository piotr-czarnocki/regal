# Scenariusze testów ręcznych: magazyn

Materiał do testów funkcjonalnych (punkt 4 tabeli w sekcji 10 `SPEC.md`). Scenariusze wykonuje się po kolei, bo kolejne korzystają ze stanów utworzonych wcześniej. Wyniki zależą od pustego magazynu na początku.

## Dane przygotowawcze

Przed pierwszym scenariuszem wykonaj `dotnet ef database update --project src/Regal.Web`, a potem utwórz w kartotekach:

| Rodzaj | Dane |
|---|---|
| Kategoria | Nabiał |
| Kontrahent | Hurtownia Mleczna (dostawca), Sklep Pod Lipą (odbiorca) |
| Towar | Mleko 1L: SKU `MLEKO1L`, jednostka `szt`, ma datę ważności |
| Towar | Cukier: SKU `CUKIER`, jednostka `kg`, bez daty ważności |
| Towar | Jogurt: SKU `JOGURT`, jednostka `szt`, ma datę ważności |
| Usługa | Montaż: SKU `MONTAZ` |

Oznaczenie D+30 to data o 30 dni późniejsza od dzisiejszej.

## S1. Przyjęcie tworzy partię, stan i cenę średnią

**Dane:** PZ od Hurtowni Mlecznej, Mleko 1L, partia `MLE-1`, ważność D+30.

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | „Nowy dokument”, typ PZ, dostawca Hurtownia Mleczna, dodaj Mleko 1L: ilość 100, cena 2,50, partia `mle-1`, data D+30 | Pozycja widoczna na liście |
| 2 | „Pokaż podgląd”, „Zatwierdź”, potwierdź w oknie | Szczegóły dokumentu `PZ/{numer}/{rok}`, partia zapisana jako `MLE-1` |
| 3 | „Stany magazynowe” | Mleko 1L: 100 szt, średnia 2,5000 zł, wartość 250,00 zł |
| 4 | Drugi PZ: Mleko 1L, ilość 50, cena 3,10, partia `MLE-1`, ta sama data | Rozwinięcie produktu pokazuje jedną partię `MLE-1` ze 150 szt |
| 5 | „Stany magazynowe” | Średnia 2,7000 zł, wartość 405,00 zł |

## S2. Walidacja przyjęcia

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | PZ bez dostawcy, Mleko 1L, „Pokaż podgląd” | Komunikat „Wybierz dostawcę.”, podgląd się nie pojawia |
| 2 | Dostawca jak w S1, ilość 1,5 szt, cena 2,50, „Pokaż podgląd” | Komunikat o liczbie całkowitej w podglądzie, „Zatwierdź” zablokowany |
| 3 | Ilość 1, cena 2,50, partia `MLE-1`, data D+31, zatwierdź | Komunikat „partia MLE-1 ma inną datę ważności”, stan bez zmian |
| 4 | Data ważności wczorajsza, zatwierdź | Komunikat o dacie wcześniejszej niż data dokumentu |
| 5 | W polu produktu wpisz „Montaż” | Usługa nie pojawia się na liście podpowiedzi |

## S3. Przychód wewnętrzny bez kosztu

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | PW, Cukier, 10 kg, bez kosztu, zatwierdź | Komunikat „podaj koszt, bo bieżąca cena średnia wynosi 0” |
| 2 | Podaj koszt 4,00, zatwierdź | Dokument PW, stan 10 kg, średnia 4,0000 zł, partia z numerem dokumentu |
| 3 | Drugi PW: Cukier, 5,5 kg, koszt pusty, zatwierdź | Stan 15,5 kg, średnia bez zmian, ruch z kosztem 4,0000 zł |

## S4. Wydanie automatyczne według daty ważności

**Dane:** dodatkowy PZ: Mleko 1L, 20 szt, cena 2,70, partia `MLE-2`, ważność D+10.

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | WZ do Sklepu Pod Lipą, Mleko 1L, ilość 30, partia „Automatycznie”, „Pokaż podgląd” | Dwa wiersze: `MLE-2` 20 szt, potem `MLE-1` 10 szt |
| 2 | Zatwierdź, potwierdź | Szczegóły: dwa ruchy -20 i -10 po 2,7000 zł, razem -81,00 zł |
| 3 | Rozwiń Mleko 1L w stanach | `MLE-2` zniknęła, `MLE-1` ma 140 szt, średnia nadal 2,7000 zł |

## S5. Wybór ręczny i partia przeterminowana

**Dane:** poprzedniego dnia PZ: Jogurt, 10 szt, cena 2,00, partia `JOG-1`, ważność równa tamtej dacie. Scenariusz wykonaj następnego dnia.

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | WZ, Mleko 1L, ilość 5, wybierz partię `MLE-1`, zatwierdź | Ruch tylko z `MLE-1` |
| 2 | „Stany magazynowe” | Jogurt z oznaczeniem „przeterminowane”, wiersz partii wyróżniony |
| 3 | WZ, Jogurt, rozwiń listę „Partia” | `JOG-1` oznaczona „przeterminowana”, nie można jej wybrać |
| 4 | WZ, Jogurt, ilość 1, „Automatycznie”, „Pokaż podgląd” | Błąd braku towaru z informacją o przeterminowanych, „Zatwierdź” zablokowany |

## S6. Rozchód wewnętrzny

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | RW, Jogurt, ilość 10, bez powodu, „Pokaż podgląd” | Komunikat o konieczności podania powodu |
| 2 | Powód „Przeterminowany”, zatwierdź | RW zdejmuje towar z `JOG-1`, stan Jogurtu 0, produkt znika z listy stanów |

## S7. Brak towaru przy równoczesnej pracy i ciągłość numeracji

Do wykonania w dwóch kartach przeglądarki.

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | W obu kartach WZ: Cukier, 10 kg, „Pokaż podgląd” | Oba podglądy poprawne (stan 15,5 kg) |
| 2 | Zatwierdź w karcie A, zapamiętaj numer | Dokument `WZ/n/{rok}` |
| 3 | Zatwierdź w karcie B | Komunikat o braku towaru, brak nowego dokumentu, „Zatwierdź” zablokowany do nowego podglądu |
| 4 | W karcie B zmień ilość na 5, podgląd, zatwierdź | Dokument `WZ/n+1/{rok}`, bez luki w numeracji, stan 0,5 kg |

## S8. Czytnik i klawisz Enter

**Dane:** kod EAN Mleka 1L z listy produktów oraz dwa towary o nakładających się kodach: SKU `1`, nazwa BOGDAN i SKU `10`, nazwa 1212.

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Nowa WZ, zeskanuj kod czytnikiem USB (albo wpisz i naciśnij Enter) | Pozycja Mleko 1L, ilość 1, pole produktu puste i aktywne, dokument niezatwierdzony |
| 2 | Zeskanuj ten sam kod jeszcze raz | Ta sama pozycja, ilość 2 |
| 3 | Wpisz `5900000000000`, Enter | Komunikat, że nie znaleziono towaru, brak nowej pozycji |
| 4 | Naciśnij Enter w polu ilości | Nic się nie dzieje, dokument niezatwierdzony |
| 5 | „Pokaż podgląd”, „Zatwierdź”, potwierdź i szybko kliknij „Zatwierdź” ponownie | Przycisk zablokowany na czas zapisu, powstaje jeden dokument |
| 6 | Nowy dokument PW, wpisz `1`, Enter | Pozycja BOGDAN, mimo że „1” jest też fragmentem kodów innych towarów; pole puste, kursor w polu |
| 7 | Wpisz `10`, Enter | Druga pozycja: 1212 |
| 8 | Wpisz pełny EAN towaru BOGDAN, Enter | Bez nowej pozycji, ilość BOGDAN wynosi 2 |
| 9 | Wpisz fragment pasujący do kilku towarów, który nie jest niczyim kodem, Enter | Lista do wyboru, żadna pozycja nie zostaje dodana |
| 10 | Wpisz `nieistniejacy`, Enter | Komunikat „Nie znaleziono produktu o kodzie nieistniejacy”, tekst zaznaczony w polu, następny skan go zastępuje |

## S9. Korekty

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Otwórz WZ z S4, „Korekta”, potwierdź | Nowy dokument WZ z ilościami +20 i +10, informacja „Dokument skorygowany dokumentem…”, przycisk znika |
| 2 | Otwórz korektę | Link do oryginału, brak przycisku „Korekta” |
| 3 | „Stany magazynowe”, rozwiń Mleko 1L | `MLE-2` ma znowu 20 szt, `MLE-1` jest większa o 10, średnia 2,7000 zł |
| 4 | Otwórz PZ z S4 (`MLE-2`) i skoryguj | Korekta przechodzi, bo stan partii pokrywa dokument |
| 5 | Nowy PZ: Mleko 1L, 10 szt, cena 2,70, partia `MLE-3`, ważność D+5. Potem WZ 1 szt z partii `MLE-3` i próba korekty tego PZ | Komunikat „towar z partii MLE-3 został już wydany”, stan partii nadal 9 szt |

## S10. Stany, filtry, nieaktywny produkt i telefon

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | „Stany magazynowe”, filtr „Ważność do” 40 dni | Widoczne tylko produkty z partią ważną najwyżej 40 dni, także przeterminowane |
| 2 | Filtr kategorii i wyszukiwanie po SKU | Lista zawęża się zgodnie z filtrami |
| 3 | Kliknij nazwę produktu | Historia ruchów, najnowsze na górze, linki do dokumentów |
| 4 | „Produkty i usługi”, dezaktywuj Mleko 1L | Komunikat o stanie magazynowym, produkt pozostaje aktywny |
| 5 | Wydaj cały Cukier, dezaktywuj go, skoryguj ostatnie wydanie | Korekta przechodzi, Cukier w stanach z oznaczeniem „nieaktywny” |
| 6 | „Dokumenty magazynowe”: filtr typu, dat, szukanie po numerze i po kontrahencie | Lista zawęża się zgodnie z filtrami |
| 7 | Powtórz kroki 1 i 3 oraz S4 kroki 1–2 na telefonie | Tabele układają się w karty, nic nie wychodzi poza ekran, zatwierdzenie działa |

# Scenariusze testów ręcznych: faktury i skanowanie aparatem

Scenariusze F1–F5 nie wymagają Fakturowni: bez konfiguracji faktura czeka w kolejce. F6 i F7 wymagają konta testowego Fakturowni (README, sekcja „Fakturownia”), a F7 dodatkowo tunelu do telefonu.

**Dane:** stany z S1–S10, kontrahent Sklep Pod Lipą z poprawnym NIP oraz usługa Montaż (cena netto 100,00 zł, VAT 23%).

## F1. Wystawienie faktury z towarem i usługą

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | „Nowa faktura”, nabywca Sklep Pod Lipą, zeskanuj Mleko 1L, wpisz `MONTAZ` i Enter | Dwie pozycje; przy usłudze oznaczenie „usługa” |
| 2 | Mleko: ilość 10, rabat 10%; Montaż: ilość 1,5 | Suma brutto na dole przelicza się od razu |
| 3 | „Pokaż podgląd” | Mleko: partia dobrana według ważności; Montaż: „usługa, bez wydania” |
| 4 | „Wystaw fakturę”, potwierdź | Szczegóły faktury ze statusem „Do wysłania” i linkiem do nowego WZ |
| 5 | Otwórz WZ | Wydanie tylko Mleka, 10 szt, z kosztem równym cenie średniej |

## F2. Brak towaru wycofuje całą fakturę

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | W dwóch kartach przygotuj fakturę na cały stan Mleka, w obu „Pokaż podgląd” | Oba podglądy poprawne |
| 2 | Wystaw w karcie A, potem w karcie B | B: komunikat o braku towaru, brak nowej faktury i brak nowego numeru WZ |

## F3. Podwójne kliknięcie i Enter

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | W nowej fakturze naciśnij Enter w polu ilości i w polu rabatu | Nic się nie wystawia |
| 2 | Potwierdź wystawienie i szybko kliknij „Wystaw fakturę” jeszcze raz | Przycisk zablokowany, powstaje jedna faktura |

## F4. Anulowanie faktury, której nie ma w Fakturowni

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Bez skonfigurowanej Fakturowni otwórz fakturę z F1, „Anuluj fakturę” | Komunikat, że nie można sprawdzić faktury w Fakturowni, faktura bez zmian |
| 2 | Otwórz WZ tej faktury i kliknij „Korekta” | Komunikat, że dokument należy do faktury |

## F5. Lista faktur

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | „Faktury”: filtr statusu „Do wysłania”, filtr dat, szukanie po nazwie nabywcy i po NIP | Lista zawęża się zgodnie z filtrami |

## F6. Wysyłka do konta testowego Fakturowni

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Skonfiguruj Fakturownię według README, uruchom aplikację | W ciągu kilku sekund faktura z F1 ma status „Wysłana”, numer i przycisk „PDF” |
| 2 | Otwórz PDF | Nabywca, NIP, pozycje i sumy zgodne z Regałem; brak ostrzeżenia o różnicy sum |
| 3 | W Fakturowni odszukaj fakturę i sprawdź pole „numer zamówienia” | Zawiera `regal-…`, takie samo jak w Regale |
| 4 | Wystaw nową fakturę i zaraz po potwierdzeniu odłącz sieć na 2 minuty | Status „Do wysłania” z ostatnim błędem; po przywróceniu sieci „Wysłana” i tylko jedna faktura w Fakturowni |
| 5 | Wystaw fakturę dla kontrahenta z błędnym adresem e-mail, jeśli Fakturownia go odrzuca | „Błąd wysyłki” z treścią odmowy; po poprawieniu danych w Fakturowni „Ponów wysyłkę” |
| 6 | Spróbuj anulować wysłaną fakturę | Przycisku „Anuluj fakturę” nie ma |

## F7. Skanowanie aparatem telefonu

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Na komputerze przez `https://localhost:7180` kliknij przycisk aparatu | Prośba o zgodę na aparat; po odmowie komunikat „Brak zgody na użycie aparatu” |
| 2 | Przez `http://localhost:5180` kliknij przycisk aparatu | Komunikat, że aparat działa tylko przez HTTPS |
| 3 | Na telefonie przez tunel (README) zaloguj się, „Nowy dokument” → aparat → zeskanuj EAN Mleka 1L | Okno się zamyka, pojawia się pozycja Mleko 1L |
| 4 | Zeskanuj ten sam kod jeszcze raz | Ilość 2 |
| 5 | „Stany magazynowe” → aparat → zeskanuj kod | Lista zawęża się do zeskanowanego produktu |
| 6 | W narzędziach przeglądarki (zakładka sieci) sprawdź pobierane pliki przy pierwszym skanie | `ponyfill.js` i `zxing_reader.wasm` z domeny aplikacji albo wcale (gdy przeglądarka ma własny czytnik), nic z CDN |

# Scenariusze testów ręcznych: analizy i generator

**Dane:** baza po scenariuszach powyżej oraz po uruchomieniu generatora (README, sekcja „Przykładowe dane”).

## A1. Generator danych

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Uruchom generator bez `--environment Development` | Komunikat, że generator działa tylko w Development albo Testing; baza bez zmian |
| 2 | Uruchom z `--environment Development` | Postęp co 30 dni, na końcu podsumowanie z liczbą faktur i ruchów |
| 3 | Uruchom drugi raz bez zmiany `--prefix` | Komunikat, że dane z tym prefiksem już istnieją |
| 4 | W aplikacji otwórz „Faktury” | Faktury z numerami `DEMO …`, ze statusem „Wysłana”, bez przycisku PDF |
| 5 | Przy skonfigurowanej Fakturowni poczekaj minutę | Żadna faktura `DEMO` nie trafia do Fakturowni |

## A2. Pulpit

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Otwórz „Pulpit” | Sprzedaż netto, marża z procentem, liczba faktur i wykres dni bieżącego miesiąca |
| 2 | Wystaw fakturę z F1 i wróć na pulpit | Sprzedaż i marża rosną o wartości z tej faktury |
| 3 | Anuluj fakturę i wróć na pulpit | Sprzedaż wraca do poprzedniej wartości |
| 4 | Sprawdź tabelę partii z bliską datą ważności | Partie do 14 dni, przeterminowane oznaczone na czerwono |

## A3. Sprzedaż i marża, ABC

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | „Sprzedaż i marża”, ostatnie 12 miesięcy | Wykres miesięczny, tabele według kategorii i produktów; suma tabel zgodna z kartą „Sprzedaż netto” |
| 2 | Zmień okres na jeden tydzień | Wykres przechodzi na dni |
| 3 | Wybierz kategorię, potem klienta | Wartości zawężają się do wybranej kategorii i klienta |
| 4 | „Analiza ABC” za rok | Klasa A to niewielka część produktów dająca około 80% sprzedaży; udział narastający rośnie do 100% |

## A4. Towary zalegające

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | „Towary zalegające”, 90 dni | Towary ze stanem, bez sprzedaży od 90 dni albo nigdy niesprzedane („nigdy”), od największej wartości |
| 2 | Zmień liczbę dni na 0 | Lista obejmuje wszystkie towary ze stanem, których nie sprzedano dzisiaj |
| 3 | Kliknij nazwę produktu | Historia ruchów potwierdza datę ostatniego wydania |

# Scenariusze testów ręcznych: zamówienia klientów (poziom 2)

Reguły opisuje `SPEC.md`, punkt 5.11. Scenariusz Z1 jest zautomatyzowany testem w przeglądarce (`OrderScenarioTests`), a reguły Z2–Z4 sprawdzają testy integracyjne `OrderTests`.

**Dane:** Chleb (szt) ze stanem 10, kontrahent Sklep Pod Lipą.

## Z1. Rezerwacja i faktura z zamówienia

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | „Nowe zamówienie”, nabywca Sklep Pod Lipą, zeskanuj Chleb, ilość 8, „Zapisz zamówienie”, potwierdź | Szczegóły zamówienia `ZK/n/rok`, status „otwarte”, przy Chlebie „dostępne dla zamówienia: 10 szt” |
| 2 | „Stany magazynowe”, wyszukaj Chleb | Ilość 10 szt i oznaczenie „zarezerwowano 8” |
| 3 | Nowy dokument WZ: Chleb, ilość 3, „Pokaż podgląd” | Błąd: dostępne do sprzedaży 2 szt, bo 8 szt zarezerwowano w zamówieniach |
| 4 | Wróć do zamówienia, „Wystaw fakturę” | Formularz faktury z informacją o zamówieniu, nabywca zablokowany, Chleb 8 szt po cenie z kartoteki |
| 5 | „Pokaż podgląd”, „Wystaw fakturę”, potwierdź | Faktura z WZ na 8 szt; zamówienie „zrealizowane” z linkiem do faktury; stan 2 szt bez rezerwacji |

## Z2. Brak towaru przy zamówieniu

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Zamówienie na 8 szt Chleba (stan 10) | Zapisane |
| 2 | Drugie zamówienie na 3 szt | Błąd: można zarezerwować najwyżej 2 szt; brak nowego numeru ZK |
| 3 | Partia przeterminowana | Nie liczy się do towaru, który można zarezerwować |

## Z3. Strata po rezerwacji

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Zamówienie na 8 szt Chleba (stan 10), potem RW na 6 szt | RW przechodzi, bo strata już się wydarzyła |
| 2 | Otwórz zamówienie | Ostrzeżenie o braku towaru, przy Chlebie „dostępne 4 szt” i „brakuje 4” |

## Z4. Anulowanie

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Otwarte zamówienie, „Anuluj zamówienie”, potwierdź | Status „anulowane”, brak przycisku „Wystaw fakturę”, rezerwacja zwolniona |
| 2 | Adres `/invoices/new?order=<id>` anulowanego zamówienia | Komunikat, że z zamówienia nie można wystawić faktury |
| 3 | Zrealizowane zamówienie | Brak przycisku anulowania; cofnięcie sprzedaży to anulowanie faktury |
