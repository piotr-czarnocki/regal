# Regał – specyfikacja

**Wersja:** 0.4 (10.11.2026), zakres ustalony w wywiadzie, reguły magazynu doprecyzowane w etapie „Magazyn”, wyniki wydajności z etapu „Analiza i oddanie”.
**Autor:** Piotr Czarnocki, projekt indywidualny.
**Termin oddania:** połowa grudnia 2026; dokładną datę należy tu wpisać.
**Budżet czasu:** do 10 h tygodniowo, około 50 h łącznie.

## 1. Cel

Regał to webowa aplikacja magazynowo-sprzedażowa dla jednej małej firmy handlowej z własną produkcją. Funkcjonalnie jest wzorowana na programach typu Small Business, ale ma własny kod, nazwę, wygląd i dokumentację.

Projekt służy jako zaliczenie na studiach i pozycja w portfolio. Repozytorium jest publiczne od początku, na licencji MIT.

## 2. Założenia ogólne

- Interfejs tylko po polsku; waluta tylko PLN; jedna firma.
- Cała aplikacja działa na komputerze i telefonie.
- Na start aplikacja działa lokalnie w Dockerze, a hosting w internecie powstanie po oddaniu projektu.
- Faktury wystawia Fakturownia. Regał przygotowuje dane, zmienia stan magazynu i zapisuje wynik.

## 3. Zakres i priorytety

### Poziom 1 – rdzeń (wymagany do oddania)

- Logowanie Google z listą dozwolonych adresów.
- Produkty i usługi z kategoriami, ceną sprzedaży netto i stawką VAT; produkt bez EAN dostaje wewnętrzny kod EAN-13.
- Kontrahenci w jednej kartotece: firmy i osoby prywatne.
- Dokumenty magazynowe PZ, PW, WZ i RW, korekta dokumentem odwrotnym.
- Stany magazynowe i partie z datą ważności dla wybranych produktów.
- Wydanie partii: ręczny wybór albo automatycznie partia z najkrótszą datą ważności.
- Wycena zapasu ceną średnią i liczenie marży.
- Faktura VAT przez API Fakturowni z automatycznym utworzeniem WZ.
- Skanowanie kodów telefonem przy dokumentach i wyszukiwaniu produktu.
- Pulpit oraz analizy: sprzedaż z marżą, analiza ABC, towary zalegające, partie z bliską datą ważności.
- Generator przykładowych danych.
- Dziennik działań.
- Analiza dynamiczna, dokumentacja i prezentacja.

### Poziom 2 – jeśli zostanie czas (w tej kolejności)

1. Zamówienia klientów (ZK) z rezerwacją towaru, bez wygasania. **Gotowe** (punkt 5.11).
2. Proforma i faktura korygująca przez Fakturownię. **Gotowe** (punkt 5.12).
3. Wysyłka faktury mailem, status płatności z Fakturowni. **Gotowe** (punkt 5.13).
4. Inwentaryzacja ze skanowaniem. **Gotowe** (punkt 5.14).
5. Pobieranie danych firmy po NIP-ie. **Gotowe** (punkt 5.15).
6. Analizy klientów, prosta prognoza zamówień i porównanie okresów. **Gotowe** (punkt 5.16).
7. Eksport analiz do Excela (.xlsx). **Gotowe** (punkt 5.17).
8. Etykiety z kodami w PDF do druku.

### Poziom 3 – po oddaniu, do portfolio

- Paragon przez Fakturownię.
- Status KSeF faktury, jeśli API Fakturowni go udostępnia.
- Kolejne role, np. magazynier.
- Obsługa kilku magazynów w interfejsie.
- Hosting w internecie.

### Świadomie poza zakresem

Księgowość, kadry i płace, wiele walut, warianty produktów, zdjęcia produktów, import produktów z pliku, receptury produkcyjne.

## 4. Użytkownicy i logowanie

- Logowanie wyłącznie przez Google, bez haseł w bazie Regału.
- Wpuszczane są tylko adresy z tabeli dozwolonych użytkowników; pozostali dostają odmowę dostępu.
- Na start każdy dozwolony użytkownik ma rolę `Administrator`. Rola jest zapisana w bazie i dodawana do tożsamości przy logowaniu, więc później łatwo dodać kolejne role.
- Pierwszy administrator jest dodawany przy starcie z konfiguracji spoza repozytorium.
- Bez ASP.NET Core Identity: uwierzytelnianie przez ciasteczko i dostawcę Google.

## 5. Wymagania funkcjonalne

### 5.1. Produkty i usługi

- Pola: SKU, EAN, nazwa, kategoria, jednostka, typ (towar albo usługa), cena sprzedaży netto, stawka VAT, czy ma datę ważności, aktywność.
- SKU i EAN są unikalne.
- Produkt bez EAN dostaje kod EAN-13 z prefiksem dla obiegu wewnętrznego i poprawną cyfrą kontrolną (zakres prefiksów do weryfikacji w GS1).
- Usługa nie ma stanu magazynowego i nie pojawia się w dokumentach magazynowych.
- Produktu użytego w dokumencie nie można usunąć, można go tylko dezaktywować.

### 5.2. Kontrahenci

- Pola: nazwa, NIP (opcjonalny), adres, e-mail, telefon, aktywność.
- Ten sam kontrahent może być odbiorcą i dostawcą.
- Kontrahenta użytego w dokumencie nie można usunąć, można go tylko dezaktywować.

### 5.3. Dokumenty magazynowe

| Dokument | Znaczenie | Wpływ na stan |
|---|---|---|
| PZ | Przyjęcie od dostawcy, z ceną zakupu | Zwiększa |
| PW | Przychód wewnętrzny z własnej produkcji, z kosztem wytworzenia | Zwiększa |
| WZ | Wydanie na zewnątrz, z fakturą lub bez | Zmniejsza |
| RW | Rozchód wewnętrzny: zużycie lub strata | Zmniejsza |

