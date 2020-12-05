using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet
{
    public class TestBase<TContext> : IDisposable
        where TContext : ContextBase, new()
    {
        public TestBase()
        {
            TestStore = JetTestStore.CreateInitialized(nameof(JetMigrationTest));
        }

        public virtual void Dispose() => TestStore.Dispose();

        public virtual JetTestStore TestStore { get; }
        public virtual List<string> SqlCommands { get; } = new List<string>();
        public virtual string Sql => string.Join("\n\n", SqlCommands);

        public virtual TContext CreateContext(Action<ModelBuilder> modelBuilder = null)
        {
            var context = new TContext();
            
            context.Initialize(
                TestStore.Name,
                (command) => SqlCommands.Add(command.CommandText),
                modelBuilder);

            TestStore.Clean(context);
            
            return context;
        }
    }
}