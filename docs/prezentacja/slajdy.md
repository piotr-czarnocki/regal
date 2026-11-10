# Prezentacja: konspekt slajdów

Około 15 minut: 10 slajdów i pokaz na żywo według [`pokaz-na-zywo.md`](pokaz-na-zywo.md). Każdy slajd ma treść na ekran i notatkę dla mówiącego.

## 1. Regał

- Magazyn i sprzedaż dla małej firmy z własną produkcją
- .NET 10 · Blazor · PostgreSQL · Fakturownia
- Piotr Czarnocki, repozytorium publiczne na GitHubie (MIT)

*Notatka:* projekt na zaliczenie i do portfolio; jedna firma, kilku pracowników.

## 2. Problem

- Piekarnia ze sklepem i hurtem: dostawy, własna produkcja, sprzedaż, straty
- Towar z datą ważności: co wydać najpierw, co się przeterminuje
- Faktury VAT, które muszą powstać raz i tylko raz
- Pytania właściciela: co zarabia, co zalega

## 3. Co program robi

- Kartoteki: produkty z EAN, kontrahenci
- Magazyn: PZ, PW, WZ, RW, partie, FEFO, cena średnia, korekty
- Faktury: WZ w tej samej transakcji, wysyłka w tle do Fakturowni
- Skanowanie: czytnik USB i aparat telefonu
- Analizy: pulpit, sprzedaż i marża, ABC, zalegające
- Poziom 2: zamówienia klientów z rezerwacją towaru, proforma i faktura korygująca ze zwrotem towaru, wysyłka mailem i płatności z Fakturowni, inwentaryzacja ze skanowaniem

*Notatka:* szczegóły w `funkcjonalnosci.md`; tu tylko mapa, resztę pokaże demo.

## 4. Pokaz na żywo

*(przejście do aplikacji, około 8 minut)*

## 5. Architektura

- Jedna aplikacja, moduły w `Features/`, bez mikroserwisów
- Blazor Interactive Server: logika tylko na serwerze, bez osobnego API
- Serwisy z krótko żyjącym `DbContext`; komponenty tylko je wywołują
- Fakturownia za interfejsem, w testach udawana

## 6. Najtrudniejsze: współbieżność i niezmienność

- Dokument blokuje produkty (`SELECT … FOR UPDATE`, rosnąco po `Id`): poprawna średnia, brak zakleszczeń
- Ostatnia bariera: warunkowy `UPDATE` i `CHECK (Quantity >= 0)`
- Numeracja bez luk w tej samej transakcji
- Rola bazy aplikacji bez `UPDATE`/`DELETE` na dokumentach: korekta zamiast edycji
- Faktury: `FOR UPDATE SKIP LOCKED` + dzierżawa + wyszukanie po `oid` przed utworzeniem

## 7. Testy

| Rodzaj | Liczba | Narzędzie |
|---|---:|---|
| Jednostkowe | 274 | xUnit, bUnit |
| Integracyjne | 236 | Testcontainers, PostgreSQL 18 |
| Funkcjonalne | 13 | Playwright |
| Wydajności | 7 zapytań, 8 stron | BenchmarkDotNet, k6 |

- Pokrycie wierszy 81% (magazyn 91%, faktury 84%)
- Wszystko poza wydajnością w CI przy każdym scaleniu

## 8. Czy testy potrafią się nie udać?

- Usunięte `FOR UPDATE` → 5 testów pada (zła średnia 30,00 zamiast 20,25; zakleszczenie `40P01`)
- Pierwsza wersja testu zakleszczenia przechodziła bez blokady: poprawiona
- Testy faktur bez zabezpieczeń przechodziły, bo duplikaty odrzucała udawana Fakturownia → teraz liczą żądania

*Notatka:* najważniejsza lekcja z analizy dynamicznej. Zielony test nie dowodzi niczego, dopóki nie widziało się go czerwonego.

## 9. Wydajność i błędy

- Rok danych: 10 000 produktów, 27 459 faktur, 136 962 ruchy magazynowe
- ABC 44 ms, raport sprzedaży ok. 150 ms (cel < 1 s)
- 20 użytkowników: p95 **1905 ms → 286 ms** (cel < 500 ms), 0 błędów
- Przyczyna: raport zalegających, LINQ → podzapytania skorelowane powtórzone 4 razy (700 ms, 830 tys. odczytów stron) → dwa zestawienia `GROUP BY` (54 ms, 2 tys.); wynik identyczny wiersz po wierszu
- Prawdziwe błędy: „2,50 → 250” (znalazł Playwright), zgubione skany (bUnit), generator z 3 razy mniejszą sprzedażą (dane), Workstation GC w hoście testów (`dotnet-counters`)

*Notatka:* benchmark 586 ms mieścił się w celu 1 s, a pod obciążeniem ten sam raport zepsuł wszystkie strony. Indeksy nie były potrzebne, bo problemem był kształt zapytania.

## 10. Czego się nauczyłem

*(z `wklad.md`, uzupełnia autor)*

- Praca z Claude Code: co przyspieszył, co trzeba było poprawić, zasada „nie czytaj poświadczeń”
- Pytania
