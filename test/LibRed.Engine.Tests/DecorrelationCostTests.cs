using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

// The gate is a time-budget policy, but its contract does not require a wall clock. A fake timestamp keeps these
// tests deterministic under coverage, profiling, emulation and heavily loaded CI machines.
public class DecorrelationCostTests
{
    [Fact]
    public void Work_below_the_budget_stays_per_row()
    {
        long now = 0;
        var gate = new DecorrelationGate(budget: 100, timestamp: () => now);

        for (var i = 0; i < 9; i++)
        {
            long started = now;
            now += 10;
            gate.Charge(started);
            Assert.False(gate.Ready);
        }
    }

    [Fact]
    public void Crossing_the_budget_switches_at_the_boundary_and_stays_ready()
    {
        long now = 1_000;
        var gate = new DecorrelationGate(budget: 100, timestamp: () => now);

        long first = now;
        now += 60;
        gate.Charge(first);
        Assert.False(gate.Ready);

        long second = now;
        now += 40;
        gate.Charge(second);
        Assert.True(gate.Ready);

        long third = now;
        now += 1;
        gate.Charge(third);
        Assert.True(gate.Ready);
    }

    [Fact]
    public void Independent_gates_do_not_share_charges()
    {
        long now = 0;
        var first = new DecorrelationGate(budget: 10, timestamp: () => now);
        var second = new DecorrelationGate(budget: 10, timestamp: () => now);

        now = 10;
        first.Charge(0);

        Assert.True(first.Ready);
        Assert.False(second.Ready);
    }

    [Fact]
    public void Non_advancing_or_regressing_clock_does_not_reduce_or_inflate_the_charge()
    {
        long now = 50;
        var gate = new DecorrelationGate(budget: 10, timestamp: () => now);

        gate.Charge(50);
        now = 40;
        gate.Charge(50);
        Assert.False(gate.Ready);

        now = 60;
        gate.Charge(50);
        Assert.True(gate.Ready);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Budget_must_be_positive(long budget)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DecorrelationGate(budget, () => 0));
}
