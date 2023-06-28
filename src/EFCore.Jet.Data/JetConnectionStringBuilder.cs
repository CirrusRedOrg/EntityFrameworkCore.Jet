using System;
using System.Data.Common;

namespace EntityFrameworkCore.Jet.Data
{
    public class JetConnectionStringBuilder : DbConnectionStringBuilder
    {
        private readonly DbConnectionStringBuilder _innerBuilder;

        public JetConnectionStringBuilder() : this(JetConfiguration.DefaultDataAccessProviderType)
        {
        }

        public JetConnectionStringBuilder(DataAccessProviderType providerType)
        {
            ProviderType = providerType;
            _innerBuilder = this;
        }

        internal JetConnectionStringBuilder(DbProviderFactory innerFactory)
        {
            ProviderType = JetConnection.GetDataAccessProviderType(innerFactory);
            _innerBuilder = innerFactory.CreateConnectionStringBuilder() ?? throw new InvalidOperationException($"CreateConnectionStringBuilder() returned null for {innerFactory}");
        }

        public DataAccessProviderType ProviderType { get; }

        public string? Provider
        {
            get => _innerBuilder.GetProvider(ProviderType);
            set
            {
                _innerBuilder.SetProvider(value!, ProviderType);
                ConnectionString = _innerBuilder.ConnectionString;
            }
        }

        public string? DataSource
        {
            get => _innerBuilder.GetDataSource(ProviderType);
            set
            {
                _innerBuilder.SetDataSource(value!, ProviderType);
                ConnectionString = _innerBuilder.ConnectionString;
            }
        }

        public string? UserId
        {
            get => _innerBuilder.GetUserId(ProviderType);
            set
            {
                _innerBuilder.SetUserId(value!, ProviderType);
                ConnectionString = _innerBuilder.ConnectionString;
            }
        }

        public string? Password
        {
            get => _innerBuilder.GetPassword(ProviderType);
            set
            {
                _innerBuilder.SetPassword(value!, ProviderType);
                ConnectionString = _innerBuilder.ConnectionString;
            }
        }

        public string? SystemDatabase
        {
            get => _innerBuilder.GetSystemDatabase(ProviderType);
            set
            {
                _innerBuilder.SetSystemDatabase(value!, ProviderType);
                ConnectionString = _innerBuilder.ConnectionString;
            }
        }

        public string? DatabasePassword
        {
            get => _innerBuilder.GetDatabasePassword(ProviderType);
            set
            {
                _innerBuilder.SetDatabasePassword(value!, ProviderType);
                ConnectionString = _innerBuilder.ConnectionString;
            }
        }
    }
}