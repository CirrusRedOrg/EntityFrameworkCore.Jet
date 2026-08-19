using System;
using System.Globalization;
using EntityFrameworkCore.Jet.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    /// <summary>
    /// Differential tests for <see cref="JetDecimalConverter"/>, which restores the pre-.NET-11 double-to-decimal
    /// behaviour (15 significant digits for double, 7 for float) that dotnet/runtime#130566 replaced with a
    /// correctly-rounded full-precision conversion.
    ///
    /// EF Core hit the same problem in the Cosmos provider — JSON numbers are IEEE-754 doubles, so materializing a
    /// decimal has to convert one — and fixed it by round-tripping through a "G15" string:
    ///
    ///     decimal.Parse(value.ToString("G15", InvariantCulture), NumberStyles.Float, InvariantCulture)
    ///
    /// We port the old runtime algorithm instead: it allocates nothing on a hot read path, reproduces the original
    /// overflow and flush-to-zero boundaries exactly rather than approximately, and extends to float at 7 digits.
    /// These tests pin that the two approaches agree, so the port is validated against the approach the EF team
    /// adopted, and so anyone tempted to "simplify" ours into theirs can see what the swap would cost.
    /// </summary>
    [TestClass]
    public class JetDecimalConverterTest
    {
        /// <summary>The upstream Cosmos approach, verbatim, as the reference implementation.</summary>
        private static decimal G15RoundTrip(double value)
            => decimal.Parse(
                value.ToString("G15", CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);

        [TestMethod]
        public void Matches_the_G15_round_trip_for_the_values_that_caused_the_regression()
        {
            // 58.6 is SUM(ROUND(UnitPrice, 2)) from Sum_over_round_works_correctly_in_projection; 21.35 is the
            // example in EF Core's own Cosmos fix; -1.1111 is the scaffolding default-value literal.
            double[] values = [58.6, 21.35, -1.1111, 0.1 + 0.2, 1.0 / 3.0, 12.75, 263.5, 1e-5, -0.00005];

            foreach (var value in values)
            {
                Assert.AreEqual(G15RoundTrip(value), JetDecimalConverter.FromDouble(value), $"for {value:R}");
            }
        }

        [TestMethod]
        public void Reproduces_the_pre_net11_runtime_where_the_G15_round_trip_does_not()
        {
            // The two approaches are NOT equivalent, and this value is the proof. Verified by running
            // Convert.ToDecimal on .NET 10 (runtime 10.0.11), which is the behaviour we are restoring:
            //
            //     Convert.ToDecimal(-0.9892735183189034)  =>  -0.989273518318904   (and JetDecimalConverter)
            //     G15 round-trip                          =>  -0.989273518318903
            //
            // Mathematically correct 15-digit rounding gives ...903, because the 16th digit is a 4. The old
            // runtime gave ...904 because VarDecFromR8 scaled by a power of ten in double arithmetic before
            // rounding, carrying its own ulp of error. So the G15 round-trip is an idealisation of the old
            // behaviour; the port is the old behaviour, imperfections included. That is the point — the goal is
            // bug-compatibility with what shipped for thirty years, not mathematical purity.
            Assert.AreEqual(-0.989273518318904m, JetDecimalConverter.FromDouble(-0.9892735183189034));
        }

        [TestMethod]
        public void Agrees_with_the_G15_round_trip_to_within_the_last_significant_digit()
        {
            // Fixed seed: a failure has to be reproducible to be worth anything.
            var random = new Random(20260814);

            for (var i = 0; i < 20000; i++)
            {
                // Spread over magnitudes a Jet/ACE column plausibly holds, both signs. Northwind is entirely
                // positive, so negatives have to be generated deliberately or sign bugs hide.
                var scale = Math.Pow(10, random.Next(-6, 7));
                var value = (random.NextDouble() - 0.5) * 2 * scale;

                var ported = JetDecimalConverter.FromDouble(value);
                var reference = G15RoundTrip(value);

                if (ported == 0m)
                {
                    Assert.AreEqual(0m, reference, $"for {value:R}");
                    continue;
                }

                // Both round at 15 significant digits, so they may differ by one unit in that digit (see
                // Reproduces_the_pre_net11_runtime_where_the_G15_round_trip_does_not) but never by more. A wider
                // gap would mean the port has drifted from 15-digit rounding altogether. One unit in the 15th
                // significant digit depends on the magnitude's exponent, so it is computed rather than assumed.
                // Built by decimal division rather than Math.Pow, whose result for e.g. 10^-11 is not exactly
                // 1e-11 as a double and lands the tolerance a hair under the real one-digit gap.
                var exponent = (int)Math.Floor(Math.Log10((double)Math.Abs(ported)));
                var oneUnitInLastDigit = 1m;
                for (var p = 0; p < 14 - exponent; p++)
                {
                    oneUnitInLastDigit /= 10m;
                }

                Assert.IsTrue(
                    Math.Abs(ported - reference) <= oneUnitInLastDigit,
                    $"for {value:R}: ported {ported}, reference {reference}");
            }
        }

        [TestMethod]
        public void Rounds_a_float_at_seven_significant_digits()
        {
            // float carries ~7 digits, so the old VarDecFromR4 rounded there rather than at 15. Widening first and
            // rounding at 15 would surface the single's own error (0.1f is 0.100000001490116... as a double).
            Assert.AreEqual(0.1m, JetDecimalConverter.FromSingle(0.1f));
            Assert.AreEqual(1.5m, JetDecimalConverter.FromSingle(1.5f));
            Assert.AreEqual(-1.234m, JetDecimalConverter.FromSingle(-1.234f));
            Assert.AreEqual(0.3333333m, JetDecimalConverter.FromSingle(1f / 3f));
        }

        [TestMethod]
        public void Keeps_the_original_boundary_behaviour()
        {
            // Below 2^-94 the old algorithm flushed to zero rather than throwing.
            Assert.AreEqual(0m, JetDecimalConverter.FromDouble(1e-30));
            Assert.AreEqual(0m, JetDecimalConverter.FromDouble(0d));

            // Above decimal's range it overflowed, and still must.
            Assert.ThrowsExactly<OverflowException>(() => JetDecimalConverter.FromDouble(1e30));
            Assert.ThrowsExactly<OverflowException>(() => JetDecimalConverter.FromDouble(double.MaxValue));
        }
    }
}
