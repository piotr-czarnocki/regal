using Regal.Web.Features.Products;

namespace Regal.UnitTests;

public class Ean13Tests
{
    [Theory]
    [InlineData("5901234123457")]
    [InlineData("4006381333931")]
    [InlineData("0400000000015")]
    public void Correct_codes_are_valid(string code) => Assert.True(Ean13.IsValid(code));

    [Theory]
    [InlineData("5901234123458")] // wrong check digit
    [InlineData("590123412345")] // too short
    [InlineData("59012341234570")] // too long
    [InlineData("590123412345X")]
    [InlineData("٥٩٠١٢٣٤١٢٣٤٥٧")] // digits, but not ASCII ones
    [InlineData("")]
    [InlineData(null)]
    public void Incorrect_codes_are_invalid(string? code) => Assert.False(Ean13.IsValid(code));

    [Fact]
    public void Check_digit_is_zero_when_the_sum_is_a_multiple_of_ten()
    {
        // Weighted sum: 1 * 1 + 3 * 3 = 10, so no digit needs adding (and not 10).
        Assert.Equal(0, Ean13.ComputeCheckDigit("000000000013"));
    }

    [Theory]
    [InlineData(1, "0400000000015")]
    [InlineData(999_999_999, "0409999999997")]
    public void Internal_codes_have_the_expected_form(long number, string expected) =>
        Assert.Equal(expected, Ean13.CreateInternal(number));

    [Theory]
    [InlineData(1)]
    [InlineData(123_456_789)]
    [InlineData(999_999_999)]
    public void Internal_codes_pass_validation_and_are_in_the_reserved_range(long number)
    {
        var code = Ean13.CreateInternal(number);

        Assert.True(Ean13.IsValid(code));
        Assert.True(Ean13.IsInReservedInternalRange(code));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000_000)]
    public void Numbers_outside_the_sequence_are_rejected(long number) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Ean13.CreateInternal(number));

    [Theory]
    [InlineData("0409999999997", true)]
    [InlineData("0499999999999", true)]
    [InlineData("0390000000000", false)]
    [InlineData("5901234123457", false)]
    public void Reserved_range_covers_040_to_049(string code, bool expected) =>
        Assert.Equal(expected, Ean13.IsInReservedInternalRange(code));
}
