using Microsoft.EntityFrameworkCore;
using Npgsql;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Invoices;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.IntegrationTests;

/// <summary>Emailing invoices and following their payment in Fakturownia (SPEC 5.13).</summary>
[Collection(PostgresCollection.Name)]
public class PaymentTests(PostgresFixture postgres) : InvoiceTestBase(postgres)
{
    private InvoicePaymentService Payments => new(Postgres.DbContextFactory, new FixedUser(UserId), Fakturownia, Clock);

    private async Task<Invoice> SentAsync(decimal quantity = 1)
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), quantity)));
        await DrainAsync();
        var invoice = await InvoiceAsync(id);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        return invoice;
    }

    /// <summary>Other tests leave invoices too, so passes run until nothing is due.</summary>
    private async Task RefreshAllDueAsync()
    {
        var payments = Payments;
        for (var pass = 0; pass < 50 && await payments.RefreshDueAsync(100) > 0; pass++)
        {
        }
    }

    [Fact]
    public async Task A_paid_invoice_is_read_with_its_amount_and_date()
    {
        var invoice = await SentAsync();
        Fakturownia.SetPayment(invoice.FakturowniaId!.Value, "paid", invoice.GrossTotal, Today);

        var result = await Payments.RefreshAsync(invoice.Id);

        Assert.True(result.IsSuccess, result.Error);
        var read = await InvoiceAsync(invoice.Id);
        Assert.Equal((InvoicePaymentStatus.Paid, invoice.GrossTotal, Today), (read.PaymentStatus, read.PaidAmount!.Value, read.PaidDate!.Value));
        Assert.NotNull(read.PaymentCheckedAt);
    }

    [Theory]
    [InlineData("partial", 10, InvoicePaymentStatus.Partial)]
    [InlineData("issued", 0, InvoicePaymentStatus.Unpaid)]
    [InlineData("sent", 0, InvoicePaymentStatus.Unpaid)]
    [InlineData("issued", 5, InvoicePaymentStatus.Partial)]
    public async Task Fakturownia_statuses_map_to_payment_states(string status, decimal paid, InvoicePaymentStatus expected)
    {
        var invoice = await SentAsync();
        Fakturownia.SetPayment(invoice.FakturowniaId!.Value, status, paid);

        await Payments.RefreshAsync(invoice.Id);

        Assert.Equal(expected, (await InvoiceAsync(invoice.Id)).PaymentStatus);
    }

    [Fact]
    public async Task An_invoice_not_yet_in_fakturownia_has_no_payment_to_check_or_email_to_send()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));

        Assert.Contains("już w Fakturowni", (await Payments.RefreshAsync(id)).Error);
        Assert.Contains("już w Fakturowni", (await Payments.SendByEmailAsync(id, "a@example.com")).Error);
        Assert.Equal(0, Fakturownia.PaymentCalls);
    }

    [Fact]
    public async Task The_background_pass_follows_unpaid_invoices_and_leaves_paid_and_recent_ones()
    {
        var unpaid = await SentAsync();
        var paid = await SentAsync();
        Fakturownia.SetPayment(paid.FakturowniaId!.Value, "paid", paid.GrossTotal, Today);
        await RefreshAllDueAsync();
        Assert.Equal(InvoicePaymentStatus.Paid, (await InvoiceAsync(paid.Id)).PaymentStatus);
        Assert.Equal(InvoicePaymentStatus.Unpaid, (await InvoiceAsync(unpaid.Id)).PaymentStatus);

        // Just checked, so nothing is due; after the interval only the unpaid one comes back.
        var calls = Fakturownia.PaymentCalls;
        await RefreshAllDueAsync();
        Assert.Equal(calls, Fakturownia.PaymentCalls);

        Clock.Advance(InvoicePaymentService.CheckEvery + TimeSpan.FromMinutes(1));
        Fakturownia.SetPayment(unpaid.FakturowniaId!.Value, "paid", unpaid.GrossTotal, Today);
        await RefreshAllDueAsync();
        Assert.Equal(InvoicePaymentStatus.Paid, (await InvoiceAsync(unpaid.Id)).PaymentStatus);
    }

    [Fact]
    public async Task The_background_pass_stops_when_fakturownia_is_unavailable_and_skips_refused_ones()
    {
        var invoice = await SentAsync();
        await RefreshAllDueAsync();
        Clock.Advance(InvoicePaymentService.CheckEvery + TimeSpan.FromMinutes(1));

        Fakturownia.PaymentErrors.Enqueue(new FakturowniaUnavailableException("timeout"));
        var checkedNow = await Payments.RefreshDueAsync(100);

        Assert.Equal(0, checkedNow);
        var before = (await InvoiceAsync(invoice.Id)).PaymentCheckedAt;
        Fakturownia.PaymentErrors.Enqueue(new FakturowniaRejectedException("HTTP 404"));
        await RefreshAllDueAsync();
        Assert.True((await InvoiceAsync(invoice.Id)).PaymentCheckedAt > before);
    }

    [Fact]
    public async Task Emailing_asks_fakturownia_and_records_when_and_to_whom()
    {
        var invoice = await SentAsync();

        var result = await Payments.SendByEmailAsync(invoice.Id, " ksiegowosc@sklep.example ");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains((invoice.FakturowniaId!.Value, "ksiegowosc@sklep.example"), Fakturownia.Emails);
        var read = await InvoiceAsync(invoice.Id);
        Assert.Equal("ksiegowosc@sklep.example", read.EmailedTo);
        Assert.NotNull(read.EmailedAt);
        await using var db = Postgres.CreateAppDbContext();
        Assert.True(await db.AuditLog.AnyAsync(e => e.Action == "InvoiceEmailed" && e.EntityId == invoice.Id.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nie-adres")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("Jan <a@example.com>")]
    public async Task Only_one_plain_address_is_accepted(string email)
    {
        var invoice = await SentAsync();

        var result = await Payments.SendByEmailAsync(invoice.Id, email);

        Assert.Equal("Podaj poprawny adres e-mail.", result.Error);
        Assert.DoesNotContain(Fakturownia.Emails, e => e.FakturowniaId == invoice.FakturowniaId);
    }

    [Fact]
    public async Task A_refused_email_is_reported_and_not_recorded()
    {
        var invoice = await SentAsync();
        Fakturownia.EmailErrors.Enqueue(new FakturowniaRejectedException("HTTP 422: limit"));

        var result = await Payments.SendByEmailAsync(invoice.Id, "a@example.com");

        Assert.Contains("odmówiła", result.Error);
        Assert.Null((await InvoiceAsync(invoice.Id)).EmailedAt);
    }

    [Fact]
    public async Task Receivables_count_unpaid_vat_invoices_and_those_past_their_due_date()
    {
        var before = await Payments.GetReceivablesAsync();
        var invoice = await SentAsync(quantity: 2);
        Fakturownia.SetPayment(invoice.FakturowniaId!.Value, "partial", 50m);
        await Payments.RefreshAsync(invoice.Id);

        var notYetDue = await Payments.GetReceivablesAsync();
        Clock.Advance(TimeSpan.FromDays(invoice.PaymentDays + 1));
        var overdue = await Payments.GetReceivablesAsync();

        Assert.Equal(before.UnpaidCount + 1, notYetDue.UnpaidCount);
        Assert.Equal(before.UnpaidGross + invoice.GrossTotal - 50m, notYetDue.UnpaidGross);
        Assert.True(overdue.OverdueCount >= before.OverdueCount + 1);
    }

    [Fact]
    public async Task Application_role_may_write_payment_columns_but_not_amounts_of_the_invoice()
    {
        var invoice = await SentAsync();
        await using var db = Postgres.CreateAppDbContext();

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Invoices\" SET \"PaidAmount\" = 1, \"EmailedTo\" = 'x@example.com' WHERE \"Id\" = {invoice.Id}");
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Invoices\" SET \"GrossTotal\" = 1 WHERE \"Id\" = {invoice.Id}"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