- Numeracja w formacie `{typ}/{numer}/{rok}`, osobna dla każdego typu i roku, bez luk. Rok liczony jest według czasu Europe/Warsaw.
- Data dokumentu to chwila zatwierdzenia; antydatowanie nie jest możliwe.
- Kontrahent: przy PZ wymagany, przy WZ opcjonalny, przy PW i RW niedozwolony. RW wymaga powodu w uwagach (do 500 znaków).
- Na dokumentach występują tylko aktywne towary (nie usługi) i aktywni kontrahenci.
- Dokument ma od 1 do 200 pozycji; przy błędzie dowolnej pozycji cały dokument jest wycofywany i nie zużywa numeru.
- Ilość jest większa od zera, ma najwyżej 3 miejsca po przecinku, a dla jednostki „szt” jest liczbą całkowitą.
- Produkt występuje na dokumencie raz, a na przyjęciu raz w każdej partii.
- Stan nie może spaść poniżej zera, także przy równoczesnych operacjach.
- Ponowne wysłanie tego samego szkicu (ten sam `RequestId`) nie tworzy drugiego dokumentu.
- Wszystko trafia do magazynu głównego; wyboru magazynu nie ma w interfejsie.

Korekta:

- Zatwierdzonego dokumentu nie można edytować ani usunąć. Błąd poprawia dokument odwrotny tego samego typu, powiązany z oryginałem.
- Korekta obejmuje cały dokument i odwraca jego ruchy na tych samych partiach, po tych samych kosztach.
- Dokument można skorygować raz. Korekty nie można skorygować.
- Korekta PZ lub PW jest możliwa tylko wtedy, gdy bieżący stan każdej partii dokumentu pokrywa ilość z tego dokumentu.
- Korekta działa także dla nieaktywnego produktu lub kontrahenta, bo poprawienie błędu nie może zależeć od późniejszych zmian w kartotekach. Taki produkt ze stanem jest widoczny na ekranie stanów z oznaczeniem „nieaktywny”.

### 5.4. Partie i daty ważności

- Każde przyjęcie (PZ, PW) tworzy partię: numer wpisany ręcznie, np. numer dostawcy, albo automatyczny, równy numerowi dokumentu. Numer jest normalizowany jak SKU (bez spacji na brzegach, wielkie litery), do 50 znaków.
- Jeśli partia o tym numerze już istnieje dla produktu, towar jest do niej dopisywany, ale data ważności musi się zgadzać.
- Data ważności jest wymagana dla produktów oznaczonych jako mające termin ważności, a dla pozostałych opcjonalna. Nie może być wcześniejsza niż data dokumentu.
- Stan jest prowadzony dla każdej partii.
- Partia jest przeterminowana, gdy data ważności jest wcześniejsza niż dzisiejsza data; w dniu ważności można ją jeszcze wydać.
- Przy wydaniu (WZ, RW) użytkownik wybiera partię ręcznie albo zostawia wybór automatyczny (FEFO): najpierw najkrótsza data ważności, partie bez daty na końcu, przy remisie najstarsza partia. Jedna pozycja może zejść z kilku partii.
- WZ nigdy nie wydaje partii przeterminowanej, także przy wyborze ręcznym. RW może, bo służy m.in. do spisania takiego towaru.
- Ustawienia „ma datę ważności” nie można zmienić, gdy produkt ma partie, a produktu ze stanem większym od zera nie można dezaktywować.

### 5.5. Wycena i marża

- Cena średnia produktu zmienia się tylko przy przychodzie (PZ, PW, korekta WZ i RW), z zaokrągleniem do 4 miejsc (połowa w górę):
  `nowa średnia = (stara ilość × stara średnia + przyjęta ilość × cena przyjęcia) / (stara ilość + przyjęta ilość)`.
- PZ wymaga ceny zakupu większej od zera. PW bez podanego kosztu przyjmuje bieżącą cenę średnią, a gdy wynosi ona 0, koszt jest wymagany.
- Pozycja wydania zapisuje koszt według średniej z chwili wydania. Korekta WZ i RW przywraca towar po tym zapisanym koszcie.
- Korekta PZ i PW liczy średnią jednym wzorem dla produktu z całego dokumentu:
  `nowa średnia = (ilość × średnia − ilość korekty × koszt) / (ilość − ilość korekty)`. Gdy nic nie zostaje, średnia się nie zmienia.
- **Znane uproszczenie:** ujemny wynik tego wzoru, możliwy po wcześniejszych zaokrągleniach lub korektach, jest przycinany do 0. Średnia może wtedy nie odzwierciedlać rzeczywistego kosztu zapasu.
- Marża pozycji to wartość sprzedaży netto minus zapisany koszt.

### 5.6. Faktury przez Fakturownię

- Użytkownik tworzy fakturę w Regale: kontrahent, pozycje, ewentualny rabat ręczny.
- Wystawienie w jednej transakcji tworzy WZ, zapisuje fakturę ze statusem „do wysłania” i zadanie wysyłki.
- Proces w tle wysyła fakturę do API Fakturowni i zapisuje jej identyfikator, numer oraz link do PDF.
- Przy awarii wysyłka jest ponawiana. Każda faktura ma unikalny identyfikator zamówienia (`oid`), więc przed ponowieniem sprawdzamy, czy faktura już nie powstała, żeby uniknąć duplikatu.
- Po wystawieniu faktury nie można jej edytować; poprawka wymaga faktury korygującej (poziom 2).
- Nieudanej faktury, która nie powstała w Fakturowni, można anulować, a WZ jest wtedy cofana dokumentem odwrotnym.
- Dane kontrahenta i pozycji trafiają do Fakturowni razem z fakturą; kartoteki są prowadzone tylko w Regale.
- Testy automatyczne nigdy nie łączą się z prawdziwą Fakturownią. Do testów ręcznych służy osobne konto testowe bez włączonej integracji z KSeF.

Ustalenia etapu „Faktury”:

