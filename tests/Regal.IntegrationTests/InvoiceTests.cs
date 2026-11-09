using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Invoices;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class InvoiceTests(PostgresFixture postgres) : InvoiceTestBase(postgres)
{
    private sealed class DeniedUser : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => throw new UnauthorizedAccessException();
    }

    [Fact]
    public async Task Issuing_creates_the_wz_the_invoice_and_its_sending_task_together()
    {
        var goods = await StockedGoodsAsync(cost: 5m);
        var service = await NewServiceAsync();
        var draft = Draft(Item(goods, 2), Item(service, 1, discount: 10m));

        var invoice = await InvoiceAsync(await IssueOkAsync(draft));

        Assert.Equal(InvoiceStatus.ToSend, invoice.Status);
        Assert.Equal((40m, 9.20m, 49.20m, 5m), (invoice.Lines[0].NetValue, invoice.Lines[0].VatValue, invoice.Lines[0].GrossValue, invoice.Lines[0].UnitCost));
        Assert.Equal((90m, 20.70m, 110.70m, 0m), (invoice.Lines[1].NetValue, invoice.Lines[1].VatValue, invoice.Lines[1].GrossValue, invoice.Lines[1].UnitCost));
        Assert.Equal((130m, 29.90m, 159.90m), (invoice.NetTotal, invoice.VatTotal, invoice.GrossTotal));
        Assert.Equal(new DateOnly(2026, 6, 15), invoice.IssueDate);

        var wz = await DocumentAsync(invoice.StockDocumentId!.Value);
        Assert.Equal(StockDocumentType.WZ, wz.Type);
        Assert.Equal(-2m, Assert.Single(wz.Movements).QuantityChange);
        Assert.Equal(98m, await StockOfAsync(goods));

        var message = await MessageAsync(invoice.Id);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, message.NextAttemptAt);
        Assert.Contains($"\"oid\":\"{invoice.ExternalOrderId}\"", message.Payload);
        Assert.Contains("\"oid_unique\":\"yes\"", message.Payload);
    }

    [Fact]
    public async Task Invoice_with_only_services_has_no_wz()
    {
        var invoice = await InvoiceAsync(await IssueOkAsync(Draft(Item(await NewServiceAsync(), 3))));

        Assert.Null(invoice.StockDocumentId);
        Assert.Equal(300m, invoice.NetTotal);
    }

    [Fact]
    public async Task Shortage_rolls_back_the_whole_invoice_and_its_wz_number()
    {
        var plenty = await StockedGoodsAsync();
        var scarce = await StockedGoodsAsync(quantity: 1);
        var before = SequenceOf((await DocumentAsync(await PostOkAsync(Wz(Out(plenty, 1))))).Number);
        var draft = Draft(Item(plenty, 1), Item(scarce, 2));

        var result = await Invoices.IssueAsync(draft);

        Assert.Contains("brak towaru", result.Error);
        Assert.Equal(0, await InvoiceCountAsync(draft.RequestId));
        Assert.Equal(99m, await StockOfAsync(plenty));
        Assert.Equal(before + 1, SequenceOf((await DocumentAsync(await PostOkAsync(Wz(Out(plenty, 1))))).Number));
    }

    [Fact]
    public async Task Same_request_sent_again_returns_the_same_invoice()
    {
        var goods = await StockedGoodsAsync();
        var draft = Draft(Item(goods, 2));

        var first = await IssueOkAsync(draft);
        var second = await IssueOkAsync(draft);

        Assert.Equal(first, second);
        Assert.Equal(1, await InvoiceCountAsync(draft.RequestId));
        Assert.Equal(98m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Parallel_sends_of_one_draft_create_one_invoice_and_one_wz()
    {
        var goods = await StockedGoodsAsync();
        var service = await NewServiceAsync();
        var mixed = Draft(Item(goods, 2), Item(service, 1));
        var servicesOnly = Draft(Item(service, 1));
        var invoices = Invoices;

        var results = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => Task.Run(() => invoices.IssueAsync(mixed)))
                .Concat(Enumerable.Range(0, 6).Select(_ => Task.Run(() => invoices.IssueAsync(servicesOnly)))));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(1, await InvoiceCountAsync(mixed.RequestId));
        Assert.Equal(1, await InvoiceCountAsync(servicesOnly.RequestId));
        Assert.Equal(98m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Inactive_contractor_and_inactive_service_are_refused()
    {
        var goods = await StockedGoodsAsync();
        var service = await NewServiceAsync();
        await Products.SetActiveAsync(service, false);
        var contractor = await NewContractorAsync();
        await Contractors.SetActiveAsync(contractor, false);
        var toInactive = Draft(Item(goods, 1));
        toInactive.ContractorId = contractor;

        Assert.Contains("nieaktywny", (await Invoices.IssueAsync(toInactive)).Error);
        Assert.Contains("nieaktywny", (await Invoices.IssueAsync(Draft(Item(goods, 1), Item(service, 1)))).Error);
        Assert.Equal(100m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Sender_records_what_fakturownia_assigned()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));

        await DrainAsync();

        var invoice = await InvoiceAsync(id);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.NotNull(invoice.FakturowniaId);
        Assert.StartsWith("FV ", invoice.Number);
        Assert.EndsWith(".pdf", invoice.PdfUrl);
        var message = await MessageAsync(id);
        Assert.Null(message.NextAttemptAt);
        Assert.Equal(1, message.Attempts);

        await using var db = Postgres.CreateAppDbContext();
        var sent = await db.AuditLog.SingleAsync(e => e.EntityType == nameof(Invoice) && e.EntityId == id.ToString() && e.Action == AuditActions.InvoiceSent);
        Assert.Null(sent.UserId);
    }

    [Fact]
    public async Task Lost_reply_after_a_successful_create_does_not_create_a_second_invoice()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        var oid = (await InvoiceAsync(id)).ExternalOrderId;
        Fakturownia.LoseNextCreateReply();

        await DrainAsync();
        Assert.Equal(InvoiceStatus.ToSend, (await InvoiceAsync(id)).Status);
        Assert.Contains("nie odpowiedziała", (await MessageAsync(id)).LastError);

        Clock.Advance(TimeSpan.FromMinutes(1));
        await DrainAsync();

        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
        Assert.Equal(1, Fakturownia.CreatesFor(oid));
    }

    [Fact]
    public async Task Duplicate_refused_by_fakturownia_is_resolved_by_looking_it_up()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        var oid = (await InvoiceAsync(id)).ExternalOrderId;
        Fakturownia.Add(oid);
        Fakturownia.HideNextFind();

        await DrainAsync();

        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
        Assert.Equal(0, Fakturownia.CreatesFor(oid));
    }

    [Fact]
    public async Task Temporary_failures_are_retried_later_and_later()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        var delays = new List<TimeSpan>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Fakturownia.FindErrors.Enqueue(new FakturowniaUnavailableException("HTTP 503: niedostępna"));
            var before = Clock.GetUtcNow().UtcDateTime;
            Assert.True(await Sender.ProcessNextAsync(CancellationToken.None));
            var next = (await MessageAsync(id)).NextAttemptAt!.Value;
            delays.Add(next - before);

            Assert.False(await Sender.ProcessNextAsync(CancellationToken.None)); // not due yet
            Clock.SetUtcNow(new DateTimeOffset(next, TimeSpan.Zero));
        }

        Assert.Equal([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4)], delays);
        await DrainAsync();
        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
    }

    [Fact]
    public async Task After_the_last_attempt_the_invoice_waits_for_a_person_and_can_be_retried()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));

        for (var attempt = 0; attempt < InvoiceRetry.MaxAttempts; attempt++)
        {
            Fakturownia.FindErrors.Enqueue(new FakturowniaUnavailableException("Brak połączenia z Fakturownią."));
            await DrainAsync();
            Clock.Advance(TimeSpan.FromHours(1));
        }

        Assert.Equal(InvoiceStatus.Failed, (await InvoiceAsync(id)).Status);
        Assert.Null((await MessageAsync(id)).NextAttemptAt);

        Assert.True((await Invoices.RetryAsync(id)).IsSuccess);
        await DrainAsync();

        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
    }

    [Fact]
    public async Task Refusal_is_not_retried_automatically()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        Fakturownia.CreateErrors.Enqueue(new FakturowniaRejectedException("HTTP 422: {\"buyer_tax_no\":[\"is invalid\"]}"));

        await DrainAsync();
        Clock.Advance(TimeSpan.FromDays(1));
        await DrainAsync();

        Assert.Equal(InvoiceStatus.Failed, (await InvoiceAsync(id)).Status);
        Assert.Contains("422", (await MessageAsync(id)).LastError);
        Assert.Equal(1, (await MessageAsync(id)).Attempts);
        Assert.Equal("Ponowić można tylko fakturę z błędem wysyłki.", (await Invoices.RetryAsync(await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1))))).Error);
    }

    [Fact]
    public async Task Task_held_by_a_sender_that_stopped_is_taken_again_after_its_lease()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        await using (var db = Postgres.CreateAppDbContext())
        {
            var until = Clock.GetUtcNow().UtcDateTime + InvoiceSender.Lease;
            await db.Database.ExecuteSqlAsync($"UPDATE \"OutboxMessages\" SET \"LockedUntil\" = {until} WHERE \"InvoiceId\" = {id}");
        }

        Assert.False(await Sender.ProcessNextAsync(CancellationToken.None));

        Clock.Advance(InvoiceSender.Lease + TimeSpan.FromSeconds(1));
        await DrainAsync();

        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
    }

    [Fact]
    public async Task Parallel_senders_send_each_invoice_exactly_once()
    {
        var service = await NewServiceAsync();
        var ids = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            ids.Add(await IssueOkAsync(Draft(Item(service, 1))));
        }

        Fakturownia.CreateDelay = TimeSpan.FromMilliseconds(20);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(DrainAsync)));

        foreach (var id in ids)
        {
            var invoice = await InvoiceAsync(id);
            Assert.Equal(InvoiceStatus.Sent, invoice.Status);
            Assert.Equal(1, Fakturownia.CreatesFor(invoice.ExternalOrderId));
        }

        Assert.Equal(12, Fakturownia.TotalCreates);
    }

    [Fact]
    public async Task Cancelling_an_unsent_invoice_reverses_its_wz_and_stops_sending()
    {
        var goods = await StockedGoodsAsync();
        var id = await IssueOkAsync(Draft(Item(goods, 4)));

        var result = await Invoices.CancelAsync(id, Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        var invoice = await InvoiceAsync(id);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(100m, await StockOfAsync(goods));
        Assert.False(await Sender.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(0, Fakturownia.TotalCreates);

        await using var db = Postgres.CreateAppDbContext();
        Assert.True(await db.StockDocuments.AnyAsync(d => d.ReversesDocumentId == invoice.StockDocumentId));
    }

    [Fact]
    public async Task Invoice_that_exists_in_fakturownia_cannot_be_cancelled_and_gets_recorded_as_sent()
    {
        var goods = await StockedGoodsAsync();
        var id = await IssueOkAsync(Draft(Item(goods, 4)));
        Fakturownia.Add((await InvoiceAsync(id)).ExternalOrderId);

        var result = await Invoices.CancelAsync(id, Guid.NewGuid());

        Assert.Contains("istnieje w Fakturowni", result.Error);
        Assert.Equal(96m, await StockOfAsync(goods));
        await DrainAsync();
        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
    }

    [Fact]
    public async Task Cancelling_needs_fakturownia_to_answer()
    {
        var goods = await StockedGoodsAsync();
        var id = await IssueOkAsync(Draft(Item(goods, 4)));
        Fakturownia.FindErrors.Enqueue(new FakturowniaUnavailableException("Brak połączenia z Fakturownią."));

        var result = await Invoices.CancelAsync(id, Guid.NewGuid());

        Assert.Contains("Nie można teraz sprawdzić", result.Error);
        Assert.Equal(InvoiceStatus.ToSend, (await InvoiceAsync(id)).Status);
        Assert.Null((await MessageAsync(id)).LockedUntil);
        Assert.Equal(96m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Cancelling_is_refused_during_a_send_after_one_and_a_second_time()
    {
        var sending = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        await using (var db = Postgres.CreateAppDbContext())
        {
            var until = Clock.GetUtcNow().UtcDateTime + InvoiceSender.Lease;
            await db.Database.ExecuteSqlAsync($"UPDATE \"OutboxMessages\" SET \"LockedUntil\" = {until} WHERE \"InvoiceId\" = {sending}");
        }

        Assert.Contains("Trwa wysyłka", (await Invoices.CancelAsync(sending, Guid.NewGuid())).Error);

        var sent = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        await DrainAsync();
        Assert.Contains("została wysłana", (await Invoices.CancelAsync(sent, Guid.NewGuid())).Error);

        var cancelled = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        Assert.True((await Invoices.CancelAsync(cancelled, Guid.NewGuid())).IsSuccess);
        Assert.Contains("już anulowana", (await Invoices.CancelAsync(cancelled, Guid.NewGuid())).Error);
    }

    [Fact]
    public async Task Wz_of_a_standing_invoice_cannot_be_corrected_by_hand()
    {
        var goods = await StockedGoodsAsync();
        var invoice = await InvoiceAsync(await IssueOkAsync(Draft(Item(goods, 4))));

        var result = await ReverseAsync(invoice.StockDocumentId!.Value);

        Assert.Contains("anuluj fakturę", result.Error);
        Assert.Equal(96m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Sending_and_cancelling_at_the_same_time_never_cancel_an_existing_invoice()
    {
        var goods = await StockedGoodsAsync(quantity: 1000);
        Fakturownia.CreateDelay = TimeSpan.FromMilliseconds(10);

        for (var round = 0; round < 15; round++)
        {
            var id = await IssueOkAsync(Draft(Item(goods, 1)));
            var invoices = Invoices;
            var sender = Sender;

            await Task.WhenAll(
                Task.Run(() => sender.ProcessNextAsync(CancellationToken.None)),
                Task.Run(() => invoices.CancelAsync(id, Guid.NewGuid())));
            await DrainAsync();

            var invoice = await InvoiceAsync(id);
            var exists = Fakturownia.Has(invoice.ExternalOrderId);
            Assert.True(
                (invoice.Status == InvoiceStatus.Sent && exists) || (invoice.Status == InvoiceStatus.Cancelled && !exists),
                $"Round {round}: status {invoice.Status}, exists in Fakturownia: {exists}.");
        }
    }

    [Theory]
    [InlineData("UPDATE \"Invoices\" SET \"GrossTotal\" = 0")]
    [InlineData("UPDATE \"Invoices\" SET \"ContractorId\" = \"ContractorId\"")]
    [InlineData("DELETE FROM \"Invoices\"")]
    [InlineData("UPDATE \"InvoiceLines\" SET \"NetPrice\" = 0")]
    [InlineData("DELETE FROM \"InvoiceLines\"")]
    [InlineData("DELETE FROM \"OutboxMessages\"")]
    public async Task Application_role_cannot_change_what_an_issued_invoice_says(string sql)
    {
        await using var db = Postgres.CreateAppDbContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Application_role_can_still_change_the_status()
    {
        await using var db = Postgres.CreateAppDbContext();

        await db.Database.ExecuteSqlRawAsync("UPDATE \"Invoices\" SET \"Status\" = \"Status\" WHERE \"Id\" = -1");
    }

    [Fact]
    public async Task Every_invoice_method_requires_an_administrator()
    {
        var service = new InvoiceService(Postgres.DbContextFactory, new DeniedUser(), Documents, Fakturownia, new FakeTimeProvider());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.IssueAsync(Draft()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RetryAsync(1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CancelAsync(1, Guid.NewGuid()));
    }
}
