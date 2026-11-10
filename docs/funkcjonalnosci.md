# Opis funkcjonalności

Regał to aplikacja webowa do prowadzenia magazynu i sprzedaży w jednej małej firmie handlowej z własną produkcją (np. piekarnia ze sklepem i hurtem). Ten dokument opisuje, co program robi z punktu widzenia użytkownika. Szczegółowe reguły i decyzje są w [`SPEC.md`](../SPEC.md), a scenariusze, którymi każdą funkcję sprawdzono, w [`scenariusze-testowe.md`](scenariusze-testowe.md).

Wszystkie ekrany są po polsku. Kwoty są w złotych, ilości do 3 miejsc po przecinku, daty według czasu polskiego.

## Mapa ekranów

| Menu | Adres | Co tam jest |
|---|---|---|
| Pulpit | `/` | Sprzedaż i marża w bieżącym miesiącu, najlepsze produkty, partie z kończącą się ważnością, towary zalegające |
| Produkty | `/products` | Kartoteka towarów i usług |
| Kategorie | `/categories` | Słownik kategorii produktów |
| Kontrahenci | `/contractors` | Odbiorcy i dostawcy |
| Stany magazynowe | `/stock` | Stan każdego towaru i jego partii; z wiersza historia ruchów (`/stock/{id}/history`) |
| Dokumenty magazynowe | `/stock-documents` | Lista PZ, PW, WZ, RW z wyszukiwaniem; nowy dokument (`/stock-documents/new`), szczegóły i korekta (`/stock-documents/{id}`) |
| Zamówienia | `/orders` | Zamówienia klientów ze statusem; nowe zamówienie (`/orders/new`), szczegóły z pokryciem, faktura i anulowanie (`/orders/{id}`) |
| Faktury | `/invoices` | Lista faktur ze statusem wysyłki; nowa faktura (`/invoices/new`, z zamówienia `/invoices/new?order={id}`), szczegóły (`/invoices/{id}`) |
| Analizy → Sprzedaż i marża | `/analytics/sales` | Wykres w czasie, tabele według produktów i kategorii |
| Analizy → ABC | `/analytics/abc` | Podział produktów na klasy A, B, C |
| Analizy → Zalegające | `/analytics/slow-moving` | Towary na stanie bez sprzedaży od N dni |

## 1. Logowanie i dostęp

- Logowanie wyłącznie kontem Google. Regał nie przechowuje haseł.
- Wejść mogą tylko adresy z listy dozwolonych użytkowników w bazie. Pozostali widzą stronę odmowy dostępu i nie dostają żadnych danych, także przez bezpośredni adres podstrony.
- Pierwszego administratora dodaje konfiguracja przy starcie (poza repozytorium).
- Każde logowanie i każda odmowa trafiają do dziennika działań.

## 2. Produkty, usługi i kategorie

- Produkt ma SKU, EAN, nazwę, kategorię, jednostkę, typ (towar albo usługa), cenę sprzedaży netto, stawkę VAT, znacznik „ma datę ważności” i aktywność.
- SKU i EAN są unikalne. SKU jest zapisywane wielkimi literami, bez spacji na brzegach.
- Gdy produkt nie ma kodu producenta, Regał nadaje mu wewnętrzny EAN-13 z poprawną cyfrą kontrolną. Podany ręcznie EAN jest sprawdzany pod kątem cyfry kontrolnej.
- Usługa nie ma stanu i nie pojawia się na dokumentach magazynowych, ale może być na fakturze.
- Produktu użytego w dokumencie nie da się usunąć, tylko dezaktywować. Towaru ze stanem większym od zera nie da się dezaktywować, a znacznika daty ważności nie da się zmienić, gdy produkt ma partie.
- Lista ma wyszukiwanie po nazwie, SKU i EAN oraz filtr aktywności.

## 3. Kontrahenci

- Nazwa, NIP (opcjonalny, sprawdzany), adres, e-mail, telefon, aktywność.
- Ten sam kontrahent może być dostawcą i odbiorcą.
- Kontrahenta z dokumentami można tylko dezaktywować.

## 4. Magazyn

### Dokumenty

| Dokument | Kiedy | Kontrahent | Wpływ na stan |
|---|---|---|---|
| PZ | Przyjęcie towaru od dostawcy, z ceną zakupu | Wymagany | Zwiększa |
| PW | Przyjęcie z własnej produkcji, z kosztem wytworzenia | Brak | Zwiększa |
| WZ | Wydanie na zewnątrz (także automatycznie z faktury) | Opcjonalny | Zmniejsza |
| RW | Zużycie wewnętrzne lub strata, z powodem | Brak | Zmniejsza |

