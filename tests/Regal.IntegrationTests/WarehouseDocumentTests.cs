using Microsoft.EntityFrameworkCore;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class WarehouseDocumentTests(PostgresFixture postgres) : WarehouseTestBase(postgres)
{
    [Fact]
    public async Task Receipt_creates_the_batch_the_stock_and_the_average()
    {
        var product = await NewGoodsAsync();

        var id = await PostOkAsync(Pz(In(product, 10, 5.5m, "dost-1")));

        var batch = Assert.Single(await BatchesOfAsync(product));
        Assert.Equal("DOST-1", batch.Number);
        Assert.Equal(10m, batch.Quantity);
        Assert.Equal(5.5m, await AverageOfAsync(product));

        var document = await DocumentAsync(id);
        Assert.Equal(new DateOnly(2026, 6, 15), document.DocumentDate);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, document.CreatedAt);
        Assert.Equal(UserId, document.UserId);
        var movement = Assert.Single(document.Movements);
        Assert.Equal(10m, movement.QuantityChange);
        Assert.Equal(5.5m, movement.UnitCost);
    }

    [Fact]
    public async Task Next_receipt_of_the_same_batch_adds_to_it_and_updates_the_average()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 10, 5m, "L1")));

        await PostOkAsync(Pz(In(product, 10, 7m, "l1 ")));

        var batch = Assert.Single(await BatchesOfAsync(product));
        Assert.Equal(20m, batch.Quantity);
        Assert.Equal(6m, await AverageOfAsync(product));
    }

    [Fact]
    public async Task Receipt_into_a_batch_with_another_expiry_is_refused_and_leaves_no_trace()
    {
        var product = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(product, 10, 5m, "L1", Today.AddDays(30))));

        var draft = Pz(In(product, 1, 9m, "L1", Today.AddDays(31)));
        var result = await Documents.PostAsync(draft);

        Assert.Contains("inną datę ważności", result.Error);
        Assert.Equal(10m, await StockOfAsync(product));
        Assert.Equal(5m, await AverageOfAsync(product));
        Assert.Equal(0, await DocumentCountAsync(draft.RequestId));
    }

    [Fact]
    public async Task Blank_batch_number_becomes_the_document_number()
    {
        var product = await NewGoodsAsync();

        var id = await PostOkAsync(Pw(In(product, 1, 4m)));

        Assert.Equal((await DocumentAsync(id)).Number, Assert.Single(await BatchesOfAsync(product)).Number);
    }

    [Fact]
    public async Task Expiry_is_checked_against_the_product_and_the_document_date()
    {
        var withExpiry = await NewGoodsAsync(hasExpiry: true);
        var withoutExpiry = await NewGoodsAsync();

        Assert.Contains("datę ważności", (await Documents.PostAsync(Pz(In(withExpiry, 1, 5m)))).Error);
        Assert.Contains("wcześniejsza", (await Documents.PostAsync(Pz(In(withExpiry, 1, 5m, expiry: Today.AddDays(-1))))).Error);
        await PostOkAsync(Pz(In(withExpiry, 1, 5m, expiry: Today)));
        await PostOkAsync(Pz(In(withoutExpiry, 1, 5m)));
        await PostOkAsync(Pz(In(withoutExpiry, 1, 5m, "Z-DATA", Today.AddDays(5))));
    }

    [Fact]
    public async Task Fractions_of_a_piece_are_refused_and_named_after_the_product()
    {
        var product = await NewGoodsAsync("szt");

        var result = await Documents.PostAsync(Pz(In(product, 1.5m, 5m)));

        Assert.Contains("liczbą całkowitą", result.Error);
    }

    [Fact]
    public async Task Shortage_on_one_line_rolls_back_the_document_and_the_next_gets_the_next_number()
    {
        var plenty = await NewGoodsAsync();
        var scarce = await NewGoodsAsync();
        await PostOkAsync(Pz(In(plenty, 10, 5m), In(scarce, 2, 5m)));
        var before = SequenceOf((await DocumentAsync(await PostOkAsync(Wz(Out(plenty, 1))))).Number);

        var failed = Wz(Out(plenty, 1), Out(scarce, 3));
        var result = await Documents.PostAsync(failed);

        Assert.Contains("brak towaru", result.Error);
        Assert.Equal(9m, await StockOfAsync(plenty));
        Assert.Equal(2m, await StockOfAsync(scarce));
        Assert.Equal(0, await DocumentCountAsync(failed.RequestId));

        var after = SequenceOf((await DocumentAsync(await PostOkAsync(Wz(Out(plenty, 1))))).Number);
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task Each_type_has_its_own_numbers()
    {
        var product = await NewGoodsAsync();
        var firstPz = SequenceOf((await DocumentAsync(await PostOkAsync(Pz(In(product, 10, 5m))))).Number);
        await PostOkAsync(Wz(Out(product, 1)));

        var secondPz = SequenceOf((await DocumentAsync(await PostOkAsync(Pz(In(product, 10, 5m))))).Number);

        Assert.Equal(firstPz + 1, secondPz);
    }

    [Fact]
    public async Task Number_and_date_use_the_year_in_warsaw()
    {
        var product = await NewGoodsAsync();
        Clock.SetUtcNow(new DateTimeOffset(2030, 12, 31, 23, 30, 0, TimeSpan.Zero));

        var document = await DocumentAsync(await PostOkAsync(Pz(In(product, 1, 5m))));

        Assert.Equal("PZ/1/2031", document.Number);
        Assert.Equal(new DateOnly(2031, 1, 1), document.DocumentDate);
        Assert.Equal(new DateTime(2030, 12, 31, 23, 30, 0, DateTimeKind.Utc), document.CreatedAt);
    }

    [Fact]
    public async Task Pw_without_cost_uses_the_average_and_needs_a_cost_when_the_average_is_zero()
    {
        var product = await NewGoodsAsync();

        Assert.Contains("podaj koszt", (await Documents.PostAsync(Pw(In(product, 1, null)))).Error);

        await PostOkAsync(Pw(In(product, 4, 10m)));
        var id = await PostOkAsync(Pw(In(product, 6, null, "BEZ-KOSZTU")));

        Assert.Equal(10m, Assert.Single((await DocumentAsync(id)).Movements).UnitCost);
        Assert.Equal(10m, await AverageOfAsync(product));
    }

    [Fact]
    public async Task Issue_records_the_current_average_and_does_not_change_it()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 10, 5m)));
        await PostOkAsync(Pz(In(product, 10, 7m)));

        var id = await PostOkAsync(Wz(Out(product, 4)));

        var movement = Assert.Single((await DocumentAsync(id)).Movements);
        Assert.Equal(-4m, movement.QuantityChange);
        Assert.Equal(6m, movement.UnitCost);
        Assert.Equal(6m, await AverageOfAsync(product));
        Assert.Equal(16m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Automatic_issue_goes_by_expiry_and_one_line_can_take_several_batches()
    {
        var product = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(product, 5, 5m, "POZNA", Today.AddDays(60))));
        await PostOkAsync(Pz(In(product, 5, 5m, "WCZESNA", Today.AddDays(10))));

        var id = await PostOkAsync(Wz(Out(product, 7)));

        Assert.Equal(2, (await DocumentAsync(id)).Movements.Count);
        var batches = await BatchesOfAsync(product);
        Assert.Equal(3m, batches.Single(b => b.Number == "POZNA").Quantity);
        Assert.Equal(0m, batches.Single(b => b.Number == "WCZESNA").Quantity);
    }

    [Fact]
    public async Task Chosen_batch_is_issued_instead_of_the_automatic_one()
    {
        var product = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(product, 5, 5m, "A", Today.AddDays(5)), In(product, 5, 5m, "B", Today.AddDays(50))));
        var chosen = (await BatchesOfAsync(product)).Single(b => b.Number == "B");

        await PostOkAsync(Wz(Out(product, 2, chosen.Id)));

        Assert.Equal(3m, (await BatchesOfAsync(product)).Single(b => b.Number == "B").Quantity);
    }

    [Fact]
    public async Task Wz_never_issues_expired_goods_and_rw_can()
    {
        var product = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(product, 5, 5m, "STARA", Today.AddDays(1))));
        var batch = Assert.Single(await BatchesOfAsync(product));
        Clock.Advance(TimeSpan.FromDays(2));

        Assert.Contains("Przeterminowane", (await Documents.PostAsync(Wz(Out(product, 1)))).Error);
        Assert.Contains("przeterminowana", (await Documents.PostAsync(Wz(Out(product, 1, batch.Id)))).Error);

        await PostOkAsync(Rw(Out(product, 1)));
        await PostOkAsync(Rw(Out(product, 1, batch.Id)));
        Assert.Equal(3m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Wz_correction_returns_goods_to_the_same_batches_at_the_same_cost()
    {
        var product = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(product, 3, 4m, "A", Today.AddDays(5)), In(product, 3, 8m, "B", Today.AddDays(9))));
        var wz = await PostOkAsync(Wz(Out(product, 5)));
        await PostOkAsync(Pz(In(product, 2, 20m, "C", Today.AddDays(40))));
        var averageBefore = await AverageOfAsync(product);

        var correction = await DocumentAsync(await ReverseOkAsync(wz));

        var original = await DocumentAsync(wz);
        Assert.Equal(wz, correction.ReversesDocumentId);
        Assert.Equal(StockDocumentType.WZ, correction.Type);
        Assert.Equal(
            original.Movements.OrderBy(m => m.BatchId).Select(m => (m.BatchId, -m.QuantityChange, m.UnitCost)),
            correction.Movements.OrderBy(m => m.BatchId).Select(m => (m.BatchId, m.QuantityChange, m.UnitCost)));

        var batches = await BatchesOfAsync(product);
        Assert.Equal(3m, batches.Single(b => b.Number == "A").Quantity);
        Assert.Equal(3m, batches.Single(b => b.Number == "B").Quantity);

        // (3 × avg + 5 × issue cost) / 8, with the issue cost being the 6.0000 average from before C.
        Assert.Equal(decimal.Round(((3m * averageBefore) + (5m * 6m)) / 8m, 4), await AverageOfAsync(product));
    }

    [Fact]
    public async Task Pz_correction_takes_the_receipt_out_of_stock_and_average()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 10, 5m, "A")));
        var second = await PostOkAsync(Pz(In(product, 10, 7m, "B")));

        await ReverseOkAsync(second);

        Assert.Equal(10m, await StockOfAsync(product));
        Assert.Equal(5m, await AverageOfAsync(product));
    }

    [Fact]
    public async Task Pz_correction_leaving_nothing_keeps_the_average()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 10, 5m)));

        await ReverseOkAsync(pz);

        Assert.Equal(0m, await StockOfAsync(product));
        Assert.Equal(5m, await AverageOfAsync(product));
    }

    [Fact]
    public async Task Pz_correction_after_a_partial_issue_is_refused()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 10, 5m)));
        await PostOkAsync(Wz(Out(product, 3)));

        var result = await ReverseAsync(pz);

        Assert.Contains("został już wydany", result.Error);
        Assert.Equal(7m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Pz_correction_is_allowed_when_current_batch_stock_covers_the_document()
    {
        var product = await NewGoodsAsync();
        var first = await PostOkAsync(Pz(In(product, 10, 5m, "A")));
        await PostOkAsync(Wz(Out(product, 3)));
        await PostOkAsync(Pz(In(product, 5, 5m, "A")));

        await ReverseOkAsync(first);

        Assert.Equal(2m, await StockOfAsync(product));
    }

    [Fact]
    public async Task A_document_can_be_corrected_once_and_a_correction_never()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 10, 5m)));
        var correction = await ReverseOkAsync(pz);

        Assert.Equal("Ten dokument ma już korektę.", (await ReverseAsync(pz)).Error);
        Assert.Equal("Korekty nie można skorygować.", (await ReverseAsync(correction)).Error);
        Assert.Equal(0m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Same_request_id_creates_one_document_and_returns_its_id()
    {
        var product = await NewGoodsAsync();
        var draft = Pz(In(product, 10, 5m));

        var first = await Documents.PostAsync(draft);
        var second = await Documents.PostAsync(draft);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await DocumentCountAsync(draft.RequestId));
        Assert.Equal(10m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Same_request_id_on_a_correction_returns_the_same_correction()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 10, 5m)));
        var requestId = Guid.NewGuid();

        var first = await Documents.ReverseAsync(pz, requestId);
        var second = await Documents.ReverseAsync(pz, requestId);

        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Inactive_product_is_refused()
    {
        var product = await NewGoodsAsync();
        Assert.True((await Products.SetActiveAsync(product, false)).IsSuccess);

        Assert.Contains("nieaktywny", (await Documents.PostAsync(Pz(In(product, 1, 5m)))).Error);
    }

    [Fact]
    public async Task Service_is_refused()
    {
        var service = await Products.CreateAsync(new ProductInput
        {
            Sku = Unique("USL"),
            Name = Unique("Usługa"),
            CategoryId = CategoryId,
            Unit = "h",
            Kind = ProductKind.Service,
        });

        Assert.Contains("usługa", (await Documents.PostAsync(Pz(In(service.Id!.Value, 1, 5m)))).Error);
    }

    [Fact]
    public async Task Inactive_contractor_is_refused()
    {
        var product = await NewGoodsAsync();
        var contractor = await NewContractorAsync();
        await Contractors.SetActiveAsync(contractor, false);
        var draft = Pz(In(product, 1, 5m));
        draft.ContractorId = contractor;

        Assert.Contains("nieaktywny", (await Documents.PostAsync(draft)).Error);
    }

    [Fact]
    public async Task Wz_may_have_a_contractor_or_none()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 2, 5m)));

        var withContractor = Wz(Out(product, 1));
        withContractor.ContractorId = SupplierId;

        Assert.Equal(SupplierId, (await DocumentAsync(await PostOkAsync(withContractor))).ContractorId);
        Assert.Null((await DocumentAsync(await PostOkAsync(Wz(Out(product, 1))))).ContractorId);
    }

    [Fact]
    public async Task Has_expiry_cannot_change_once_the_product_has_batches()
    {
        var product = await NewGoodsAsync();
        var input = await Products.GetForEditAsync(product);
        input!.HasExpiry = true;
        Assert.True((await Products.UpdateAsync(product, input)).IsSuccess);
        input.HasExpiry = false;
        Assert.True((await Products.UpdateAsync(product, input)).IsSuccess);

        await PostOkAsync(Pz(In(product, 1, 5m)));

        input.HasExpiry = true;
        Assert.Contains("ma już partie", (await Products.UpdateAsync(product, input)).Error);

        input.HasExpiry = false;
        input.NetPrice = 33m;
        Assert.True((await Products.UpdateAsync(product, input)).IsSuccess);
    }

    [Fact]
    public async Task Product_with_stock_cannot_be_deactivated_and_without_stock_can()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 2, 5m)));

        Assert.Contains("stan magazynowy", (await Products.SetActiveAsync(product, false)).Error);

        await PostOkAsync(Wz(Out(product, 2)));
        Assert.True((await Products.SetActiveAsync(product, false)).IsSuccess);
        Assert.True((await Products.SetActiveAsync(product, true)).IsSuccess);
    }

    [Fact]
    public async Task Correction_works_for_inactive_records_and_the_stock_list_marks_the_product()
    {
        var product = await NewGoodsAsync();
        var contractor = await NewContractorAsync();
        var receipt = Pz(In(product, 5, 5m));
        receipt.ContractorId = contractor;
        await PostOkAsync(receipt);
        var wz = Wz(Out(product, 5));
        wz.ContractorId = contractor;
        var wzId = await PostOkAsync(wz);
        await Products.SetActiveAsync(product, false);
        await Contractors.SetActiveAsync(contractor, false);

        await ReverseOkAsync(wzId);

        Assert.Equal(5m, await StockOfAsync(product));
        var name = (await Products.GetForEditAsync(product))!.Name;
        var row = Assert.Single((await Stock.SearchAsync(new StockFilter(null, name, null), 0, 10)).Items);
        Assert.False(row.IsActive);
        Assert.Equal(25m, row.Value);
    }

    [Fact]
    public async Task Approval_and_correction_are_written_to_the_audit_log()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 1, 5m)));
        var correction = await ReverseOkAsync(pz);
        var pzNumber = (await DocumentAsync(pz)).Number;
        var correctionNumber = (await DocumentAsync(correction)).Number;

        await using var db = Postgres.CreateAppDbContext();
        var posted = await db.AuditLog.SingleAsync(e => e.EntityType == nameof(StockDocument) && e.EntityId == pz.ToString());
        var reversed = await db.AuditLog.SingleAsync(e => e.EntityType == nameof(StockDocument) && e.EntityId == correction.ToString());

        Assert.Equal(AuditActions.StockDocumentPosted, posted.Action);
        Assert.Equal($"number={pzNumber}", posted.Details);
        Assert.Equal(UserId, posted.UserId);
        Assert.Equal(AuditActions.StockDocumentReversed, reversed.Action);
        Assert.Equal($"number={correctionNumber};reverses={pzNumber}", reversed.Details);
    }

    [Fact]
    public async Task Document_posted_in_the_callers_transaction_disappears_with_its_rollback()
    {
        var product = await NewGoodsAsync();
        var first = SequenceOf((await DocumentAsync(await PostOkAsync(Pw(In(product, 1, 5m))))).Number);
        var draft = Pw(In(product, 1, 5m, "W-TRANSAKCJI"));

        await using (var db = Postgres.CreateAppDbContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await Documents.PostInTransactionAsync(db, draft);
            Assert.True(result.IsSuccess, result.Error);
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await DocumentCountAsync(draft.RequestId));
        Assert.Equal(1m, await StockOfAsync(product));
        Assert.Equal(first + 1, SequenceOf((await DocumentAsync(await PostOkAsync(Pw(In(product, 1, 5m, "PO"))))).Number));
    }

    [Fact]
    public async Task Failed_document_leaves_the_callers_transaction_usable()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 2, 5m)));
        var failed = Wz(Out(product, 3));
        var name = Unique("Z-transakcji");

        await using (var db = Postgres.CreateAppDbContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            Assert.False((await Documents.PostInTransactionAsync(db, failed)).IsSuccess);

            db.Categories.Add(new Category { Name = name });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var check = Postgres.CreateAppDbContext();
        Assert.True(await check.Categories.AnyAsync(c => c.Name == name));
        Assert.Equal(0, await DocumentCountAsync(failed.RequestId));
        Assert.Equal(2m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Document_posted_in_the_callers_transaction_is_kept_by_its_commit()
    {
        var product = await NewGoodsAsync();
        var draft = Pw(In(product, 3, 5m));

        await using (var db = Postgres.CreateAppDbContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Documents.PostInTransactionAsync(db, draft);
            await transaction.CommitAsync();
        }

        Assert.Equal(1, await DocumentCountAsync(draft.RequestId));
        Assert.Equal(3m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Posting_in_a_transaction_needs_an_open_one()
    {
        await using var db = Postgres.CreateAppDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Documents.PostInTransactionAsync(db, Pw()));
    }

    [Fact]
    public async Task Search_filters_by_type_date_number_and_contractor()
    {
        var product = await NewGoodsAsync();
        var contractor = await NewContractorAsync();
        var name = (await Contractors.GetForEditAsync(contractor))!.Name!;
        var draft = Pz(In(product, 5, 5m));
        draft.ContractorId = contractor;
        var pz = await PostOkAsync(draft);
        Clock.Advance(TimeSpan.FromDays(3));
        var wz = await PostOkAsync(Wz(Out(product, 1)));
        var wzNumber = (await DocumentAsync(wz)).Number;

        Assert.Equal(pz, Assert.Single((await Documents.SearchAsync(new StockDocumentFilter(null, null, null, name), 0, 10)).Items).Id);
        Assert.Equal(wz, Assert.Single((await Documents.SearchAsync(new StockDocumentFilter(null, null, null, wzNumber), 0, 10)).Items).Id);
        Assert.Empty((await Documents.SearchAsync(new StockDocumentFilter(StockDocumentType.RW, null, null, wzNumber), 0, 10)).Items);
        Assert.Empty((await Documents.SearchAsync(new StockDocumentFilter(null, Today.AddDays(1), null, wzNumber), 0, 10)).Items);
        Assert.Single((await Documents.SearchAsync(new StockDocumentFilter(StockDocumentType.WZ, Today, Today, wzNumber), 0, 10)).Items);
    }

    [Fact]
    public async Task Details_link_the_original_and_the_correction_both_ways()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 2, 5m, "L1", Today.AddDays(9))));
        var correction = await ReverseOkAsync(pz);

        var original = await Documents.GetDetailsAsync(pz);
        var reversal = await Documents.GetDetailsAsync(correction);

        Assert.Equal(correction, original!.ReversedBy!.Id);
        Assert.Null(original.Reverses);
        Assert.Equal(pz, reversal!.Reverses!.Id);
        Assert.Null(reversal.ReversedBy);
        var row = Assert.Single(original.Movements);
        Assert.Equal("L1", row.BatchNumber);
        Assert.Equal(Today.AddDays(9), row.ExpiryDate);
        Assert.Equal(10m, row.Value);
    }

    [Fact]
    public async Task Preview_shows_the_allocation_and_changes_nothing()
    {
        var product = await NewGoodsAsync();
        var scarce = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 5m), In(scarce, 1, 5m)));

        var preview = await Documents.PreviewIssueAsync(Wz(Out(product, 2), Out(scarce, 2)));

        Assert.Equal(2m, Assert.Single(preview[0].Allocations).Quantity);
        Assert.Contains("brak towaru", preview[1].Error);
        Assert.Equal(5m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Stock_list_filters_by_category_and_expiry_and_marks_expired_batches()
    {
        var soon = await NewGoodsAsync(hasExpiry: true);
        var later = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(soon, 1, 5m, "S", Today.AddDays(2)), In(later, 1, 5m, "L", Today.AddDays(90))));
        var soonName = (await Products.GetForEditAsync(soon))!.Name;
        var laterName = (await Products.GetForEditAsync(later))!.Name;

        Assert.Single((await Stock.SearchAsync(new StockFilter(CategoryId, soonName, 7), 0, 10)).Items);
        Assert.Empty((await Stock.SearchAsync(new StockFilter(CategoryId, laterName, 7), 0, 10)).Items);
        Assert.Empty((await Stock.SearchAsync(new StockFilter(CategoryId + 100000, soonName, null), 0, 10)).Items);

        Clock.Advance(TimeSpan.FromDays(3));

        Assert.True(Assert.Single((await Stock.SearchAsync(new StockFilter(null, soonName, null), 0, 10)).Items).HasExpiredBatch);
        Assert.True(Assert.Single(await Stock.GetBatchesAsync(soon)).IsExpired);
    }

    [Fact]
    public async Task Product_history_lists_movements_newest_first()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 5m, "A")));
        var wz = await PostOkAsync(Wz(Out(product, 2)));

        var history = await Stock.GetProductMovementsAsync(product, 0, 10);

        Assert.Equal(2, history.TotalCount);
        Assert.Equal(wz, history.Items[0].DocumentId);
        Assert.Equal(-2m, history.Items[0].QuantityChange);
        Assert.Equal(StockDocumentType.PZ, history.Items[1].DocumentType);
    }

    [Fact]
    public async Task Product_can_be_found_by_full_code_and_by_the_12_digits_a_scanner_reports()
    {
        var product = await NewGoodsAsync();
        var ean = (await Products.GetForEditAsync(product))!.Ean!;

        Assert.Equal(product, (await Stock.FindByCodeAsync(ean))!.Id);
        Assert.Equal(product, (await Stock.FindByCodeAsync(ean[1..]))!.Id);
        Assert.Null(await Stock.FindByCodeAsync("abc"));
        Assert.Contains(await Stock.SearchGoodsAsync(ean, 10), p => p.Id == product);
    }
}
