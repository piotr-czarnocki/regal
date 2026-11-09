using System.Text.Json;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Invoices;

namespace Regal.UnitTests;

public class InvoiceRulesTests
{
    private static InvoiceDraft Draft(params InvoiceDraftLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), ContractorId = 1, Lines = [.. lines] };

    private static InvoiceDraftLine Line(int productId = 1, decimal? price = null, decimal discount = 0) =>
        new() { ProductId = productId, Quantity = 1, NetPrice = price, DiscountPercent = discount };

    [Theory]
    [InlineData(2, 20, 0, "23", 40, 9.20, 49.20)]
    [InlineData(1, 100, 10, "23", 90, 20.70, 110.70)]
    [InlineData(3, 10, 0, "8", 30, 2.40, 32.40)]
    [InlineData(1, 100, 0, "zw", 100, 0, 100)]
    [InlineData(1, 100, 0, "np", 100, 0, 100)]
    [InlineData(1, 0.50, 0, "23", 0.50, 0.12, 0.62)] // 0.115 rounds half away from zero
    [InlineData(1.5, 9.99, 33.33, "5", 9.99, 0.50, 10.49)]
    [InlineData(1, 100, 100, "23", 0, 0, 0)]
    public void Line_amounts(double quantity, double price, double discount, string rate, double net, double vat, double gross) =>
        Assert.Equal(
            new InvoiceLineAmounts((decimal)net, (decimal)vat, (decimal)gross),
            InvoiceRules.Amounts((decimal)quantity, (decimal)price, (decimal)discount, rate));

    [Fact]
    public void Valid_draft_passes() => Assert.Null(InvoiceRules.Validate(Draft(Line(), Line(2, 10m, 5m))));

    [Fact]
    public void Draft_needs_a_request_id_a_buyer_and_lines()
    {
        var noRequest = Draft(Line());
        noRequest.RequestId = Guid.Empty;
        var noBuyer = Draft(Line());
        noBuyer.ContractorId = null;

        Assert.NotNull(InvoiceRules.Validate(noRequest));
        Assert.Equal("Wybierz nabywcę.", InvoiceRules.Validate(noBuyer));
        Assert.NotNull(InvoiceRules.Validate(Draft()));
    }

    [Fact]
    public void A_product_appears_once() => Assert.NotNull(InvoiceRules.Validate(Draft(Line(1), Line(1))));

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void Payment_term_is_between_0_and_365_days(int days)
    {
        var draft = Draft(Line());
        draft.PaymentDays = days;

        Assert.NotNull(InvoiceRules.Validate(draft));
    }

    [Fact]
    public void Payment_method_comes_from_the_list()
    {
        var draft = Draft(Line());
        draft.PaymentMethod = "bitcoin";

        Assert.NotNull(InvoiceRules.Validate(draft));
    }

    [Theory]
    [InlineData(-0.01, 0)]
    [InlineData(1.001, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 100.01)]
    [InlineData(10, 5.555)]
    public void Price_and_discount_are_checked(double price, double discount) =>
        Assert.NotNull(InvoiceRules.Validate(Draft(Line(price: (decimal)price, discount: (decimal)discount))));

    [Fact]
    public void Oid_is_stable_for_one_request()
    {
        var requestId = Guid.NewGuid();

        Assert.Equal(InvoiceRules.OidOf(requestId), InvoiceRules.OidOf(requestId));
        Assert.Equal($"regal-{requestId:N}", InvoiceRules.OidOf(requestId));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 60)]
    [InlineData(10, 60)]
    public void Retry_delay_doubles_up_to_an_hour(int attempts, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), InvoiceRetry.DelayAfter(attempts));

    [Fact]
    public void Payload_carries_the_oid_the_buyer_and_the_lines()
    {
        var invoice = new Invoice
        {
            ExternalOrderId = "regal-abc",
            PaymentMethod = "transfer",
            PaymentDays = 14,
            IssueDate = new DateOnly(2026, 11, 20),
            Lines =
            [
                new InvoiceLine { ProductName = "Mleko", Unit = "szt", VatRate = "5", Quantity = 2, GrossValue = 10.50m },
                new InvoiceLine { ProductName = "Montaż", Unit = "h", VatRate = "zw", Quantity = 1.5m, GrossValue = 150m },
            ],
        };
        var company = new Contractor { Name = "Firma", Nip = "1234563218", Address = "Długa 1, 00-001 Warszawa" };

        using var json = JsonDocument.Parse(InvoicePayload.Build(invoice, company));
        var root = json.RootElement.GetProperty("invoice");

        Assert.Equal("vat", root.GetProperty("kind").GetString());
        Assert.Equal("regal-abc", root.GetProperty("oid").GetString());
        Assert.Equal("yes", root.GetProperty("oid_unique").GetString());
        Assert.Equal("2026-11-20", root.GetProperty("issue_date").GetString());
        Assert.Equal(14, root.GetProperty("payment_to_kind").GetInt32());
        Assert.Equal("1", root.GetProperty("buyer_company").GetString());
        Assert.Equal("1234563218", root.GetProperty("buyer_tax_no").GetString());
        var positions = root.GetProperty("positions").EnumerateArray().ToList();
        Assert.Equal(("Mleko", "5", 2m, 10.50m), (positions[0].GetProperty("name").GetString(), positions[0].GetProperty("tax").GetString(), positions[0].GetProperty("quantity").GetDecimal(), positions[0].GetProperty("total_price_gross").GetDecimal()));
        Assert.Equal("zw", positions[1].GetProperty("tax").GetString());
        Assert.False(root.TryGetProperty("api_token", out _));
    }

    [Fact]
    public void Person_without_nip_is_not_a_company_and_sends_no_tax_number()
    {
        var invoice = new Invoice { ExternalOrderId = "regal-x", PaymentMethod = "cash" };

        using var json = JsonDocument.Parse(InvoicePayload.Build(invoice, new Contractor { Name = "Jan Kowalski" }));
        var root = json.RootElement.GetProperty("invoice");

        Assert.Equal("0", root.GetProperty("buyer_company").GetString());
        Assert.False(root.TryGetProperty("buyer_tax_no", out _));
    }
}