- Formularz dokumentu ma pole produktu, które przyjmuje kod z czytnika USB, z aparatu telefonu albo wpisany ręcznie. Enter szuka najpierw dokładnego EAN, potem dokładnego SKU, a dopiero potem fragmentu nazwy.
- Przy przyjęciu podaje się numer partii (albo zostaje numer dokumentu) i datę ważności, wymaganą dla produktów z terminem ważności.
- Przy wydaniu można wybrać partię albo zostawić wybór automatyczny: najpierw partia z najkrótszą ważnością (FEFO). Podgląd przed zatwierdzeniem pokazuje, z których partii zejdzie towar i po jakim koszcie. WZ nigdy nie wyda partii przeterminowanej; RW może, bo służy też do spisania strat.
- Dokument ma od 1 do 200 pozycji. Jeśli którakolwiek jest błędna, nic nie zostaje zapisane i numer nie przepada.
- Numeracja `PZ/1/2026` itd., osobna dla typu i roku, bez luk.
- Stan nigdy nie spada poniżej zera, także gdy dwie osoby wydają ten sam towar w tej samej chwili.
- Podwójne kliknięcie „Zatwierdź” albo ponowne wysłanie formularza nie tworzy drugiego dokumentu.

### Korekta

- Zatwierdzonego dokumentu nie można zmienić ani usunąć. Pomyłkę poprawia przycisk „Koryguj” w szczegółach dokumentu: powstaje dokument odwrotny tego samego typu, który cofa wszystkie ruchy na tych samych partiach i po tych samych kosztach.
- Każdy dokument można skorygować raz. Korekta PZ lub PW wymaga, żeby przyjęty towar nadal był na stanie.
- WZ utworzonego przez fakturę nie da się skorygować ręcznie; cofa go anulowanie faktury.

### Stany i historia

- Ekran stanów pokazuje ilość, cenę średnią i wartość każdego towaru oraz rozwinięcie na partie z datami ważności. Partie przeterminowane i z bliską datą są oznaczone. Produkt nieaktywny, który ma jeszcze stan, jest widoczny z oznaczeniem „nieaktywny”.
- Historia produktu pokazuje każdy ruch z numerem dokumentu, partią, ilością i kosztem.

### Wycena

- Cena średnia (średnia ważona) zmienia się tylko przy przyjęciu. Wydanie zapisuje koszt według średniej z chwili wydania, dzięki czemu marża faktury nie zmienia się po późniejszych zakupach.

## 5. Faktury

- Nowa faktura: nabywca, pozycje (towary i usługi), cena netto z kartoteki z możliwością zmiany, rabat procentowy, termin i sposób płatności. Sumy netto, VAT i brutto są liczone na bieżąco.
- Wystawienie jednocześnie tworzy WZ (partie według FEFO), zapisuje fakturę ze statusem „do wysłania” i zadanie wysyłki. Gdy towaru brakuje, nie powstaje nic.
- W tle Regał wysyła fakturę do Fakturowni i zapisuje jej numer i link do PDF. Status zmienia się na „wysłana”.
- Gdy Fakturownia nie odpowiada, wysyłka jest ponawiana coraz rzadziej (1, 2, 4… minut, najwyżej co godzinę). Przed każdym ponowieniem Regał sprawdza po identyfikatorze zamówienia, czy faktura już nie powstała, więc nie ma duplikatów.
- Fakturę z błędem wysyłki można ponowić ręcznie albo anulować. Anulowanie jest możliwe tylko wtedy, gdy Fakturownia potwierdzi, że faktury u niej nie ma, i cofa WZ dokumentem odwrotnym.
- Regał ostrzega, gdy suma brutto policzona przez Fakturownię różni się od jego własnej.
- Bez skonfigurowanej Fakturowni faktury czekają w kolejce, a reszta aplikacji działa normalnie.

## 5a. Zamówienia klientów (poziom 2)

