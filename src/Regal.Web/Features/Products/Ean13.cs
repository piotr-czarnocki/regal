using System.Globalization;

namespace Regal.Web.Features.Products;

/// <summary>
/// EAN-13 validation and generation of codes for use inside the company.
///
/// Internal codes use GS1 prefix 040. The GS1 prefix list reserves 040-049 for
/// "GS1 Restricted Circulation Numbers within a company". The better known 200-299
/// is reserved for numbers within a geographic region, defined by each national
/// GS1 organisation, so it is not free for one company to use. Restricted
/// circulation numbers must never leave the company's own processes.
/// Source: https://www.gs1.org/standards/id-keys/company-prefix
///
/// Layout: 040 + 9-digit sequence number + check digit.
/// </summary>
public static class Ean13
{
    public const long MaxInternalNumber = 999_999_999;

    private const int Length = 13;
    private const string InternalPrefix = "040";

    public static bool IsValid(string? code) =>
        code is { Length: Length }
        && code.All(char.IsAsciiDigit)
        && code[^1] - '0' == ComputeCheckDigit(code.AsSpan(0, Length - 1));

    /// <summary>True for the whole 040-049 range, which manually entered codes may not use.</summary>
    public static bool IsInReservedInternalRange(string code) => code.StartsWith("04", StringComparison.Ordinal);

    public static string CreateInternal(long number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, MaxInternalNumber);

        var body = InternalPrefix + number.ToString("D9", CultureInfo.InvariantCulture);
        return body + ComputeCheckDigit(body).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Multiplies the 12 digits by alternating weights 1, 3, 1, 3, ... from the left, and
    /// returns the digit that brings the sum up to the next multiple of 10.
    /// </summary>
    public static int ComputeCheckDigit(ReadOnlySpan<char> firstTwelveDigits)
    {
        var sum = 0;
        for (var i = 0; i < firstTwelveDigits.Length; i++)
        {
            var weight = i % 2 == 0 ? 1 : 3;
            sum += (firstTwelveDigits[i] - '0') * weight;
        }

        return (10 - (sum % 10)) % 10;
    }
}