- Pozycja ma cenę netto z kartoteki, którą można zmienić, i rabat w procentach. VAT liczony jest od wartości po rabacie, na każdej pozycji, z zaokrągleniem do groszy (połowa w górę). Do Fakturowni trafia wartość brutto pozycji po rabacie.
- Formularz ma termin płatności (domyślnie 14 dni, 0–365) i sposób płatności (przelew, gotówka, karta).
- Faktura może zawierać towary i usługi. WZ powstaje tylko dla towarów, a jego partie dobiera reguła FEFO. Faktura z samymi usługami nie ma WZ.
- Koszt pozycji pochodzi z ruchów WZ, czyli ze średniej z chwili wydania. Dla usług wynosi 0.
- `oid` to `regal-{RequestId}`. Ponowne wysłanie formularza zwraca istniejącą fakturę.
- Wysyłka w tle pobiera zadanie z `FOR UPDATE SKIP LOCKED` i dzierżawą 5 minut, więc połączenie HTTP nie trzyma blokady w bazie. Przed utworzeniem faktury sprawdza, czy faktura o tym `oid` już istnieje, i wysyła ją z `oid_unique=yes`.
- Błędy przejściowe (sieć, przekroczony czas, 429, 5xx) są ponawiane po 1, 2, 4… minutach, najwyżej co godzinę, maksymalnie 10 razy. Odmowa (4xx) nie jest ponawiana. W obu przypadkach faktura dostaje status „błąd wysyłki” i można ją ponowić ręcznie.
- Anulowanie najpierw sprawdza w Fakturowni, że faktury nie ma; gdy Fakturownia nie odpowiada, anulowanie jest odrzucane. WZ faktury, która nie jest anulowana, nie da się skorygować ręcznie.
- Bez skonfigurowanej Fakturowni faktury czekają w kolejce, a aplikacja działa normalnie.
- Regał zapisuje sumę brutto policzoną przez Fakturownię i ostrzega, gdy różni się od własnej.

### 5.7. Skanowanie

- Skaner w przeglądarce odczytuje EAN-13 i wpisuje go do pola; serwer wyszukuje produkt.
- Działa w formularzach PZ, PW, WZ i RW oraz w wyszukiwaniu produktu.
- Kod zawsze można wpisać ręcznie.
- Kod jest traktowany jak niezaufane dane wejściowe.
- Aparat wymaga HTTPS, więc telefon łączy się przez tunel HTTPS do lokalnej aplikacji. Wybrany został ngrok ze stałą domeną, bo logowanie Google wymaga stałego adresu przekierowania.
- Czytnik działa także z klawiatury: kod wpisany albo zeskanowany czytnikiem USB i zatwierdzony Enterem jest szukany najpierw jako dokładny EAN, potem jako dokładny SKU, a dopiero potem jako fragment nazwy, SKU lub EAN. 12 cyfr traktowane jest jak EAN-13 z wiodącym zerem.
- Biblioteka: `barcode-detector` 3.2.3 (MIT, wydana 19.10.2026) z ZXing-C++ w WebAssembly. Gdy przeglądarka ma własny `BarcodeDetector` z obsługą EAN-13, używany jest ten wbudowany. Pliki biblioteki są w `wwwroot/lib`, a suma kontrolna WASM jest sprawdzona; nic nie jest pobierane z CDN. Odrzucono `html5-qrcode`, którego ostatnie wydanie pochodzi z kwietnia 2023.
- Odczytany kod przechodzi tylko wtedy, gdy ma do 50 znaków i składa się z liter, cyfr, kropek i myślników.

### 5.8. Analizy

- **Pulpit:** sprzedaż i marża w bieżącym miesiącu, najlepsze produkty, partie z bliską datą ważności, towary zalegające.
- **Sprzedaż i marża:** w czasie, według produktów i kategorii.
- **Analiza ABC:** podział produktów według udziału w sprzedaży (A około 80%, B około 15%, C około 5%).
- **Zalegające:** produkty na stanie bez sprzedaży od N dni.
- **Filtry:** okres, kategoria, klient.
- Wykresy rysowane komponentem `MudChart` z MudBlazor 9 (wykres liniowy sprzedaży i marży). Wystarczają, więc nie dodano osobnej biblioteki.

Ustalenia etapu „Analizy”:

- Sprzedaż to pozycje faktur, które nie zostały anulowane, z datą wystawienia faktury. Koszt to koszt zapisany przy wydaniu (WZ), więc marża = sprzedaż netto − ten koszt. Usługi mają koszt 0.
- Kategoria w filtrze to bieżąca kategoria produktu. Klient to nabywca z faktury.
- Okres obejmuje oba dni graniczne. Do dwóch miesięcy wykres pokazuje dni, dla dłuższych okresów miesiące; dni bez sprzedaży mają wartość 0.
- ABC: produkty uporządkowane malejąco po sprzedaży netto. Produkt jest w klasie A, jeśli produkty przed nim dają mniej niż 80% sprzedaży; w B poniżej 95%; pozostałe w C. Produkt przekraczający granicę zostaje więc w klasie, w której się zaczął, a pierwszy produkt jest zawsze w A.
- Zalegające: towary ze stanem większym od zera, których ostatnia sprzedaż była wcześniej niż N dni temu albo których nigdy nie sprzedano (domyślnie 90 dni, na pulpicie 60).
- Pulpit pokazuje bieżący miesiąc według czasu Europe/Warsaw oraz partie z ważnością do 14 dni, także przeterminowane.

### 5.9. Generator danych

