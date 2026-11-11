using System.Globalization;
using System.Text;

namespace Regal.Web.Features.Products;

/// <summary>
/// The bars of an EAN-13 code (GS1 General Specifications): a start guard 101, six digits in sets
/// L or G chosen by the first digit, a centre guard 01010, six digits in set R and an end guard 101,
/// 95 modules in all. The first digit has no bars of its own; it is the pattern of L and G.
/// </summary>
public static class Ean13Barcode
{
    public const int Modules = 95;

    private static readonly string[] L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
    private static readonly string[] G = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
    private static readonly string[] R = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
    private static readonly string[] Parity = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

    /// <summary>95 characters, '1' for a dark module. The code must be a valid EAN-13.</summary>
    public static string Pattern(string code)
    {
        if (!Ean13.IsValid(code))
        {
            throw new ArgumentException("A valid EAN-13 is expected.", nameof(code));
        }

        var digits = code.Select(c => c - '0').ToArray();
        var parity = Parity[digits[0]];
        var bars = new StringBuilder(Modules).Append("101");
        for (var i = 1; i <= 6; i++)
        {
            bars.Append(parity[i - 1] == 'L' ? L[digits[i]] : G[digits[i]]);
        }

        bars.Append("01010");
        for (var i = 7; i <= 12; i++)
        {
            bars.Append(R[digits[i]]);
        }

        return bars.Append("101").ToString();
    }

    /// <summary>
    /// The bars as SVG, one unit per module, with the guards reaching lower as on printed codes.
    /// Room is left at the sides (quiet zone) and below for the digits, which the label prints itself.
    /// </summary>
    public static string Svg(string code, int height = 60)
    {
        const int quiet = 9;
        var pattern = Pattern(code);
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Modules + (2 * quiet)} {height}\" shape-rendering=\"crispEdges\">");

        var start = -1;
        for (var module = 0; module <= pattern.Length; module++)
        {
            var dark = module < pattern.Length && pattern[module] == '1';
            if (dark && start < 0)
            {
                start = module;
            }
            else if (!dark && start >= 0)
            {
                var guard = start < 3 || start is >= 45 and < 50 || start >= 92;
                var barHeight = guard ? height : height - 6;
                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{start + quiet}\" y=\"0\" width=\"{module - start}\" height=\"{barHeight}\" fill=\"#000\"/>");
                start = -1;
            }
        }

        return svg.Append("</svg>").ToString();
    }
}
