using System;
using System.Collections.Generic;
using System.Linq;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet
{
    public class TestBase<TContext> : IDisposable
        where TContext : ContextBase, new()
    {
        public TestBase()
        {
            TestStore = JetTestStore.CreateInitialized(StoreName);
        }

        public virtual void Dispose() => TestStore.Dispose();

        public virtual string StoreName => GetType().Name;
        public virtual JetTestStore TestStore { get; }
        public virtual List<string> SqlCommands { get; } = new List<string>();
        public virtual string Sql => string.Join("\n\n", SqlCommands.Select(c => c.Trim('\r', '\n')));

        public virtual TContext CreateContext(
            Action<JetDbContextOptionsBuilder> jetOptions = null,
            Action<IServiceProvider, DbContextOptionsBuilder> options = null,
            Action<ModelBuilder> model = null)
        {
            var context = new TContext();
            
            context.Initialize(
                TestStore.Name,
                command => SqlCommands.Add(command.CommandText),
                model: model,
                options: options,
                jetOptions: jetOptions);

            TestStore.Clean(context);
            
            return context;
        }
    }
}