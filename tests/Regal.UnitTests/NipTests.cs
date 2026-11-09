using Regal.Web.Features.Contractors;

namespace Regal.UnitTests;

public class NipTests
{
    [Fact]
    public void Correct_number_is_valid() => Assert.True(Nip.IsValid("1234563218"));

    [Theory]
    [InlineData("1234563219")] // wrong check digit
    [InlineData("9000000000")] // remainder 10, which no check digit can match
    [InlineData("0000000000")] // passes the checksum, but is not a real number
    [InlineData("123456321")] // too short
    [InlineData("12345632180")] // too long
    [InlineData("123456321X")]
    [InlineData("PL1234563218")]
    [InlineData(null)]
    public void Incorrect_numbers_are_invalid(string? nip) => Assert.False(Nip.IsValid(nip));

    [Theory]
    [InlineData("123-456-32-18", "1234563218")]
    [InlineData(" 123 456 32 18 ", "1234563218")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Separators_are_removed(string? input, string? expected) =>
        Assert.Equal(expected, Nip.Normalize(input));
}
