using System.Numerics;

namespace xKsef.Domain.SalesInvoices.Calculation;

public static class FinancialMath
{
    public const decimal MaxAmount = 9999999999999999.99m;
    public static decimal RoundAmount(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static bool HasScale(decimal value, int scale) => decimal.Round(value, scale) == value;

    // Check decimal multiplication against its exact coefficient, including silent scale reduction.
    // Amounts remain decimal; BigInteger is used only to verify representability and compare allocation remainders.
    internal static decimal Multiply(decimal a, decimal b)
    {
        var result = checked(a * b);
        var (ac, asc) = Parts(a); var (bc, bsc) = Parts(b); var (rc, rsc) = Parts(result);
        var scale = Math.Max(asc + bsc, rsc);
        if (ac * bc * BigInteger.Pow(10, scale - asc - bsc) != rc * BigInteger.Pow(10, scale - rsc))
            throw new OverflowException("Utrata precyzji mnożenia decimal.");
        return result;
    }

    internal static (BigInteger Coefficient, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        return ((bits[3] & int.MinValue) != 0 ? -coefficient : coefficient, (bits[3] >> 16) & 255);
    }

    // Exact largest-remainder apportionment of an already rounded monetary target.
    public static IReadOnlyDictionary<Guid, decimal> Allocate(decimal target, IReadOnlyDictionary<Guid, decimal> weights)
    {
        if (target < 0 || !HasScale(target, 2) || weights.Any(x => x.Value < 0)) throw new ArgumentException("Nieprawidłowy podział kwoty.");
        if (weights.Count == 0) return target == 0 ? new Dictionary<Guid, decimal>() : throw new ArgumentException("Brak podstaw podziału.");
        var scale = weights.Max(x => Parts(x.Value).Scale);
        var integers = weights.ToDictionary(x => x.Key, x => Parts(x.Value).Coefficient * BigInteger.Pow(10, scale - Parts(x.Value).Scale));
        var total = integers.Values.Aggregate(BigInteger.Zero, (a, b) => a + b);
        if (total == 0)
        {
            if (target != 0) throw new ArgumentException("Zerowa podstawa podziału.");
            return weights.ToDictionary(x => x.Key, _ => 0m);
        }
        var cents = new BigInteger(target * 100m);
        var shares = integers.Select(x => { var quotient = BigInteger.DivRem(cents * x.Value, total, out var remainder); return (x.Key, Cents: quotient, Remainder: remainder); }).ToArray();
        var remaining = (int)(cents - shares.Aggregate(BigInteger.Zero, (sum, x) => sum + x.Cents));
        var winners = shares.OrderByDescending(x => x.Remainder).ThenBy(x => x.Key.ToString("N"), StringComparer.Ordinal).Take(remaining).Select(x => x.Key).ToHashSet();
        return shares.ToDictionary(x => x.Key, x => (decimal)(x.Cents + (winners.Contains(x.Key) ? 1 : 0)) / 100m);
    }

    internal static IReadOnlyDictionary<Guid, decimal> AllocateRounding(decimal target, IReadOnlyDictionary<Guid, decimal> unrounded)
    {
        var scale = Math.Max(2, unrounded.Values.Max(x => Parts(x).Scale));
        var denominator = BigInteger.Pow(10, scale - 2);
        var shares = unrounded.Select(x =>
        {
            var parts = Parts(x.Value);
            var coefficient = parts.Coefficient * BigInteger.Pow(10, scale - parts.Scale);
            var cents = BigInteger.DivRem(coefficient, denominator, out var remainder);
            return (x.Key, Cents: cents, Remainder: remainder);
        }).ToArray();
        var remaining = new BigInteger(target * 100m) - shares.Aggregate(BigInteger.Zero, (sum, x) => sum + x.Cents);
        if (remaining < 0 || remaining > shares.Length) throw new OverflowException("Niespójna kwota podziału zaokrągleń.");
        var winners = shares.OrderByDescending(x => x.Remainder).ThenBy(x => x.Key.ToString("N"), StringComparer.Ordinal).Take((int)remaining).Select(x => x.Key).ToHashSet();
        return shares.ToDictionary(x => x.Key, x => (decimal)(x.Cents + (winners.Contains(x.Key) ? 1 : 0)) / 100m);
    }
}
