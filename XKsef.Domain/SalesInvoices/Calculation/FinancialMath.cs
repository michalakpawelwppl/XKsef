using System.Numerics;

namespace xKsef.Domain.SalesInvoices.Calculation;

public static class FinancialMath
{
    public const decimal MaxAmount = 9999999999999999.99m;
    public static decimal RoundAmount(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static bool HasScale(decimal value, int scale) => decimal.Round(value, scale) == value;

    // Explicit decimal conversion of a rational intermediate, with up to 28 places and no binary floating point.
    internal static decimal Ratio(decimal value, decimal numerator, decimal denominator)
    {
        var (n, d) = Fraction(value, numerator, denominator);
        for (var scale = 28; scale >= 0; scale--)
        {
            var coefficient = RoundInteger(n * BigInteger.Pow(10, scale), d);
            if (coefficient <= new BigInteger(decimal.MaxValue)) return FromCoefficient(coefficient, scale);
        }
        throw new OverflowException();
    }

    internal static decimal RoundRatio(decimal value, decimal numerator, decimal denominator)
    {
        var (n, d) = Fraction(value, numerator, denominator);
        return FromCoefficient(RoundInteger(n * 100, d), 2);
    }

    private static (BigInteger Numerator, BigInteger Denominator) Fraction(decimal value, decimal numerator, decimal denominator)
    {
        if (value < 0 || numerator < 0 || denominator <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        var (v, vs) = Parts(value); var (n, ns) = Parts(numerator); var (d, ds) = Parts(denominator);
        return (v * n * BigInteger.Pow(10, ds), d * BigInteger.Pow(10, vs + ns));
    }

    private static BigInteger RoundInteger(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        return remainder * 2 >= denominator ? quotient + 1 : quotient;
    }

    private static decimal FromCoefficient(BigInteger value, int scale)
    {
        if (value < 0 || value > new BigInteger(decimal.MaxValue)) throw new OverflowException();
        var mask = new BigInteger(uint.MaxValue);
        return new decimal(unchecked((int)(uint)(value & mask)), unchecked((int)(uint)((value >> 32) & mask)), unchecked((int)(uint)((value >> 64) & mask)), false, (byte)scale);
    }

    // Unrounded proportional discount shares. Repair only the decimal representation residual, never VAT.
    internal static IReadOnlyDictionary<Guid, decimal> AllocateProportion(decimal target, IReadOnlyDictionary<Guid, decimal> weights)
    {
        var total = weights.Values.Aggregate(0m, (a, b) => checked(a + b));
        if (target < 0 || target > total) throw new ArgumentOutOfRangeException(nameof(target));
        if (total == 0) return weights.ToDictionary(x => x.Key, _ => 0m);
        var values = weights.ToDictionary(x => x.Key, x => Ratio(target, x.Value, total));
        var residual = target - values.Values.Aggregate(0m, (a, b) => checked(a + b));
        var largest = weights.OrderByDescending(x => x.Value).ThenBy(x => x.Key.ToString("N"), StringComparer.Ordinal).First().Key;
        values[largest] = checked(values[largest] + residual);
        if (values.Any(x => x.Value < 0 || x.Value > weights[x.Key])) throw new OverflowException();
        return values;
    }

    // Exact rational remainders: do not normalize shares by an already rounded VAT target.
    internal static IReadOnlyDictionary<Guid, decimal> AllocateRatio(decimal target, IReadOnlyDictionary<Guid, decimal> bases, decimal numerator, decimal denominator)
    {
        var scale = bases.Values.Max(x => Parts(x).Scale);
        var (n, ns) = Parts(numerator); var (d, ds) = Parts(denominator);
        var divisor = d * BigInteger.Pow(10, scale + ns);
        var shares = bases.Select(x =>
        {
            var (coefficient, itemScale) = Parts(x.Value);
            var dividend = coefficient * BigInteger.Pow(10, scale - itemScale + ds) * n * 100;
            var cents = BigInteger.DivRem(dividend, divisor, out var remainder);
            return (x.Key, Cents: cents, Remainder: remainder);
        }).ToArray();
        var remaining = new BigInteger(target * 100) - shares.Aggregate(BigInteger.Zero, (a, x) => a + x.Cents);
        if (remaining < 0 || remaining > shares.Length) throw new OverflowException("Niespójna alokacja podatku.");
        var winners = shares.OrderByDescending(x => x.Remainder).ThenBy(x => x.Key.ToString("N"), StringComparer.Ordinal).Take((int)remaining).Select(x => x.Key).ToHashSet();
        return shares.ToDictionary(x => x.Key, x => (decimal)(x.Cents + (winners.Contains(x.Key) ? 1 : 0)) / 100m);
    }

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
