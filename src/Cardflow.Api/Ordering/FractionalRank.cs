using System.Numerics;

namespace Cardflow.Api.Ordering;

public static class FractionalRank
{
    public static string Between(string? before, string? after)
    {
        Validate(before);
        Validate(after);

        if (before is not null && after is not null &&
            string.CompareOrdinal(before, after) >= 0)
        {
            throw new ArgumentException("The first rank must come before the second rank.");
        }

        var precision = Math.Max(before?.Length ?? 0, after?.Length ?? 0) + 1;
        var lower = before is null ? BigInteger.Zero : Parse(before) << (precision - before.Length);
        var upper = after is null ? BigInteger.One << precision : Parse(after) << (precision - after.Length);
        var middle = (lower + upper) / 2;

        return Format(middle, precision);
    }

    public static IReadOnlyList<string> Spread(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return [];

        var precision = 1;
        while ((BigInteger.One << precision) < (count + 1L) * 2)
        {
            precision++;
        }

        var step = (BigInteger.One << precision) / (count + 1L);
        return Enumerable.Range(1, count)
            .Select(index => Format(step * index, precision)).ToArray();
    }

    private static BigInteger Parse(string rank)
    {
        var value = BigInteger.Zero;
        foreach (var digit in rank)
        {
            value = (value << 1) + (digit == '1' ? BigInteger.One : BigInteger.Zero);
        }

        return value;
    }

    private static void Validate(string? rank)
    {
        if (rank is null)
        {
            return;
        }

        if (rank.Length == 0 || rank[^1] != '1' || rank.Any(digit => digit is not ('0' or '1')))
        {
            throw new ArgumentException("A rank must contain only 0 and 1 and end in 1.");
        }
    }

    private static string Format(BigInteger value, int precision)
    {
        while (value.IsEven)
        {
            value >>= 1;
            precision--;
        }

        var characters = new char[precision];
        for (var index = precision - 1; index >= 0; index--)
        {
            characters[index] = value.IsEven ? '0' : '1';
            value >>= 1;
        }

        return new string(characters);
    }
}