- Zamówienie (ZK) rezerwuje towar dla nabywcy, zanim zostanie wydany i zafakturowany: nabywca, pozycje (towary i usługi) z ilościami, uwagi. Numeracja `ZK/1/2026` bez luk.
- Zapis jest możliwy tylko wtedy, gdy towaru wystarcza: liczy się stan partii, które nie są przeterminowane, minus to, co zarezerwowały inne otwarte zamówienia. Usługi niczego nie rezerwują.
- Zarezerwowanego towaru nie zabierze ręczny WZ ani zwykła faktura; podgląd wydania od razu mówi, ile jest dostępne do sprzedaży i ile zarezerwowano. Ekran stanów pokazuje przy ilości „zarezerwowano X”.
- RW i korekty nie patrzą na rezerwacje, bo opisują to, co się fizycznie stało. Jeśli po stracie zabraknie towaru, szczegóły zamówienia pokazują, ile brakuje.
- „Wystaw fakturę” w zamówieniu otwiera formularz faktury z nabywcą (bez możliwości zmiany) i pozycjami po cenach z kartoteki. Wystawienie wydaje towar z użyciem rezerwacji tego zamówienia i oznacza je jako zrealizowane w całości.
- Otwarte zamówienie można anulować, co zwalnia rezerwację. Zamówień nie da się edytować ani usuwać; zrealizowanego nie da się anulować (cofnięcie sprzedaży to anulowanie faktury).

## 6. Skanowanie telefonem i czytnikiem

- Przycisk z ikoną aparatu przy polu produktu otwiera podgląd kamery i odczytuje kod EAN-13. Odczytany kod trafia do pola tak, jakby został wpisany.
- Aparat w przeglądarce wymaga HTTPS, dlatego telefon łączy się z aplikacją przez tunel ngrok (profil uruchomieniowy `tunnel`, opis w README).
- Czytnik USB działa jak klawiatura: kod i Enter. Kolejne skany są kolejkowane, więc szybkie skanowanie kilku kodów nie gubi żadnego.
- Gdy dostęp do aparatu jest zablokowany albo urządzenie nie ma kamery, okno mówi o tym i kod można wpisać ręcznie.

## 7. Analizy

Sprzedaż i ABC mają filtr okresu, kategorii i klienta, a zalegające filtr kategorii i liczby dni. Sprzedaż to pozycje faktur, które nie zostały anulowane; marża to sprzedaż netto minus koszt z wydania.

- **Pulpit:** sprzedaż i marża w bieżącym miesiącu, liczba faktur, najlepsze produkty, partie z ważnością do 14 dni (także przeterminowane), towary bez sprzedaży od 60 dni.
- **Sprzedaż i marża:** wykres liniowy dzień po dniu (do dwóch miesięcy) albo miesiąc po miesiącu oraz tabele według produktów i kategorii z marżą procentową.
- **ABC:** produkty uporządkowane według sprzedaży; klasa A to produkty dające pierwsze 80% sprzedaży, B kolejne 15%, C resztę. Ekran pokazuje udział i udział narastający.
- **Zalegające:** towary na stanie, których ostatnia sprzedaż była dawniej niż N dni temu albo których nigdy nie sprzedano, z wartością zamrożonego zapasu.

## 8. Dziennik działań

Regał zapisuje logowania, odmowy dostępu, zmiany kartotek, zatwierdzenia i korekty dokumentów oraz wysyłki i anulowania faktur: kto, kiedy, co i którego rekordu dotyczyło. Wpisy nie zawierają tokenów ani sekretów i nie da się ich zmienić ani usunąć z aplikacji, bo rola bazy, której używa aplikacja, nie ma takich uprawnień. Dziennik nie ma osobnego ekranu; przegląda się go w bazie (DBeaver, połączenie tylko do odczytu).

## 9. Dane przykładowe

Narzędzie `tools/Regal.DataGenerator` tworzy rok wymyślonej działalności przez prawdziwe serwisy aplikacji: dostawy, produkcję pieczywa, spisywanie przeterminowanego towaru, faktury z realistycznym rozkładem popularności i ok. 1% anulowanych. Dane służą do pokazu i do testów wydajności. Wygenerowane faktury nigdy nie trafiają do Fakturowni. Narzędzie działa tylko w środowisku deweloperskim i testowym oraz przed zapisem wypisuje, do której bazy pisze.

## Czego program świadomie nie robi

Księgowość, kadry, wiele magazynów i walut, warianty i zdjęcia produktów, import z pliku, receptury. Z poziomu 2 specyfikacji powstały zamówienia klientów z rezerwacją (punkt 5a); nie powstały m.in. proformy i faktury korygujące, wysyłka faktur mailem, inwentaryzacja, eksport do Excela i etykiety w PDF. Kolejne role użytkowników to poziom 3. Lista znanych uproszczeń jest w `SPEC.md` (m.in. przycinanie średniej do zera po korekcie).
