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

**Dane:** kod EAN Mleka 1L z listy produktów.

| Krok | Działanie | Oczekiwany wynik |
|---|---|---|
| 1 | Nowa WZ, zeskanuj kod czytnikiem USB (albo wpisz i naciśnij Enter) | Pozycja Mleko 1L, ilość 1, pole produktu puste i aktywne, dokument niezatwierdzony |
| 2 | Zeskanuj ten sam kod jeszcze raz | Ta sama pozycja, ilość 2 |
| 3 | Wpisz `5900000000000`, Enter | Komunikat, że nie znaleziono towaru, brak nowej pozycji |
| 4 | Naciśnij Enter w polu ilości | Nic się nie dzieje, dokument niezatwierdzony |
| 5 | „Pokaż podgląd”, „Zatwierdź”, potwierdź i szybko kliknij „Zatwierdź” ponownie | Przycisk zablokowany na czas zapisu, powstaje jeden dokument |

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