- Tworzy około roku wymyślonej działalności: produkty, kontrahentów, dokumenty i faktury, bez wysyłki do Fakturowni.
- Narzędzie konsolowe `tools/Regal.DataGenerator` korzysta z prawdziwych serwisów z zegarem przesuwanym dzień po dniu, więc dane przestrzegają reguł aplikacji: stany nie są ujemne, średnie i numeracja są poprawne, partie mają daty ważności.
- Symulacja: zamówienia uzupełniające do dostawców (PZ) i własna produkcja pieczywa (PW), co tydzień spisanie przeterminowanych partii (RW), faktury z popularnością produktów zbliżoną do rozkładu Zipfa (dla czytelnej analizy ABC), około 15% towarów, które nigdy się nie sprzedają, mniej sprzedaży w weekendy i około 1% anulowanych faktur.
- Wygenerowane faktury dostają status „wysłana” z numerem `DEMO n/rok` i nie mają zadania wysyłki, więc nigdy nie trafiają do Fakturowni.
- Rekordy należą do nieaktywnego użytkownika `dane-testowe@regal.invalid`, który nie może się zalogować.
- Ten sam `--seed` daje te same dane. Drugie uruchomienie z tym samym prefiksem SKU jest odrzucane.
- Działa tylko w środowisku deweloperskim i testowym.
- Ma parametry wielkości, np. 10 000 produktów i 100 000 ruchów do testów wydajności (`--products`, `--customers`, `--suppliers`, `--days`, `--invoices-per-day`, `--seed`, `--prefix`).

### 5.10. Dziennik działań

- Zapisuje logowania, odmowy dostępu, zmiany kartotek, zatwierdzenia dokumentów i wysyłki faktur.
- Zawiera użytkownika, czas, działanie i identyfikator rekordu, ale nigdy tokenów ani sekretów.
- Wpisów nie można zmieniać ani usuwać z aplikacji.

### 5.11. Zamówienia klientów (poziom 2)

Zamówienie (ZK) rezerwuje towar dla klienta, zanim zostanie wydany i zafakturowany.

- Numeracja `ZK/{numer}/{rok}`, osobna dla każdego roku, bez luk, według czasu Europe/Warsaw.
- Zamówienie ma nabywcę (aktywny kontrahent, wymagany), od 1 do 200 pozycji i uwagi do 500 znaków. Pozycja to aktywny produkt i ilość; każdy produkt występuje raz, a ilość podlega tym samym regułom co na dokumentach. Cen nie ma: ustala je faktura.
- Statusy: **otwarte**, **zrealizowane** (wystawiono z niego fakturę) i **anulowane**. Zamówienia nie można edytować ani usunąć. Rezerwacja nie wygasa sama.
- **Rezerwacja** dotyczy tylko towarów. Zarezerwowana ilość produktu to suma pozycji otwartych zamówień. **Dostępne do sprzedaży** = stan partii, które nie są przeterminowane, minus zarezerwowane.
- Zamówienie można zapisać tylko wtedy, gdy dla każdego towaru zamówiona ilość nie przekracza dostępnej do sprzedaży. Nie ma zamówień „na brak” ani rezerwacji częściowych.
- Rezerwację respektują wydania sprzedażowe: WZ wpisany ręcznie i faktura nie mogą zabrać towaru zarezerwowanego dla innych zamówień.
- RW i korekty dokumentów nie patrzą na rezerwacje, bo opisują to, co fizycznie się stało (strata, pomyłka). Zamówienie może wtedy stracić pokrycie. Szczegóły zamówienia pokazują, ile brakuje.
- **Faktura z zamówienia:** formularz faktury wypełnia się nabywcą i pozycjami zamówienia; nabywcy nie można zmienić, a ceny, rabaty i ilości tak. Wystawienie w jednej transakcji tworzy WZ (z użyciem rezerwacji tego zamówienia) i oznacza zamówienie jako zrealizowane w całości.
- **Anulowanie** otwartego zamówienia zwalnia rezerwację. Zrealizowanego zamówienia nie można anulować; jego skutki cofa anulowanie faktury, ale zamówienie zostaje zrealizowane.
- Ponowne wysłanie tego samego formularza (ten sam `RequestId`) nie tworzy drugiego zamówienia.
- **Współbieżność:** zapis zamówienia blokuje wiersze `Products` swoich towarów (rosnąco, jak dokumenty), więc rezerwacja i wydanie tego samego produktu idą po kolei. Faktura z zamówienia najpierw blokuje wiersz zamówienia, potem produkty; anulowanie blokuje tylko wiersz zamówienia. Kolejność jest stała, więc nie ma zakleszczeń.
- Rola bazy aplikacji nie ma `DELETE` na zamówieniach ani `UPDATE`/`DELETE` na pozycjach; w zamówieniu może zmieniać tylko status i powiązaną fakturę.

### 5.12. Proforma i faktura korygująca (poziom 2)

**Proforma:**

- Formularz faktury ma rodzaj: faktura VAT albo proforma. Proforma trafia do Fakturowni (`kind: "proforma"`) tak samo jak faktura, z ponawianiem i `oid`.
- Proforma nie jest sprzedażą: nie tworzy WZ, nie zmienia stanów ani rezerwacji i nie liczy się do analiz. Nie można jej wystawić z zamówienia, bo faktura z zamówienia je realizuje.
- Anulowanie działa jak dla faktury (sprawdzenie w Fakturowni), tylko bez cofania WZ.

**Faktura korygująca:**

- Korygować można fakturę VAT wysłaną do Fakturowni (ma jej identyfikator), raz. Korekty nie można skorygować.
- Dla każdej pozycji podaje się ilość, cenę netto i rabat po korekcie. Ilość może tylko zmaleć (do zera włącznie); zwiększenie sprzedaży to nowa faktura. Cena i rabat mogą zmienić się w obie strony. Wymagana jest przyczyna (do 500 znaków) i co najmniej jedna zmiana.
- Pozycja korekty zapisuje różnicę ilości i wartości (po minus przed, z VAT liczonym jak na fakturze od wartości po korekcie), cenę i rabat po korekcie oraz powiązanie z pozycją faktury. Dzięki temu suma faktury i korekty daje stan po korekcie, a analizy liczą sprzedaż i marżę bez osobnych reguł. Koszt jednostkowy pozostaje z faktury.
- Zmniejszenie ilości towaru oznacza zwrot: w tej samej transakcji powstaje korekta WZ faktury, która przyjmuje zwracaną ilość z powrotem na te same partie, po kosztach z wydania, zaczynając od partii wydanej jako ostatnia. Cena średnia liczy się jak przy korekcie WZ. Ponieważ WZ można skorygować raz, druga korekta tej samej faktury nie jest możliwa.
- Korekta trafia do Fakturowni jako `kind: "correction"` z `invoice_id` i `from_invoice_id` oryginału, przyczyną i pozycjami zmienionymi, każda z atrybutami przed i po.
- Korekty nie można anulować, bo zwrotu towaru nie da się cofnąć dokumentem tego samego rodzaju. Odrzuconą przez Fakturownię korektę można ponowić. To znane uproszczenie.
- Wystawienie korekty blokuje najpierw wiersz korygowanej faktury, potem produkty (przez korektę WZ), więc dwie korekty tej samej faktury idą po kolei.

