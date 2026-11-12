# Mapa kodu: reguła → gdzie jest pilnowana → który test to dowodzi

Ten dokument jest przewodnikiem po kodzie do obrony projektu. Dla najważniejszych reguł z [`SPEC.md`](../SPEC.md) podaje miejsce w kodzie, które ich pilnuje, i test, który by się nie powiódł, gdyby reguła przestała działać. Nazwy metod zamiast numerów wierszy, bo numery szybko się dezaktualizują.

Skróty ścieżek: `Web/` to `src/Regal.Web/`, `IT/` to `tests/Regal.IntegrationTests/`, `UT/` to `tests/Regal.UnitTests/`, `E2E/` to `tests/Regal.E2ETests/`.

## Zasada ogólna

Każda reguła, której złamanie psuje dane, jest pilnowana dwa razy: w serwisie (czytelny komunikat dla człowieka) i w bazie (CHECK, wyzwalacz, uprawnienia kolumn), bo bazy nie da się obejść błędem w kodzie. Testy integracyjne działają na prawdziwym PostgreSQL w kontenerze, więc sprawdzają też blokady i ograniczenia bazy.

## Magazyn

| Reguła (SPEC) | Gdzie pilnowana | Dowód |
|---|---|---|
| Stan nigdy nie jest ujemny (5.4) | `Web/Features/Warehouse/StockDocumentService.cs`: `IssueAsync` sprawdza dostępność po zablokowaniu produktów; w bazie `CK_Stock_Quantity_NonNegative` (`Web/Data/RegalDbContext.cs`) | `IT/WarehouseConcurrencyTests.cs`: `Parallel_issues_exceeding_stock_exactly_one_succeeds_and_stock_stays_valid` |
| Blokady zawsze w tej samej kolejności, bez zakleszczeń (5.4) | `Web/Features/Products/ProductLocks.cs`: `SELECT … ORDER BY "Id" FOR UPDATE` | `IT/WarehouseConcurrencyTests.cs`: `Documents_listing_products_in_opposite_order_do_not_deadlock` |
| Średnia ważona ceny zakupu (5.5) | `Web/Features/Warehouse/AverageCost.cs`: `AfterReceipt`, `AfterReceiptReversal` | `UT/AverageCostTests.cs`; równolegle: `IT/WarehouseConcurrencyTests.cs`: `Parallel_receipts_of_one_product_give_the_correct_average` |
| Wydanie z partii o najbliższej dacie ważności, przeterminowane się nie wydają (5.4) | `Web/Features/Warehouse/BatchAllocator.cs` (kolejność po `ExpiryDate`) | `UT/BatchAllocatorTests.cs`; w przeglądarce: `E2E/WarehouseScenarioTests.cs`: `Issue_takes_the_batch_expiring_first_and_shows_it_in_the_preview` |
| Numeracja kolejna bez dziur i duplikatów (5.4) | `StockDocumentService.NewDocumentAsync`: licznik w tabeli `DocumentCounters` (`INSERT … ON CONFLICT … RETURNING`) | `IT/WarehouseConcurrencyTests.cs`: `Parallel_documents_get_consecutive_numbers_without_duplicates` |
| Ponowne wysłanie formularza tworzy jeden dokument | `RequestId` z unikalnym indeksem; `StockDocumentService.FindByRequestIdAsync` | `IT/WarehouseConcurrencyTests.cs`: `Parallel_sends_of_one_draft_create_one_document` |

## Inwentaryzacja (5.14)

| Reguła | Gdzie pilnowana | Dowód |
|---|---|---|
| Różnica liczona od stanu w chwili zatwierdzenia | `Web/Features/Warehouse/StocktakeService.cs`: `ApproveAsync` | `IT/StocktakeTests.cs`: `The_difference_is_against_the_stock_at_approval` |
| Zatwierdzenie blokuje wszystkie produkty przed księgowaniem RW i PW | `StocktakeService.ApproveAsync` (wiersz arkusza, potem produkty rosnąco) | `IT/StocktakeTests.cs`: `Approval_locks_all_its_products_before_posting_so_it_cannot_deadlock_with_an_issue` |
| Zamkniętego arkusza nie można zmienić | serwis oraz wyzwalacze `stocktakes_closed_stay_closed` i `stocktake_lines_only_in_draft` (migracja `AddStocktakes`) | `IT/StocktakeTests.cs`: `A_closed_sheet_refuses_every_change_in_the_service_and_in_the_database` |
| Skan dolicza sztukę tylko dla „szt” | `StocktakeService.AddProductAsync` | `IT/StocktakeTests.cs`: `Scanning_goods_sold_by_weight_puts_them_on_the_sheet_without_counting` |
| Cały spis na telefonie | strona `Web/Features/Warehouse/StocktakeDetailsPage.razor` | `E2E/StocktakeScenarioTests.cs`: `Stocktake_on_a_phone_counts_by_scanning_and_books_the_shortage_as_rw` |

