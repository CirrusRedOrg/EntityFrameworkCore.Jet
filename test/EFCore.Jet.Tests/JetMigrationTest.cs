using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Jet
{
    public class JetMigrationTest : IDisposable
    {
        [ConditionalFact]
        public virtual void Create_table_with_HasDefaultValueSql()
        {
            using var context = CreateContext(
                builder =>
                {
                    builder.Entity<Cookie>(
                        entity =>
                        {
                            entity.Property(e => e.BestServedBefore)
                                .HasDefaultValueSql("#2021-12-31#");

                            entity.HasData(
                                new Cookie
                                {
                                    CookieId = 1,
                                    Name = "Basic",
                                });
                        });
                });
            
            var cookies = context.Set<Cookie>()
                .ToList();
            
            Assert.Single(cookies);
            Assert.Equal(new DateTime(2021, 12, 31), cookies[0].BestServedBefore);
        }

        public class Cookie
        {
            public int CookieId { get; set; }
            public string Name { get; set; }
            public DateTime BestServedBefore { get; set; }
        }

        private class Context : DbContext
        {
            private readonly IServiceProvider _serviceProvider;
            private readonly string _databaseName;
            private readonly Action<ModelBuilder> _modelBuilder;
            private readonly Action<IServiceProvider, DbContextOptionsBuilder> _options;

            public List<string> SqlCommands { get; } = new List<string>();

            public Context(string databaseName, Action<ModelBuilder> modelBuilder = null, Action<IServiceProvider, DbContextOptionsBuilder> options = null, IServiceCollection serviceCollection = null)
            {
                _serviceProvider = (serviceCollection ?? new ServiceCollection().AddEntityFrameworkJet())
                    .BuildServiceProvider();
                _databaseName = databaseName;
                _modelBuilder = modelBuilder;
                _options = options;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder
                    .UseJet(_databaseName, DataAccessProviderType.OleDb /*TestEnvironment.DataAccessProviderFactory*/, b => b.ApplyConfiguration())
                    .AddInterceptors(new CommandInterceptor())
                    .UseInternalServiceProvider(_serviceProvider);

                if (_modelBuilder != null)
                {
                    var conventionSet = JetConventionSetBuilder.Build();
                    var modelBuilder = new ModelBuilder(conventionSet);

                    _modelBuilder.Invoke(modelBuilder);
                    
                    var model = modelBuilder.FinalizeModel();
                    optionsBuilder.UseModel(model);
                }

                _options?.Invoke(_serviceProvider, optionsBuilder);
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
            }

            private class CommandInterceptor : DbCommandInterceptor
            {
                public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
                {
                    ((Context)eventData.Context).SqlCommands.Add(command.CommandText);
                    return base.ReaderExecuting(command, eventData, result);
                }

                public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
                {
                    ((Context)eventData.Context).SqlCommands.Add(command.CommandText);
                    return base.ScalarExecuting(command, eventData, result);
                }

                public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
                {
                    ((Context)eventData.Context).SqlCommands.Add(command.CommandText);
                    return base.NonQueryExecuting(command, eventData, result);
                }

                public override Task<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = new CancellationToken())
                {
                    ((Context)eventData.Context).SqlCommands.Add(command.CommandText);
                    return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
                }

                public override Task<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = new CancellationToken())
                {
                    ((Context)eventData.Context).SqlCommands.Add(command.CommandText);
                    return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
                }

                public override Task<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = new CancellationToken())
                {
                    ((Context)eventData.Context).SqlCommands.Add(command.CommandText);
                    return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
                }
            }
        }
        
        public JetMigrationTest()
        {
            TestStore = JetTestStore.CreateInitialized(nameof(JetMigrationTest));
        }

        protected JetTestStore TestStore { get; }
        public virtual void Dispose() => TestStore.Dispose();

        private Context CreateContext(Action<ModelBuilder> modelBuilder)
        {
            var context = new Context(TestStore.Name, modelBuilder);

            TestStore.Clean(context);
            
            return context;
        }
    }
}