### 5.13. Wysyłka faktury mailem i status płatności (poziom 2)

- Fakturę, która jest już w Fakturowni (każdego rodzaju), można wysłać nabywcy mailem: Regał prosi Fakturownię o wysłanie (`POST invoices/{id}/send_by_email.json`, `email_to`, `email_pdf=true`). Wysyła więc Fakturownia, z jej szablonem; Regał nie ma własnego serwera poczty.
- Domyślny adres to adres nabywcy z kartoteki, ostatnio użyty adres albo wpisany ręcznie. Przyjmowany jest jeden zwykły adres (bez nazwy i bez listy). Regał zapisuje, kiedy i na jaki adres wysłał, a w dzienniku działań powstaje wpis.
- Status płatności pochodzi z Fakturowni (`GET invoices/{id}.json`: `status`, `paid`, `paid_date`): `paid` to opłacona, `partial` albo kwota zapłacona większa od zera to częściowo opłacona, pozostałe to nieopłacona. Kwota może przyjść z przecinkiem dziesiętnym.
- Status odświeża przycisk „Sprawdź płatność” oraz proces w tle: co 10 minut najwyżej 20 nieopłaconych faktur sprawdzonych najdawniej, każda nie częściej niż co 6 godzin, z ostatnich 365 dni. Opłaconych nie sprawdza się ponownie. Gdy Fakturownia nie odpowiada, przebieg kończy się i wraca za 10 minut.
- Należności po terminie: nieopłacone lub częściowo opłacone faktury VAT, których termin płatności (data wystawienia + dni) minął; pulpit pokazuje ich liczbę i kwotę do zapłaty.
- Rola bazy aplikacji może zmieniać w `Invoices` także kolumny płatności i wysyłki, ale nadal nie kwoty ani pozycje.

### 5.14. Inwentaryzacja ze skanowaniem (poziom 2)

- Spis prowadzi się na arkuszu, który jest wersją roboczą zapisywaną po każdej zmianie (przerwane połączenie telefonu niczego nie gubi). Arkusz można anulować, dopóki nie jest zatwierdzony.
- Zeskanowanie albo wybranie towaru dodaje na arkusz wszystkie jego partie ze stanem większym od zera. Gdy towar ma jedną partię, kolejne skany dodają po jednej sztuce do policzonej ilości; przy kilku partiach ilość wpisuje się przy partii. Pozycję można usunąć.
- Arkusz obejmuje tylko to, co na nim jest: towary spoza arkusza zostają bez zmian (spis częściowy, np. jednego regału).
- Zatwierdzenie wymaga policzenia każdej pozycji (zero jest dozwolone). Stan oczekiwany to stan partii w chwili zatwierdzenia, odczytany pod blokadą wszystkich produktów arkusza; w czasie liczenia nie należy wydawać ani przyjmować tych towarów. Arkusz zapisuje stan oczekiwany przy każdej pozycji.
- Różnice księguje się w tej samej transakcji: niedobór to RW z partii, której dotyczy (powód „Niedobór inwentaryzacyjny INW/…”), nadwyżka to PW do tej samej partii po bieżącej cenie średniej. Produkt może wystąpić na RW raz, więc niedobory jednego produktu na kilku partiach dają kilka dokumentów RW. Dokumenty mają do 200 pozycji, a powstałe dokumenty wskazują swój arkusz.
- Nadwyżki nie da się przyjąć na przeterminowaną partię ani przy cenie średniej 0; zatwierdzenie jest wtedy odrzucane z wyjaśnieniem, a takie pozycje załatwia się ręcznie.
- Numeracja `INW/{numer}/{rok}` nadawana przy zatwierdzeniu, bez luk. Arkusz bez różnic też dostaje numer.
- Zatwierdzenie blokuje najpierw wiersz arkusza, potem wszystkie produkty rosnąco, a dopiero potem księguje dokumenty, więc kilka dokumentów w jednej transakcji nie łamie kolejności blokad. Zmiany pozycji też blokują wiersz arkusza.
- Zatwierdzonego ani anulowanego arkusza nie można zmienić; pilnuje tego serwis i wyzwalacz w bazie.

### 5.15. Dane firmy po NIP-ie (poziom 2)

- W oknie kontrahenta przycisk „Pobierz dane” obok NIP-u wypełnia nazwę i adres z wykazu podatników VAT Ministerstwa Finansów („biała lista”, `GET https://wl-api.mf.gov.pl/api/search/nip/{nip}?date=RRRR-MM-DD`, bez klucza). Dane można poprawić przed zapisem; nic nie zapisuje się samo.
- NIP jest najpierw normalizowany i sprawdzany sumą kontrolną; niepoprawny nie jest wysyłany. Data zapytania to dzisiejsza data w Polsce.
- Adres pochodzi z `workingAddress`, a gdy go nie ma, z `residenceAddress` (osoby fizyczne). Okno pokazuje status VAT („Czynny”, „Zwolniony”, „Niezarejestrowany”).
- Gdy wykaz nie zna NIP-u, nie odpowiada albo odrzuca zapytanie, okno mówi o tym, a wpisane dane zostają bez zmian.
- Zapytanie idzie tylko po kliknięciu, nigdy w tle ani przy zapisie, bo API nie podaje limitów. Testy używają udawanego wykazu.

### 5.16. Analizy klientów, prognoza zamówień i porównanie okresów (poziom 2)