## Zamówienia i rezerwacje (5.11)

| Reguła | Gdzie pilnowana | Dowód |
|---|---|---|
| Dostępne do sprzedaży = nieprzeterminowany stan − rezerwacje otwartych zamówień | `Web/Features/Warehouse/Reservations.cs`: `ReservedAsync`, `UnexpiredStockAsync`, `CheckSale` | `IT/OrderTests.cs`: `An_order_cannot_reserve_more_than_is_available_for_sale`, `Expired_batches_cannot_be_reserved` |
| Ręczne WZ i faktura bez zamówienia nie biorą zarezerwowanego towaru; RW tak | `StockDocumentService.CheckReservation` (tylko dla WZ) | `IT/OrderTests.cs`: `A_manual_wz_cannot_take_goods_reserved_for_an_order`, `Rw_ignores_reservations_and_the_order_shows_what_is_missing` |
| Równoległe zamówienia nie obiecują więcej niż jest | blokada produktów w `CustomerOrderService.CreateAsync` | `IT/OrderTests.cs`: `Parallel_orders_never_reserve_more_than_the_stock` |
| Faktura z zamówienia i anulowanie zamówienia nie zakleszczają się | `InvoiceService.LockOrderAsync` i `CustomerOrderService.CancelAsync`: najpierw wiersz zamówienia | `IT/OrderTests.cs`: `Cancelling_and_invoicing_one_order_at_the_same_time_leaves_one_outcome` |
| Aplikacja nie może przepisać ani usunąć zamówienia | `GRANT UPDATE ("Status", "InvoiceId")` (migracja `AddCustomerOrders`) | `IT/OrderTests.cs`: `Application_role_cannot_rewrite_or_delete_orders` |

## Faktury i Fakturownia (5.6, 5.12, 5.13)

| Reguła | Gdzie pilnowana | Dowód |
|---|---|---|
| WZ, faktura i zadanie wysyłki w jednej transakcji (outbox) | `Web/Features/Invoices/InvoiceService.cs`: `IssueAsync` | `IT/InvoiceTests.cs`: `Parallel_sends_of_one_draft_create_one_invoice_and_one_wz` |
| Każda faktura trafia do Fakturowni dokładnie raz | `Web/Features/Invoices/InvoiceSender.cs`: `ClaimAsync` (`FOR UPDATE SKIP LOCKED`), `FindOrCreateAsync` (szuka po `oid` przed utworzeniem) | `IT/InvoiceTests.cs`: `Parallel_senders_send_each_invoice_exactly_once`, `Duplicate_refused_by_fakturownia_is_resolved_by_looking_it_up` |
| Anulowanie nie ściga się z wysyłką | `InvoiceService.ReserveForCancelAsync` (blokada zadania wysyłki) | `IT/InvoiceTests.cs`: `Cancelling_is_refused_during_a_send_after_one_and_a_second_time` |
| Korekta zapisuje różnice i nie może dodać towaru | `InvoiceService.CorrectCoreAsync`; w bazie `CK_InvoiceLines_Quantity_Positive` (wiersz korekty ma ilość ≤ 0) | `IT/CorrectionTests.cs`: `Database_refuses_a_correction_line_that_adds_quantity` |
| Jedna korekta na fakturę, także przy dwóch kliknięciach naraz | `CorrectCoreAsync`: blokada wiersza korygowanej faktury | `IT/CorrectionTests.cs`: `Parallel_corrections_of_one_invoice_create_one` |
| Proforma nie zdejmuje towaru i nie liczy się do sprzedaży | `InvoiceService` (`goodsIds` puste dla proformy); filtry `Kind == Vat` w `Web/Features/Analytics/AnalyticsService.cs` | `IT/CorrectionTests.cs`: `A_proforma_is_sent_but_moves_no_stock_and_is_not_a_sale` |
| „Po terminie” według zegara aplikacji i czasu polskiego | `Web/Features/Warehouse/WarehouseTime.cs`: `DateOf`, `Display` | `IT/InvoicePagesTests.cs`: `Overdue_is_judged_by_the_application_clock_in_poland`; `UT/WarehouseTimeTests.cs` |
| Aplikacja zmienia w fakturze tylko kolumny wysyłki i płatności | `GRANT UPDATE (…)` w migracjach `AddInvoices` i `AddInvoicePaymentsAndEmail` | `IT/InvoiceTests.cs`: `Application_role_cannot_change_what_an_issued_invoice_says` |

