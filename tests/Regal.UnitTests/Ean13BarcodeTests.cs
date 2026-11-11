using Regal.Web.Features.Products;

namespace Regal.UnitTests;

public class Ean13BarcodeTests
{
    // Decodes with the parity rule rather than the encoder's tables: L codes have an odd number of
    // dark modules, G and R an even one, and L/G order in the left half gives the first digit.
    private static readonly string[] FirstDigitParity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    [Theory]
    [InlineData("5901234123457")]
    [InlineData("4006381333931")]
    [InlineData("0401234000017")]
    public void The_pattern_has_95_modules_with_guards_and_the_right_parities(string code)
    {
        var pattern = Ean13Barcode.Pattern(code);

        Assert.Equal(95, pattern.Length);
        Assert.Equal("101", pattern[..3]);
        Assert.Equal("01010", pattern[45..50]);
        Assert.Equal("101", pattern[92..]);

        var left = Enumerable.Range(0, 6).Select(i => pattern.Substring(3 + (7 * i), 7)).ToList();
        var right = Enumerable.Range(0, 6).Select(i => pattern.Substring(50 + (7 * i), 7)).ToList();
        var parity = string.Concat(left.Select(c => c.Count(m => m == '1') % 2 == 1 ? 'L' : 'G'));
        Assert.Equal(code[0] - '0', Array.IndexOf(FirstDigitParity, parity));
        Assert.All(left, c => Assert.Equal('0', c[0]));   // left digits start light...
        Assert.All(right, c => Assert.Equal('1', c[0]));  // ...right digits start dark
        Assert.All(right, c => Assert.Equal(0, c.Count(m => m == '1') % 2));
    }

    [Fact]
    public void A_known_code_gives_its_published_pattern() =>
        // 5901234123457: first digit 5 gives LGGLLG, so 9 L, 0 G, 1 G, 2 L, 3 L, 4 G; then 1 2 3 4 5 7 in R.
        Assert.Equal(
            "101" + "0001011" + "0100111" + "0110011" + "0010011" + "0111101" + "0011101"
            + "01010" + "1100110" + "1101100" + "1000010" + "1011100" + "1001110" + "1000100" + "101",
            Ean13Barcode.Pattern("5901234123457"));

    [Theory]
    [InlineData("5901234123458")]
    [InlineData("123")]
    [InlineData("")]
    public void An_invalid_code_is_refused(string code) =>
        Assert.Throws<ArgumentException>(() => Ean13Barcode.Pattern(code));

    [Fact]
    public void The_svg_draws_one_rectangle_per_run_of_dark_modules()
    {
        var pattern = Ean13Barcode.Pattern("5901234123457");
        var runs = pattern.Split('0', StringSplitOptions.RemoveEmptyEntries).Length;

        var svg = Ean13Barcode.Svg("5901234123457");

        Assert.StartsWith("<svg", svg);
        Assert.Equal(runs, svg.Split("<rect").Length - 1);
    }
}