- **Klienci:** nabywcy w okresie (filtr okresu i kategorii), od największej sprzedaży netto: liczba faktur VAT, sprzedaż netto, marża, średnia faktura i data ostatniego zakupu. Korekty pomniejszają sprzedaż, ale nie są liczone jako faktury; proformy się nie liczą.
- **Porównanie okresów:** strona sprzedaży zestawia wybrany okres z poprzednim okresem tej samej długości (bezpośrednio przed nim) i z tymi samymi datami rok wcześniej (29 lutego staje się 28 lutego), przy tych samych filtrach. Zmiana w procentach liczona jest względem wartości z danego wiersza; przy zerze „brak danych”.
- **Prognoza zamówień:** dla aktywnych towarów sprzedanych w historii (domyślnie 28 dni, kończy się dzisiaj) średnia dzienna sprzedaż = sprzedana ilość ÷ liczba dni. Dostępne = stan bez przeterminowanych partii minus rezerwacje. Zapas na = dostępne ÷ średnia dzienna. Sugerowane zamówienie = średnia × horyzont (domyślnie 14 dni) − dostępne, nie mniej niż 0, w górę do całych sztuk albo do 3 miejsc po przecinku. Lista pokazuje tylko towary do zamówienia, od najkrótszego zapasu. To świadomie prosta średnia, bez sezonowości, dni tygodnia i czasu dostawy.
- Historia i horyzont: od 1 do 365 dni.

### 5.17. Eksport analiz do Excela (poziom 2)

- Strony „Sprzedaż i marża”, „Analiza ABC”, „Towary zalegające”, „Klienci” i „Prognoza zamówień” mają przycisk „Excel”, który zapisuje to, co strona pokazuje, przy bieżących filtrach, jako plik `.xlsx`. Sprzedaż ma trzy arkusze: produkty, kategorie i porównanie okresów.
- Nazwa pliku zawiera okres, np. `sprzedaz-2026-01-01-2026-11-10.xlsx`, albo dzisiejszą datę dla stanów i prognozy.
- Kwoty i ilości są liczbami, a daty datami, więc w arkuszu można je sumować i sortować. Pierwszy wiersz to pogrubione nagłówki, zablokowane przy przewijaniu.
- Żaden tekst nie staje się formułą: tekst zaczynający się od `=`, `+`, `-`, `@`, tabulatora lub powrotu karetki dostaje prefiks cudzysłowu Excela (punkt 9, Excel injection).
- Plik powstaje na serwerze (ClosedXML 0.105.0, MIT) w trakcie połączenia Blazora, więc obowiązują te same uprawnienia co dla strony, i trafia do przeglądarki jako strumień (moduł `wwwroot/js/file-download.js`).

## 6. Technologie

| Obszar | Wybór |
|---|---|
| Platforma | .NET 10 (LTS), C# |
| Interfejs | Blazor Web App, tryb Interactive Server, MudBlazor 9 |
| Baza danych | PostgreSQL 18, EF Core z Npgsql, migracje |
| Logowanie | Uwierzytelnianie ciasteczkiem z dostawcą Google |
| Faktury | API Fakturowni (REST, JSON) |
| Kody kreskowe | Generowanie EAN-13 po stronie serwera, skanowanie biblioteką JavaScript (wybór na etapie) |
| Eksport (poziom 2) | ClosedXML 0.105.0 (MIT) |
| Testy | xUnit, bUnit 2.x, Testcontainers, Playwright, k6, BenchmarkDotNet |
| Uruchamianie | Docker Compose |
| CI i bezpieczeństwo | GitHub Actions, CodeQL, Dependabot, ochrona przed wysłaniem sekretów |
| Podgląd bazy | DBeaver, połączenie tylko do odczytu |
| Pomoc przy kodzie | Claude Code; autor odpowiada za kod i musi umieć go wyjaśnić |

## 7. Architektura

Jedna aplikacja podzielona na moduły, bez mikroserwisów:

```text
src/Regal.Web/
  Components/        układ i współdzielone komponenty
  Features/
    Auth/  Products/  Contractors/  Warehouse/  Invoices/  Orders/  Analytics/  Audit/
  Integrations/
    Fakturownia/     klient API i proces wysyłki w tle
  Data/              DbContext, encje, migracje, generator danych
tests/
  Regal.UnitTests/
  Regal.IntegrationTests/
  Regal.E2ETests/
  performance/
docs/
```

Zasady:

- Komponenty nie zawierają reguł biznesowych ani zapytań do bazy, tylko wywołują serwisy.
- Serwisy tworzą krótko żyjący `DbContext` przez `IDbContextFactory`, jeden na operację.
- Uprawnienia są sprawdzane w komponentach i w serwisach.
- Pieniądze i ilości to `decimal` w C# oraz `numeric` w PostgreSQL; czas jest zapisywany w UTC.
- Klient Fakturowni jest ukryty za interfejsem, żeby w testach podstawić udawaną implementację.

## 8. Model danych

