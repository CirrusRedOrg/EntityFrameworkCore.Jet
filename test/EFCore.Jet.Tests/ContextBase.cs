using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Jet
{
    public class ContextBase : DbContext
    {
        private IServiceProvider _serviceProvider;
        private string _databaseName;
        private Action<DbCommand> _commandLogger;
        private Action<ModelBuilder> _model;
        private Action<IServiceProvider, DbContextOptionsBuilder> _options;
        private Action<JetDbContextOptionsBuilder> _jetOptions;

        public void Initialize(
            string databaseName,
            Action<DbCommand> commandLogger,
            Action<ModelBuilder> model = null,
            Action<IServiceProvider, DbContextOptionsBuilder> options = null,
            IServiceCollection serviceCollection = null,
            Action<JetDbContextOptionsBuilder> jetOptions = null)
        {
            _serviceProvider = (serviceCollection ?? new ServiceCollection().AddEntityFrameworkJet())
                .BuildServiceProvider();
            
            _databaseName = databaseName;
            _commandLogger = commandLogger;
            _model = model;
            _options = options;
            _jetOptions = jetOptions;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseJet(_databaseName, TestEnvironment.DataAccessProviderFactory, b =>
                {
                    b.ApplyConfiguration();
                    _jetOptions?.Invoke(b);
                })
                .AddInterceptors(new CommandInterceptor(_commandLogger))
                .UseInternalServiceProvider(_serviceProvider);

            if (_model != null)
            {
                var conventionSet = JetConventionSetBuilder.Build();
                var modelBuilder = new ModelBuilder(conventionSet);

                _model.Invoke(modelBuilder);

                var model = modelBuilder.FinalizeModel();
                optionsBuilder.UseModel(model);
            }

            _options?.Invoke(_serviceProvider, optionsBuilder);
        }
        
        private class CommandInterceptor : DbCommandInterceptor
        {
            private readonly Action<DbCommand> _commandLogger;

            public CommandInterceptor(Action<DbCommand> commandLogger)
            {
                _commandLogger = commandLogger;
            }

            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                _commandLogger.Invoke(command);
                return base.ReaderExecuting(command, eventData, result);
            }

            public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
            {
                _commandLogger.Invoke(command);
                return base.ScalarExecuting(command, eventData, result);
            }

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                _commandLogger.Invoke(command);
                return base.NonQueryExecuting(command, eventData, result);
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = new CancellationToken())
            {
                _commandLogger.Invoke(command);
                return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            }

            public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = new CancellationToken())
            {
                _commandLogger.Invoke(command);
                return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
            }

            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = new CancellationToken())
            {
                _commandLogger.Invoke(command);
                return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
            }
        }
    }
}