using System;
using System.Data.Common;
using System.Reflection;

namespace EntityFrameworkCore.Jet.Data
{
    /// <summary>
    /// Jet provider factory
    /// </summary>
    public class JetFactory : DbProviderFactory
    {
        public static readonly Version MinimumRequiredOdbcVersion = new Version(8, 0, 0);
        public static readonly Version MinimumRequiredOleDbVersion = new Version(8, 0, 0);

        public static readonly JetFactory Instance = new JetFactory(null, null);

        public JetConnection? Connection { get; }

        internal DbProviderFactory? InnerFactory { get; }

        internal JetFactory(JetConnection? connection, DbProviderFactory? innerFactory)
        {
            if (innerFactory is JetFactory)
                throw new ArgumentException("JetProviderFactory cannot use a JetProviderFactory as its underlying provider factory. Supported provider factories are OdbcFactory and OleDbFactory.");

            Connection = connection;
            InnerFactory = innerFactory;
        }

        /// <summary>
        /// Specifies whether the specific <see cref="T:System.Data.Common.DbProviderFactory" /> supports the <see cref="T:System.Data.Common.DbDataSourceEnumerator" /> class.
        /// </summary>
        public override bool CanCreateDataSourceEnumerator
            => false;

        /// <summary>
        /// Returns a new instance of the provider's class that implements the <see cref="T:System.Data.Common.DbCommand" /> class.
        /// </summary>
        /// <returns>
        /// A new instance of <see cref="T:System.Data.Common.DbCommand" />.
        /// </returns>
        public override DbCommand CreateCommand()
            => InnerFactory == null
                ? throw new InvalidOperationException(Messages.CannotCallJetProviderFactoryMethodOnSingletonInstance(nameof(CreateCommand)))
                : new JetCommand(Connection);

        /// <summary>
        /// Returns a new instance of the provider's class that implements the <see cref="T:System.Data.Common.DbCommandBuilder" /> class.
        /// </summary>
        /// <returns>
        /// A new instance of <see cref="T:System.Data.Common.DbCommandBuilder" />.
        /// </returns>
        public override DbCommandBuilder CreateCommandBuilder()
        {
            if (InnerFactory == null)
                throw new InvalidOperationException(Messages.CannotCallJetProviderFactoryMethodOnSingletonInstance(nameof(CreateCommandBuilder)));

            var commandBuilder = InnerFactory.CreateCommandBuilder();
            commandBuilder.QuotePrefix = "`";
            commandBuilder.QuoteSuffix = "`";

            return commandBuilder;
        }

        /// <summary>
        /// Returns a new instance of the provider's class that implements the <see cref="T:System.Data.Common.DbConnection" /> class.
        /// </summary>
        /// <returns>
        /// A new instance of <see cref="T:System.Data.Common.DbConnection" />.
        /// </returns>
        public override DbConnection CreateConnection()
            => InnerFactory == null
                ? new JetConnection()
                : new JetConnection(InnerFactory);

        /// <summary>
        /// Returns a new instance of the provider's class that implements the <see cref="T:System.Data.Common.DbConnectionStringBuilder" /> class.
        /// </summary>
        /// <returns>
        /// A new instance of <see cref="T:System.Data.Common.DbConnectionStringBuilder" />.
        /// </returns>
        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
            => InnerFactory == null
                ? throw new InvalidOperationException(Messages.CannotCallJetProviderFactoryMethodOnSingletonInstance(nameof(CreateConnectionStringBuilder)))
                : new JetConnectionStringBuilder(InnerFactory);

        /// <summary>
        /// Returns a new instance of the provider's class that implements the <see cref="T:System.Data.Common.DbDataAdapter" /> class.
        /// </summary>
        /// <returns>
        /// A new instance of <see cref="T:System.Data.Common.DbDataAdapter" />.
        /// </returns>
        public override DbDataAdapter CreateDataAdapter()
            => InnerFactory == null
                ? throw new InvalidOperationException(Messages.CannotCallJetProviderFactoryMethodOnSingletonInstance(nameof(CreateDataAdapter)))
                : InnerFactory.CreateDataAdapter();

        /// <summary>
        /// Returns a new instance of the provider's class that implements the <see cref="T:System.Data.Common.DbDataSourceEnumerator" /> class.
        /// </summary>
        /// <returns>
        /// A new instance of <see cref="T:System.Data.Common.DbDataSourceEnumerator" />.
        /// </returns>
        public override DbDataSourceEnumerator? CreateDataSourceEnumerator()
            => null;

        /// <summary>
        /// Returns a new instance of the provider's class that implements the <see cref="T:System.Data.Common.DbParameter" /> class.
        /// </summary>
        /// <returns>
        /// A new instance of <see cref="T:System.Data.Common.DbParameter" />.
        /// </returns>
        public override DbParameter CreateParameter()
            => InnerFactory == null
                ? throw new InvalidOperationException(Messages.CannotCallJetProviderFactoryMethodOnSingletonInstance(nameof(CreateDataAdapter)))
                : InnerFactory.CreateParameter();

        public virtual DbProviderFactory GetDataAccessProviderFactory(DataAccessProviderType dataAccessProviderType)
        {
            if (dataAccessProviderType == DataAccessProviderType.OleDb)
            {
                try
                {
                    var type = Type.GetType("System.Data.OleDb.OleDbFactory, System.Data.OleDb");
                    var assemblyName = type.Assembly.GetName();
                    var version = assemblyName.Version;

                    if (version < MinimumRequiredOleDbVersion &&
                        assemblyName.Name != "System.Data") // For .NET Framework, System.Data.OleDb is just a stub that references the .NET Framework implementation.
                    {
                        throw new TypeLoadException($"The referenced version '{version}' of 'System.Data.OleDb' is lower than the minimum required version {MinimumRequiredOleDbVersion}.");
                    }
                    
                    return (DbProviderFactory) type
                        .GetField("Instance", BindingFlags.Static | BindingFlags.Public)
                        .GetValue(null);
                }
                catch (Exception e)
                {
                    throw new TypeLoadException($"To use OLE DB in conjunction with Jet, please reference the 'System.Data.OleDb' (version >= {MinimumRequiredOleDbVersion}) NuGet package.", e);
                }
            }
            else
            {
                try
                {
                    var type = Type.GetType("System.Data.Odbc.OdbcFactory, System.Data.Odbc");
                    var assemblyName = type.Assembly.GetName();
                    var version = assemblyName.Version;

                    if (version < MinimumRequiredOdbcVersion &&
                        assemblyName.Name != "System.Data") // For .NET Framework, System.Data.Odbc is just a stub that references the .NET Framework implementation.
                    {
                        throw new TypeLoadException($"The referenced version '{version}' of 'System.Data.Odbc' is lower than the minimum required version {MinimumRequiredOdbcVersion}.");
                    }
                    
                    return (DbProviderFactory) type
                        .GetField("Instance", BindingFlags.Static | BindingFlags.Public)
                        .GetValue(null);
                }
                catch (Exception e)
                {
                    throw new TypeLoadException($"To use ODBC in conjunction with Jet, please reference the 'System.Data.Odbc' (version >= {MinimumRequiredOdbcVersion}) NuGet package.", e);
                }
            }
        }
    }
}