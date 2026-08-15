using System;
using System.Runtime.CompilerServices;

namespace EntityFrameworkCore.Jet.Data
{
    /// <summary>
    ///     Converts a <see cref="double" />/<see cref="float" /> to <see cref="decimal" /> the way .NET did
    ///     before .NET 11, by rounding the source to its true significant-digit count (15 for double, 7 for
    ///     float) rather than expanding its exact binary value.
    ///     <para>
    ///         Why this exists. Jet's <c>ROUND</c> is the VBA function, so it widens a Currency column to a
    ///         Double, and <c>SUM(ROUND(UnitPrice, 2))</c> comes back from ACE as a Double even though EF asks
    ///         for a decimal. Up to .NET 10, <c>Convert.ToDecimal(58.6d)</c> returned <c>58.6</c>: the runtime's
    ///         <c>VarDecFromR8</c> rounded to 15 digits precisely to "keep garbage digits out of the Decimal
    ///         we're making". dotnet/runtime#130566 (.NET 11 preview 7) replaced that with a correctly-rounded
    ///         full-precision conversion — see https://github.com/dotnet/runtime/pull/130566 — so the same call now yields
    ///         <c>58.600000000000001421085471520</c> — mathematically truer to the double, but it surfaces
    ///         binary noise that was never in the stored money value.
    ///     </para>
    ///     <para>
    ///         This is a faithful port of the pre-change <c>Decimal.DecCalc.VarDecFromR8</c>/<c>VarDecFromR4</c>,
    ///         rather than a <c>G15</c> string round-trip, so it reproduces the old results exactly — including
    ///         the overflow and flush-to-zero boundaries — and allocates nothing on a hot read path.
    ///     </para>
    ///     <para>
    ///         EF Core hit the same problem in the Cosmos provider, where JSON numbers are IEEE-754 doubles, and
    ///         fixed it with the <c>G15</c> round-trip
    ///         (<c>CosmosJsonDecimalReaderWriter.DoubleToDecimal</c>). Do not "simplify" this into that: the two
    ///         are not equivalent. <c>Convert.ToDecimal(-0.9892735183189034)</c> returned
    ///         <c>-0.989273518318904</c> on .NET 10, which this port reproduces, while the round-trip yields
    ///         <c>-0.989273518318903</c> — the mathematically correct 15-digit answer, but not the one that
    ///         shipped. The old algorithm scaled by a power of ten in double arithmetic before rounding, and that
    ///         error is part of the behaviour being restored. See <c>JetDecimalConverterTest</c>, which pins the
    ///         divergence and sweeps 20,000 values to confirm the two never differ by more than one unit in the
    ///         15th significant digit.
    ///     </para>
    /// </summary>
    public static class JetDecimalConverter
    {
        private const int DecScaleMax = 28;
        private const int ScaleShift = 16;

        private static ReadOnlySpan<uint> UInt32Powers10 =>
        [
            1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000
        ];

        /// <summary>10^n for n of 1-19.</summary>
        private static ReadOnlySpan<ulong> UInt64Powers10 =>
        [
            10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000,
            10000000000, 100000000000, 1000000000000, 10000000000000, 100000000000000,
            1000000000000000, 10000000000000000, 100000000000000000, 1000000000000000000,
            10000000000000000000
        ];

        private static ReadOnlySpan<double> DoublePowers10 =>
        [
            1, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9,
            1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19,
            1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28, 1e29,
            1e30, 1e31, 1e32, 1e33, 1e34, 1e35, 1e36, 1e37, 1e38, 1e39,
            1e40, 1e41, 1e42, 1e43, 1e44, 1e45, 1e46, 1e47, 1e48, 1e49,
            1e50, 1e51, 1e52, 1e53, 1e54, 1e55, 1e56, 1e57, 1e58, 1e59,
            1e60, 1e61, 1e62, 1e63, 1e64, 1e65, 1e66, 1e67, 1e68, 1e69,
            1e70, 1e71, 1e72, 1e73, 1e74, 1e75, 1e76, 1e77, 1e78, 1e79,
            1e80
        ];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetExponent(double d)
            => (uint)(BitConverter.DoubleToUInt64Bits(d) >> 52) & 0x7FFu;

        /// <summary>
        ///     <see cref="float" /> carries 7 significant digits, so it is widened and rounded at 7 rather than
        ///     15 — matching the old <c>VarDecFromR4</c>.
        /// </summary>
        public static decimal FromSingle(float value)
            => Convert(value, significantDigits: 7);

