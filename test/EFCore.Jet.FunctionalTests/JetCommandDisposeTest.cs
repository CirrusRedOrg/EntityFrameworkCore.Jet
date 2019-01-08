using System.Data.Jet;
using System.Data.SqlClient;
using Xunit;

namespace EntityFramework.Jet.FunctionalTests
{
    public class JetCommandDisposeTest
    {
        [Fact]
        public void JetCommandCannotBeLoggedAfterDispose()
        {
            var command = new JetCommand();

            command.CommandText = "foo";
            command.Parameters.Add(command.CreateParameter());
            command.Dispose();

            Assert.Equal(command.CommandText, string.Empty);
            Assert.Equal(command.Parameters.Count, 0);
        }

        [Fact]
        public void SqlCommandCanBeLoggedAfterDispose()
        {
            var command = new SqlCommand();

            command.CommandText = "bar";
            command.Parameters.Add(new SqlParameter());
            command.Dispose();

            Assert.Equal(command.CommandText, "bar");
            Assert.Equal(command.Parameters.Count, 1);
        }
    }
}
