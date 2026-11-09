namespace Regal.Web.Features.Contractors;

public static class Nip
{
    private const int Length = 10;

    private static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    /// <summary>Removes the usual separators; returns null when blank.</summary>
    public static string? Normalize(string? nip)
    {
        if (string.IsNullOrWhiteSpace(nip))
        {
            return null;
        }

        return nip.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    /// <param name="normalizedNip">Value returned by Normalize.</param>
    public static bool IsValid(string? normalizedNip)
    {
        if (normalizedNip is not { Length: Length } || !normalizedNip.All(char.IsAsciiDigit))
        {
            return false;
        }

        // All zeros would pass the checksum, but is not a number the tax office issues.
        if (normalizedNip.All(digit => digit == '0'))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < Weights.Length; i++)
        {
            sum += (normalizedNip[i] - '0') * Weights[i];
        }

        // A remainder of 10 never equals a single digit, so such a number is rejected here too.
        return sum % 11 == normalizedNip[^1] - '0';
    }
}