        /// <inheritdoc cref="JetDecimalConverter" />
        public static decimal FromDouble(double value)
            => Convert(value, significantDigits: 15);

        private static decimal Convert(double input, int significantDigits)
        {
            // The most we can scale by is 10^28, just over 2^93, so an exponent of -94 could barely reach 0.5;
            // anything smaller always rounds to zero.
            const uint dblBias = 1022;
            var exp = (int)(GetExponent(input) - dblBias);

            if (exp < -94)
            {
                return 0m;
            }

            if (exp > 96)
            {
                throw new OverflowException("Value was either too large or too small for a Decimal.");
            }

            var isNegative = false;
            if (input < 0)
            {
                input = -input;
                isNegative = true;
            }

            // Max power of 10 the value could have, via exponent * log10(2), scaled: .30103 * 65536 = 19728.3.
            var dbl = input;
            var power = significantDigits - 1 - ((exp * 19728) >> 16);
            var upperBound = DoublePowers10[significantDigits];
            var lowerBound = DoublePowers10[significantDigits - 1];

            if (power >= 0)
            {
                // Fewer digits than we can hold, so scale up.
                if (power > DecScaleMax)
                {
                    power = DecScaleMax;
                }

                dbl *= DoublePowers10[power];
            }
            else
            {
                if (power != -1 || dbl >= upperBound)
                {
                    dbl /= DoublePowers10[-power];
                }
                else
                {
                    power = 0; // didn't scale it
                }
            }

            if (dbl < lowerBound && power < DecScaleMax)
            {
                dbl *= 10;
                power++;
            }

            // Round to int64, half-to-even.
            var mant = (ulong)(long)dbl;
            dbl -= (long)mant;
            if (dbl > 0.5 || (dbl == 0.5 && (mant & 1) != 0))
            {
                mant++;
            }

            if (mant == 0)
            {
                return 0m;
            }

            uint low, mid, high;

            if (power < 0)
            {
                // Scale back up: -power is at most (29 - 15) = 14.
                power = -power;
                if (power < 10)
                {
                    var pow10 = UInt32Powers10[power];
                    var low64 = Math.BigMul((uint)mant, pow10);
                    var hi64 = Math.BigMul((uint)(mant >> 32), pow10);
                    low = (uint)low64;
                    hi64 += low64 >> 32;
                    mid = (uint)hi64;
                    high = (uint)(hi64 >> 32);
                }
                else
                {
                    var product = (System.UInt128)mant * UInt64Powers10[power - 1];
                    low = (uint)product;
                    mid = (uint)(product >> 32);
                    high = (uint)(product >> 64);

                    if (product >> 96 != 0)
                    {
                        throw new OverflowException("Value was either too large or too small for a Decimal.");
                    }
                }

                power = 0;
            }
            else
            {
                // Factor out powers of 10 to reduce the scale where the low digits are zero. At most 14, since
                // the value has `significantDigits` digits and the most significant one is non-zero.
                var lmax = power;
                if (lmax > 14)
                {
                    lmax = 14;
                }

                if ((byte)mant == 0 && lmax >= 8)
                {
                    const uint den = 100000000;
                    var div = mant / den;
                    if ((uint)mant == (uint)(div * den))
                    {
                        mant = div;
                        power -= 8;
                        lmax -= 8;
                    }
                }

                if (((uint)mant & 0xF) == 0 && lmax >= 4)
                {
                    const uint den = 10000;
                    var div = mant / den;
                    if ((uint)mant == (uint)(div * den))
                    {
                        mant = div;
                        power -= 4;
                        lmax -= 4;
                    }
                }

                if (((uint)mant & 3) == 0 && lmax >= 2)
                {
                    const uint den = 100;
                    var div = mant / den;
                    if ((uint)mant == (uint)(div * den))
                    {
                        mant = div;
                        power -= 2;
                        lmax -= 2;
                    }
                }

                if ((mant & 1) == 0 && lmax >= 1)
                {
                    const uint den = 10;
                    var div = mant / den;
                    if ((uint)mant == (uint)(div * den))
                    {
                        mant = div;
                        power--;
                    }
                }

                low = (uint)mant;
                mid = (uint)(mant >> 32);
                high = 0;
            }

            return new decimal((int)low, (int)mid, (int)high, isNegative, (byte)power);
        }
    }
}