| Tabela | Najważniejsze kolumny |
|---|---|
| `Users` | `Id`, `Email` (unikalny), `Role`, `IsActive` |
| `Warehouses` | `Id`, `Name`; na start jeden rekord |
| `Categories` | `Id`, `Name` |
| `Products` | `Id`, `Sku`, `Ean`, `Name`, `CategoryId`, `Unit`, `Kind`, `NetPrice`, `VatRate`, `HasExpiry`, `AverageCost`, `IsActive` |
| `Contractors` | `Id`, `Name`, `Nip`, `Address`, `Email`, `Phone`, `IsActive` |
| `Batches` | `Id`, `ProductId`, `Number` (unikalne razem), `ExpiryDate` |
| `Stock` | `WarehouseId`, `BatchId` (klucz złożony), `Quantity` z `CHECK (Quantity >= 0)` |
| `StockDocuments` | `Id`, `Type`, `Number` (unikalny), `StocktakeId` (dokumenty różnic inwentaryzacji), `ContractorId`, `UserId`, `ReversesDocumentId` (unikalny indeks częściowy: jedna korekta na dokument), `CreatedAt` (UTC), `DocumentDate` (Europe/Warsaw), `Notes`, `RequestId` (unikalny klucz idempotencji) |
| `StockMovements` | `Id`, `DocumentId`, `BatchId`, `QuantityChange`, `UnitCost` |
| `Invoices` | `Id`, `Kind` (VAT, proforma, korekta), `PaymentStatus`, `PaidAmount`, `PaidDate`, `PaymentCheckedAt`, `EmailedAt`, `EmailedTo`, `CorrectsInvoiceId` (korygowana faktura, unikalny), `CorrectionReason`, `Status`, `ExternalOrderId` (oid, unikalny), `FakturowniaId`, `Number`, `PdfUrl`, `ContractorId`, `StockDocumentId`, `UserId`, sumy netto, VAT i brutto, `RemoteGrossTotal`, `PaymentDays`, `PaymentMethod`, `IssueDate` (Europe/Warsaw), `IssuedAt` (UTC) |
| `InvoiceLines` | `Id`, `InvoiceId`, `ProductId`, `CorrectsLineId` (w korekcie: pozycja korygowana; ilość i wartości są wtedy różnicami), kopia nazwy i jednostki, ilość, cena netto, rabat %, stawka VAT, wartości netto, VAT i brutto, koszt jednostkowy |
| `OutboxMessages` | `Id`, `Type`, `InvoiceId`, `Payload` (pełne żądanie z chwili wystawienia), `Attempts`, `NextAttemptAt` (puste: nic do zrobienia), `LockedUntil` (dzierżawa), `LastError` |
| `DocumentCounters` | `Type`, `Year` (klucz złożony), `LastNumber` |
| `CustomerOrders` | `Id`, `Number` (unikalny), `Status`, `ContractorId`, `UserId`, `CreatedAt` (UTC), `OrderDate` (Europe/Warsaw), `Notes`, `RequestId` (unikalny), `InvoiceId` (faktura, która je zrealizowała) |
| `CustomerOrderLines` | `Id`, `OrderId`, `ProductId` (unikalne razem), `Quantity` z `CHECK (Quantity > 0)` |
| `OrderCounters` | `Year` (klucz), `LastNumber` |
| `Stocktakes` | `Id`, `Number` (nadawany przy zatwierdzeniu, unikalny), `Status`, `UserId`, `CreatedAt`, `ApprovedAt`, `Notes` |
| `StocktakeLines` | `Id`, `StocktakeId`, `BatchId` (unikalne razem), `Counted` (`CHECK >= 0`), `Expected` (stan przy zatwierdzeniu) |
| `StocktakeCounters` | `Year` (klucz), `LastNumber` |
| `AuditLog` | `Id`, `UserId`, `Action`, `EntityType`, `EntityId`, `Details`, `CreatedAt` |

Kluczowe reguły:

- **Typy:** ilości to `numeric(18,3)`, koszty `numeric(18,4)`, daty ważności `date`.
- **Współbieżność:** dokument najpierw blokuje wiersze `Products` swoich pozycji (`SELECT ... FOR UPDATE`, rosnąco po `Id`). Operacje na tym samym produkcie idą po kolei, co chroni stany i średnią, a stała kolejność zapobiega zakleszczeniom. Tę samą blokadę biorą zmiany z punktu 5.4, które zależą od partii lub stanu.
- **Wydanie:** warunkowy `UPDATE "Stock" ... WHERE "Quantity" >= @ilosc`; zero zmienionych wierszy oznacza brak towaru. Razem z `CHECK` to ostatnia bariera.
- **Numeracja:** licznik zwiększany w tej samej transakcji co dokument (`INSERT ... ON CONFLICT DO UPDATE ... RETURNING`), więc wycofanie nie zostawia luki. Blokada wiersza licznika sprawia, że dokumenty tego samego typu zapisują się po kolei od chwili pobrania numeru.
- **Transakcja wywołującego:** przyjęcie i wydanie można wykonać w cudzej otwartej transakcji; błąd cofa tylko dokument (savepoint). Faktura w etapie 3 tworzy WZ w ten sposób, raz na transakcję i zanim zablokuje inne produkty.
- **Niezmienność:** rola bazy używana przez działającą aplikację nie ma `UPDATE` ani `DELETE` na `StockDocuments`, `StockMovements`, `Batches`, `InvoiceLines` i `AuditLog`. Nie zmienia też `CustomerOrderLines`. W `Invoices` może zmieniać tylko kolumny `Status`, `FakturowniaId`, `Number`, `PdfUrl`, `RemoteGrossTotal` oraz kolumny płatności i wysyłki mailem, a w `CustomerOrders` tylko `Status` i `InvoiceId`. Z `Invoices`, `CustomerOrders` i `OutboxMessages` nie może niczego usuwać. Migracje wykonuje osobna rola.
- **Indeksy pod analizy:** pomiary na roku danych (136 962 ruchy) pokazały, że istniejące indeksy wystarczają: analizy roczne czytają prawie całe tabele, a pulpit korzysta z indeksu na dacie wystawienia. Wolny był raport zalegających przez kształt zapytania, który poprawiono (`docs/analiza-dynamiczna.md`, punkt 5).

## 9. Bezpieczeństwo

- Lista dozwolonych adresów; użytkownik spoza listy nie dostaje dostępu do żadnych danych.
- Sekrety (Google, token Fakturowni, hasło bazy) wyłącznie w User Secrets lokalnie i w `.env` dla Dockera; w repozytorium tylko `.env.example`.
- W publicznym repozytorium włączona ochrona przed wysłaniem sekretów i Dependabot.
- Token Fakturowni wysyłany w nagłówku `Authorization`, nigdy w adresie URL ani w logach.
- Walidacja danych na serwerze, m.in. ilość większa od zera i poprawna cyfra kontrolna EAN.
- Zapytania przez EF Core z parametrami; surowy SQL tylko z parametrami.
- Baza w Dockerze wystawiona tylko na `127.0.0.1`.
- Eksporty z neutralizacją komórek zaczynających się od `=`, `+`, `-` lub `@` (CSV/Excel injection).
- HTTPS poza `localhost`, także w tunelu do telefonu.

