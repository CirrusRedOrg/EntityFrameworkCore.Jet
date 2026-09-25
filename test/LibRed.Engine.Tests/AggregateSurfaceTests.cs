using LibRed.Engine.Execution;
using LibRed.Engine.Planning;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The aggregate surface is answered in three places — <see cref="RunningAggregate"/> computes the value,
/// <see cref="QueryPlanner.IsAggregate"/> decides the name is an aggregate at all, and
/// <see cref="QueryExecutor.AggregateResultType"/> declares the type it comes back as. The first two already
/// derive from <see cref="RunningAggregate"/>; the third is a switch beside it, whose own comment asks to be
/// kept in lock-step. This binds them, so adding an aggregate to one and not the others fails here.
/// </summary>
public class AggregateSurfaceTests
{
    [Fact]
    public void Every_computed_aggregate_is_recognised_by_the_planner()
    {
        foreach (string name in RunningAggregate.SupportedNames)
        {
            Assert.True(RunningAggregate.Supports(name), $"{name} is listed but Supports says otherwise.");
            Assert.True(QueryPlanner.IsAggregate(name), $"{name} computes but the planner does not treat it as an aggregate.");
        }
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(double))]
    [InlineData(typeof(string))]
    public void Every_computed_aggregate_declares_a_result_type(Type argument)
    {
        foreach (string name in RunningAggregate.SupportedNames)
        {
            // Null is what AggregateResultType returns for a name it has no case for — the silent failure
            // this test exists to catch, since such a name still plans and still passes the arity check.
            Assert.True(
                QueryExecutor.AggregateResultType(name, argument) is not null,
                $"{name} over {argument.Name} declares no result type — give it a case in AggregateResultType.");
        }
    }
}
