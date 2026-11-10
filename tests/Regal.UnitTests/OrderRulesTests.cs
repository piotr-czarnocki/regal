using Regal.Web.Data.Entities;
using Regal.Web.Features.Orders;

namespace Regal.UnitTests;

public class OrderRulesTests
{
    private static OrderDraft Valid() => new()
    {
        RequestId = Guid.NewGuid(),
        ContractorId = 1,
        Lines = [new OrderDraftLine { ProductId = 1, Quantity = 2 }],
    };

    [Fact]
    public void A_complete_draft_passes() => Assert.Null(OrderRules.Validate(Valid()));

    [Fact]
    public void A_draft_without_request_id_is_refused()
    {
        var draft = Valid();
        draft.RequestId = Guid.Empty;

        Assert.Contains("identyfikatora", OrderRules.Validate(draft));
    }

    [Fact]
    public void A_buyer_is_required()
    {
        var draft = Valid();
        draft.ContractorId = null;

        Assert.Equal("Wybierz nabywcę.", OrderRules.Validate(draft));
    }

    [Theory]
    [InlineData(0, "co najmniej jedną")]
    [InlineData(201, "najwyżej 200")]
    public void The_number_of_lines_is_limited(int count, string message)
    {
        var draft = Valid();
        draft.Lines = Enumerable.Range(1, count).Select(id => new OrderDraftLine { ProductId = id, Quantity = 1 }).ToList();

        Assert.Contains(message, OrderRules.Validate(draft));
    }

    [Fact]
    public void Two_hundred_lines_are_allowed()
    {
        var draft = Valid();
        draft.Lines = Enumerable.Range(1, OrderRules.MaxLines).Select(id => new OrderDraftLine { ProductId = id, Quantity = 1 }).ToList();

        Assert.Null(OrderRules.Validate(draft));
    }

    [Fact]
    public void A_product_appears_once()
    {
        var draft = Valid();
        draft.Lines.Add(new OrderDraftLine { ProductId = 1, Quantity = 3 });

        Assert.Contains("tylko raz", OrderRules.Validate(draft));
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Notes_have_at_most_500_characters_after_trimming(int length, bool valid)
    {
        var draft = Valid();
        draft.Notes = "  " + new string('x', length) + "  ";

        Assert.Equal(valid, OrderRules.Validate(draft) is null);
    }

    [Fact]
    public void Numbers_read_like_the_other_documents() => Assert.Equal("ZK/12/2026", OrderRules.Number(12, 2026));

    [Theory]
    [InlineData(CustomerOrderStatus.Open, "otwarte")]
    [InlineData(CustomerOrderStatus.Completed, "zrealizowane")]
    [InlineData(CustomerOrderStatus.Cancelled, "anulowane")]
    public void Statuses_have_polish_labels(CustomerOrderStatus status, string label) => Assert.Equal(label, OrderRules.StatusLabel(status));
}