## 10. Analiza dynamiczna

Zakres według sylabusa „Dynamiczna analiza oprogramowania”, punkty 2–7. Wyniki trafiają do `docs/analiza-dynamiczna.md`.

| Pkt | Temat | Wykonanie | Dowód w dokumentacji |
|---|---|---|---|
| 2 | Testy jednostkowe | xUnit i bUnit, automatycznie w CI: cena średnia, marża, ABC, wybór partii, EAN | Zestawy testów, wyniki, pokrycie |
| 3 | Testy integracyjne | Testcontainers: dokumenty na PostgreSQL, wycofywanie transakcji, numeracja, równoczesne wydania, wysyłka faktur z udawaną Fakturownią | Strategia i wyniki |
| 4 | Testy funkcjonalne | Playwright: scenariusze z zestawami danych, np. PZ, wydanie partii, faktura, odmowa dostępu | Scenariusze, dane, wyniki |
| 5 | Testy wydajności | Kryteria, k6 dla żądań HTTP, BenchmarkDotNet i plany zapytań dla analiz na dużych danych | Wyniki i optymalizacja przed i po |
| 6 | Debugowanie | Co najmniej 3 prawdziwe błędy: debugger, logi, ślady wykonania (`dotnet-trace`) | Objaw, ślad, przyczyna, poprawka |
| 7 | Dobór narzędzi | Porównanie z alternatywami | Uzasadnienie wyboru |

Interactive Server działa przez stałe połączenie, więc zwykły test HTTP nie obejmuje kliknięć w interfejsie. Dokumentacja jasno określa, co mierzy każdy test.

**Cele wydajności** (potwierdzone pomiarem 10.11.2026: ABC 44 ms, raport sprzedaży ok. 150 ms, p95 286 ms, 0 błędów):

- 95% odpowiedzi HTTP poniżej 500 ms przy 20 równoczesnych użytkownikach.
- Analiza ABC i raport sprzedaży poniżej 1 s dla 100 000 ruchów.
- 0% błędów podczas testu.

Błędy i problemy są zapisywane od pierwszego dnia, bo służą też jako materiał do punktu 6 i do „przygód”.

## 11. Dokumenty do oddania

| Wymaganie prowadzącego | Plik |
|---|---|
| 1. Działający program bez błędów | Kod, `README.md` (PL i EN), zielone CI |
| 2. Opis funkcjonalności | `docs/funkcjonalnosci.md` |
| 3. Narzędzia i technologie | `docs/narzedzia.md` |
| 4. „Przygody” | `docs/przygody.md`, prowadzony na bieżąco |
| 5. Analiza dynamiczna | `docs/analiza-dynamiczna.md` |
| 6. Wkład i czego się nauczono | `docs/wklad.md`, uzupełniany po każdym etapie |
| Prezentacja | `docs/prezentacja/` i scenariusz pokazu na żywo |

Dokumentacja jest pisana w Markdown w repozytorium, a na koniec składana do PDF. Commity są po angielsku.

## 12. Plan

| Tydzień | Etap | Gotowe, gdy |
|---|---|---|
| 1 (9–15.11) | **Fundament** | Aplikacja działa z PostgreSQL w Dockerze, logowanie Google z listą adresów, CI zielone |
| 2 (16–22.11) | **Kartoteki i magazyn** | Produkty z EAN, kontrahenci, PZ/PW/WZ/RW, partie, stany, test równoczesnych wydań przechodzi |
| 3 (23–29.11) | **Faktury i telefon** | Faktura w Fakturowni z WZ, ponawianie bez duplikatów, skanowanie przez tunel HTTPS |
| 4 (30.11–6.12) | **Analizy** | Pulpit, sprzedaż z marżą, ABC, zalegające, generator danych |
| 5 (7–13.12) | **Analiza i oddanie** | Testy funkcjonalne i wydajności, optymalizacja, dokumentacja, prezentacja |

Testy jednostkowe i integracyjne powstają razem z każdą funkcją. Poziom 2 zaczynamy tylko wtedy, gdy etap bieżącego tygodnia jest gotowy przed czasem.

## 13. Definicja ukończenia

- Wszystkie funkcje poziomu 1 działają.
- Wszystkie testy i CI przechodzą.
- Projekt uruchamia się na czystym komputerze według `README.md`.
- Nie ma znanych błędów krytycznych, a pozostałe są opisane.
- Autor umie wyjaśnić każdy moduł.

## 14. Otwarte sprawy

- Wpisać dokładną datę oddania i formę dokumentacji.
- Sprawdzić, czy nazwa „Regał” nie koliduje z istniejącą marką oprogramowania.
- Założyć konto testowe Fakturowni na początku tygodnia 3 (okres próbny 30 dni).
- Utworzyć klienta OAuth w Google Cloud w tygodniu 1.
- Potwierdzić prefiks wewnętrznego EAN-13 w zasadach GS1.
- Sprawdzić w przeglądarce i na telefonie ekrany magazynu według `docs/scenariusze-testowe.md`, w tym czytnik USB.
- Uruchomić skrypt k6 (`tests/performance/k6/pages.js`) według README i dopisać jego wynik obok pomiaru z `Regal.LoadHost`.
- Na koncie testowym Fakturowni potwierdzić wysyłkę mailem i odczyt płatności (scenariusz F10).
- Na koncie testowym Fakturowni potwierdzić format proformy i korekty (scenariusze F8 i F9), zwłaszcza czy pozycja korekty ma mieć różnicę, czy stan po korekcie.
- Na koncie testowym Fakturowni potwierdzić: nagłówek `Authorization: Bearer` przy wyszukiwaniu po `oid`, odrzucanie duplikatu przy `oid_unique=yes`, nazwy pól odpowiedzi (`token`, `price_gross`) i zgodność sum (scenariusz F6).

Rozstrzygnięte w etapie „Magazyn”: wycena PW bez podanego kosztu (punkt 5.5).
