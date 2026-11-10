# Scenariusz pokazu na żywo

Pokaz trwa około 10 minut i przechodzi przez cały obieg towaru: przyjęcie, wydanie, faktura, analiza. Każdy krok ma plan awaryjny, bo na żywo zawodzi zwykle sieć albo telefon.

## Przygotowanie (dzień wcześniej)

1. Baza deweloperska z migracjami: `docker compose up -d`, potem `dotnet ef database update --project src/Regal.Web` (dokładne polecenie z rolą właściciela jest w README).
2. Dane przykładowe, przy zatrzymanej aplikacji:

   ```powershell
   dotnet run --project tools/Regal.DataGenerator -- --environment Development --seed 1 --prefix DEMO
   ```

   Domyślnie daje to 200 produktów, 68 kontrahentów i rok sprzedaży (ok. 8 faktur dziennie), w kilkadziesiąt sekund. Narzędzie wypisuje bazę docelową; trzeba sprawdzić, że to baza deweloperska.
3. Konto testowe Fakturowni (bez KSeF) wpisane w User Secrets, jeśli ma być pokazana prawdziwa wysyłka. Bez niego faktura zostaje „do wysłania”, co też da się pokazać (krok 5).
4. Tunel do telefonu: profil `tunnel` i ngrok ze stałą domeną (README, sekcja o telefonie). Sprawdzić logowanie Google na telefonie.
5. Przygotować jeden fizyczny produkt z kodem EAN (np. butelkę wody) i dodać go w kartotece z tym kodem, kategoria „Napoje”, jednostka „szt”.
6. Zrobić zrzuty ekranu każdego kroku na wypadek awarii i zapisać je w `docs/prezentacja/zrzuty/`.

## Przebieg

| Czas | Krok | Co pokazać | Co powiedzieć |
|---|---|---|---|
| 0:00 | 1. Logowanie | Strona logowania, „Zaloguj przez Google”, pulpit | Tylko adresy z listy; obcy dostaje odmowę i żadnych danych |
| 0:30 | 2. Pulpit | Sprzedaż i marża w miesiącu, partie z bliską datą, zalegające | Dane z generatora: rok działalności przez prawdziwe serwisy, nie wstawione do tabel |
| 1:30 | 3. Przyjęcie PZ telefonem | Na telefonie: Dokumenty → Nowy → PZ, dostawca, ikona aparatu, skan butelki, ilość 24, cena „1,85”, partia „L2611”, data ważności, Zatwierdź | Kod z aparatu trafia do pola jak wpisany. Przecinek dziesiętny działa (był błąd „2,50 → 250”) |
| 3:00 | 4. Stany | Na komputerze: Stany → wiersz produktu → partia L2611, cena średnia | Średnia ważona liczy się przy każdym przyjęciu; stan per partia |
| 3:45 | 5. Faktura | Faktury → Nowa: odbiorca, butelka × 3 i usługa „Dostawa lokalna” × 1, rabat 5% na wodę, Wystaw fakturę | Jedna transakcja: WZ z partii o najkrótszej ważności, faktura „do wysłania”, zadanie wysyłki. Wysyłka w tle, numer i PDF z Fakturowni po kilku sekundach |
| 5:00 | 6. WZ i historia | Szczegóły faktury → link do WZ → partie i koszt; historia produktu | Koszt zapisany przy wydaniu daje marżę, która nie zmienia się po kolejnych zakupach |
| 6:00 | 7. Korekta | Dokument PZ z kroku 3 → Korekta | Dokumentów się nie edytuje. Korekta PZ jest możliwa tylko wtedy, gdy towar z niej nadal jest na stanie; tu część sprzedano, więc system odmawia z wyjaśnieniem |
| 6:45 | 8. Analizy | Sprzedaż i marża (rok, wykres miesięczny), ABC (klasy, udział narastająco), Zalegające (90 dni) | ABC: A to ok. 20% produktów dających 80% sprzedaży, dzięki rozkładowi Zipfa w generatorze |
| 8:00 | 9. Testy i CI | GitHub → Actions: zielony przebieg; `docs/analiza-dynamiczna.md`: wyniki eksperymentu bez `FOR UPDATE` i pomiarów wydajności | Test współbieżności sprawdzony przez usunięcie blokady: bez niej 5 testów pada |
| 9:00 | Pytania | | |

## Plan awaryjny

| Problem | Co robić |
|---|---|
| Telefon nie łączy się przez tunel albo nie ma zasięgu | Krok 3 na komputerze: wpisać EAN w pole i nacisnąć Enter (to ta sama ścieżka co czytnik USB) |
| Aparat nie odczytuje kodu (światło, odblask) | Wpisać kod ręcznie w oknie skanera; pokazać, że okno o tym mówi |
| Fakturownia nie odpowiada | Faktura zostaje „do wysłania” albo „błąd wysyłki”. To dobra okazja, żeby pokazać ponawianie i przycisk „Ponów wysyłkę” |
| Logowanie Google nie działa (sieć) | Uruchomić testy w przeglądarce (`dotnet test tests/Regal.E2ETests`), które logują się kontem testowym, i pokazać zrzuty |
| Cokolwiek innego | Zrzuty ekranu z przygotowania |

## Pytania, które mogą paść

- **Co się stanie, gdy dwie osoby wydają ten sam towar naraz?** Dokument blokuje wiersze produktów (`SELECT … FOR UPDATE`, rosnąco po `Id`), więc operacje idą po kolei. Ostatnią barierą jest warunkowy `UPDATE` i `CHECK (Quantity >= 0)`. Test uruchamia równoczesne wydania na PostgreSQL.
- **Skąd pewność, że faktura nie powstanie dwa razy?** Każda faktura ma `oid`; przed utworzeniem wysyłka sprawdza, czy Fakturownia już ją ma, a zadanie pobiera z `FOR UPDATE SKIP LOCKED`. Test liczy żądania utworzenia.
- **Dlaczego Blazor Server, a nie SPA?** `narzedzia.md`.
- **Co zrobił Claude Code, a co autor?** `wklad.md`.
