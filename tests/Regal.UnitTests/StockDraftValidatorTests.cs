using Regal.Web.Data.Entities;
using Regal.Web.Features.Warehouse;

namespace Regal.UnitTests;

public class StockDraftValidatorTests
{
    private static StockDocumentDraft Draft(StockDocumentType type, int? contractorId, params StockDocumentLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), Type = type, ContractorId = contractorId, Notes = "powód", Lines = [.. lines] };

    private static StockDocumentLine Line(int productId = 1, decimal? cost = 5m, string? batch = null) =>
        new() { ProductId = productId, Quantity = 1, UnitCost = cost, BatchNumber = batch };

    [Theory]
    [InlineData(1, "szt", true)]
    [InlineData(1000, "SZT ", true)]
    [InlineData(0, "szt", false)]
    [InlineData(-1, "kg", false)]
    [InlineData(1.5, "szt", false)]
    [InlineData(1.5, " Szt", false)]
    [InlineData(1.5, "kg", true)]
    [InlineData(1.234, "kg", true)]
    [InlineData(1.2345, "kg", false)]
    [InlineData(1000000000, "kg", false)]
    public void Quantity_rules(double quantity, string unit, bool valid) =>
        Assert.Equal(valid, StockDraftValidator.ValidateQuantity((decimal)quantity, unit) is null);

    [Fact]
    public void Largest_quantity_is_accepted() =>
        Assert.Null(StockDraftValidator.ValidateQuantity(StockDraftValidator.MaxQuantity, "kg"));

    [Fact]
    public void Expiry_is_required_only_when_the_product_has_one()
    {
        var date = new DateOnly(2026, 6, 15);

        Assert.NotNull(StockDraftValidator.ValidateExpiry(null, hasExpiry: true, date));
        Assert.Null(StockDraftValidator.ValidateExpiry(null, hasExpiry: false, date));
    }

    [Fact]
    public void Expiry_can_be_the_document_date_but_not_earlier()
    {
        var date = new DateOnly(2026, 6, 15);

        Assert.Null(StockDraftValidator.ValidateExpiry(date, hasExpiry: true, date));
        Assert.NotNull(StockDraftValidator.ValidateExpiry(date.AddDays(-1), hasExpiry: true, date));
        Assert.NotNull(StockDraftValidator.ValidateExpiry(date.AddDays(-1), hasExpiry: false, date));
    }

    [Fact]
    public void Draft_needs_a_request_id()
    {
        var draft = Draft(StockDocumentType.PW, null, Line());
        draft.RequestId = Guid.Empty;

        Assert.NotNull(StockDraftValidator.Validate(draft));
    }

    [Fact]
    public void Draft_needs_at_least_one_line() =>
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null)));

    [Fact]
    public void Draft_can_have_200_lines_but_not_201()
    {
        var lines = Enumerable.Range(1, StockDraftValidator.MaxLines).Select(id => Line(id)).ToList();
        Assert.Null(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null, [.. lines])));

        lines.Add(Line(1000));
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null, [.. lines])));
    }

    [Fact]
    public void Notes_are_limited_to_500_characters()
    {
        var draft = Draft(StockDocumentType.PW, null, Line());
        draft.Notes = new string('x', 500);
        Assert.Null(StockDraftValidator.Validate(draft));

        draft.Notes = new string('x', 501);
        Assert.NotNull(StockDraftValidator.Validate(draft));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Rw_needs_a_reason(string? notes)
    {
        var draft = Draft(StockDocumentType.RW, null, Line());
        draft.Notes = notes;

        Assert.NotNull(StockDraftValidator.Validate(draft));
    }

    [Theory]
    [InlineData(StockDocumentType.PZ, null, false)]
    [InlineData(StockDocumentType.PZ, 1, true)]
    [InlineData(StockDocumentType.WZ, null, true)]
    [InlineData(StockDocumentType.WZ, 1, true)]
    [InlineData(StockDocumentType.PW, null, true)]
    [InlineData(StockDocumentType.PW, 1, false)]
    [InlineData(StockDocumentType.RW, null, true)]
    [InlineData(StockDocumentType.RW, 1, false)]
    public void Contractor_rules(StockDocumentType type, int? contractorId, bool valid) =>
        Assert.Equal(valid, StockDraftValidator.Validate(Draft(type, contractorId, Line())) is null);

    [Fact]
    public void Pz_requires_a_cost_and_pw_does_not()
    {
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.PZ, 1, Line(cost: null))));
        Assert.Null(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null, Line(cost: null))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.23456)]
    public void Given_cost_must_be_positive_with_at_most_four_decimals(double cost)
    {
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.PZ, 1, Line(cost: (decimal)cost))));
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null, Line(cost: (decimal)cost))));
    }

    [Fact]
    public void Issue_lists_a_product_once() =>
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.WZ, null, Line(1), Line(1))));

    [Fact]
    public void Receipt_lists_a_product_once_per_batch()
    {
        Assert.Null(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null, Line(1, batch: "A"), Line(1, batch: "B"))));
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null, Line(1, batch: "a1 "), Line(1, batch: "A1"))));
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null, Line(1), Line(1, batch: " "))));
    }

    [Fact]
    public void Batch_number_is_limited_to_50_characters() =>
        Assert.NotNull(StockDraftValidator.Validate(Draft(StockDocumentType.PW, null, Line(batch: new string('x', 51)))));

    [Theory]
    [InlineData(" ab-1 ", "AB-1")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Batch_numbers_are_normalized(string? input, string? expected) =>
        Assert.Equal(expected, BatchNumbers.Normalize(input));
}