## Dostęp i dziennik (5.1, 5.10)

| Reguła | Gdzie pilnowana | Dowód |
|---|---|---|
| Wejść może tylko adres z listy | `Web/Features/Auth/AuthService.cs`: `AuthorizeLoginAsync` | `IT/AuthServiceTests.cs`: `Address_outside_the_list_is_denied_and_audited` |
| Dezaktywacja kończy trwającą sesję | `AuthService.IsSessionValidAsync`, `ActiveUserAuthenticationStateProvider` | `IT/AuthServiceTests.cs`: `Session_stops_being_valid_after_deactivation` |
| Dziennika działań nie da się zmienić ani usunąć z aplikacji | rola bazy `regal_app` nie ma na `AuditLog` prawa `UPDATE` ani `DELETE` (migracja `InitialCreate`) | `IT/AuthServiceTests.cs`: `Application_role_cannot_change_the_audit_log` |
| Magazynier tylko w magazynie: serwisy sprzedaży, analiz, kartotek i użytkowników odmawiają (5.19) | `Web/Features/Auth/Roles.cs` (`Staff`), `CurrentUser.RequireUserIdAsync`; strony z `[Authorize(Roles = Roles.Administrator)]` | `IT/RoleTests.cs`: `A_warehouse_worker_gets_nothing_from_sales_catalog_analytics_or_users`, `A_warehouse_worker_receives_issues_and_counts_goods`; w przeglądarce `E2E/RoleScenarioTests.cs` |
| Nikt nie odbiera uprawnień sam sobie; zmiana roli kończy sesję (5.19) | `Web/Features/Auth/UserService.cs`: `SetRoleAsync`, `SetActiveAsync` | `IT/RoleTests.cs`: `Changing_the_role_ends_the_open_session_and_nobody_can_lock_themselves_out` |

## Generator danych (5.9)

| Reguła | Gdzie pilnowana | Dowód |
|---|---|---|
| Dane przechodzą przez prawdziwe serwisy i nigdy nie trafiają do Fakturowni | `Web/Data/Generator/DemoDataGenerator.cs` (klient `NoFakturownia`, faktury bez identyfikatora Fakturowni) | `IT/DemoDataGeneratorTests.cs`: `Generated_trade_follows_the_rules_and_is_never_sent` |
| Zamówienia, proformy i płatności poziomu 2 | `DemoDataGenerator.CompleteDueOrdersAsync`, `SetPaymentsAsync` | `IT/DemoDataGeneratorTests.cs`: `Generated_data_covers_orders_proformas_and_payments` |
| To samo ziarno daje te same dane | jeden `Random(seed)` dla całej symulacji | `IT/DemoDataGeneratorTests.cs`: `Same_seed_gives_the_same_trade` |

## Analiza produktów i kontrahentów (5.20)

| Reguła | Gdzie pilnowana | Dowód |
|---|---|---|
| Jedna definicja sprzedaży: karta i ranking dają te same liczby co analiza sprzedaży i klientów | `AnalyticsService.Lines`, używane przez `Web/Features/Analytics/InsightService.cs` | `IT/InsightTests.cs`: `The_product_card_counts_corrections_and_leaves_out_proformas_and_cancelled_invoices`, `The_contractor_card_shows_the_buyer_its_payments_and_orders` |
| Cofnięta dostawa znosi oryginał u dostawcy | `InsightService.Deliveries` (ruchy PZ ze znakiem) | `IT/InsightTests.cs`: `A_reversed_delivery_cancels_its_original_for_the_supplier` |
| Należności klienta liczone tak samo jak na pulpicie | `InvoicePaymentService.SummarizeReceivablesAsync` | `IT/InsightTests.cs`: `The_contractor_card_shows_the_buyer_its_payments_and_orders` |
| Średni czas zapłaty i odstęp między zakupami | `Web/Features/Analytics/InsightRules.cs` | `UT/InsightRulesTests.cs` |
| Każda strona analiz jest tylko dla administratora | `[Authorize(Roles = Roles.Administrator)]` | `UT/PageAccessTests.cs`: `Every_page_is_for_the_administrator_or_listed_as_open_to_the_warehouse` |
