using System;
using System.Data.Common;
using EntityFrameworkCore.Jet.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests
{
    public abstract class TestBase<T>
        where T : DbContext
    {
        protected DbConnection Connection { get; set; }
        protected T Context { get; set; }

        [TestInitialize]
        public virtual void Initialize()
        {
            CreateContext();

            TryDropDatabase(Context);

            TryCreateDatabase(Context);

            bool tablesCreated = TryCreateTables(Context);

            if (tablesCreated)
            {
                try
                {
                    Seed();
                }
                catch (Exception e)
                {
                    Console.WriteLine(
                        "E R R O R - Seed - " + GetType()
                            .Name + " === Dump Begin ==============");
                    Console.WriteLine(e);
                    Console.WriteLine("= Dump End ============================================= ");
                    throw;
                }
            }
        }

        public static bool TryCreateTables(T context)
        {
            try
            {
                GetDatabaseCreatorService(context)
                    .CreateTables();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }

        private static void TryCreateDatabase(T context)
        {
            try
            {
                GetDatabaseCreatorService(context)
                    .Create();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error creating database\r\n{0}", GetFullExceptionStackMessages(ex));
            }
        }

        private static void TryDropDatabase(T context)
        {
            try
            {
                GetDatabaseCreatorService(context)
                    .Delete();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error dropping database\r\n{0}", GetFullExceptionStackMessages(ex));
            }
        }

        private static RelationalDatabaseCreator GetDatabaseCreatorService(T context)
        {
            RelationalDatabaseCreator databaseCreator = (RelationalDatabaseCreator) context.Database.GetService<IDatabaseCreator>();
            return databaseCreator;
        }

        public static IMigrator GetDatabaseMigratorService(T context)
        {
            return context.GetService<IMigrator>();
        }

        private static string GetFullExceptionStackMessages(Exception ex)
        {
            if (ex == null)
                return String.Empty;

            return ex.Message + "\r\n" + GetFullExceptionStackMessages(ex.InnerException);
        }

        public virtual void Seed()
        {
        }

        [TestCleanup]
        public virtual void CleanUp()
        {
            DisposeContext();
            Connection.Dispose();
        }

        protected void DisposeContext()
        {
            Context.Dispose();
            Context = null;
        }

        protected void CreateContext()
        {
            Connection = GetConnection();
            var options = GetContextOptions(Connection);

            ConstructorInfo constructorInfo =
                typeof(T).GetConstructor(new Type[] {typeof(DbContextOptions<T>)}) ??
                typeof(T).GetConstructor(new Type[] {typeof(DbContextOptions)});

            if (constructorInfo == null)
                throw new InvalidOperationException("The Context does not have the expected constructor Context(DbContextOptions)");
            Context = (T) constructorInfo.Invoke(new object[] {options});
        }

        protected virtual DbContextOptions GetContextOptions()
        {
            return GetContextOptions(Connection);
        }

        public static DbContextOptions GetContextOptions(DbConnection dbConnection)
        {
            var optionsBuilder = new DbContextOptionsBuilder<T>().EnableSensitiveDataLogging();

            if (dbConnection is JetConnection)
                return optionsBuilder.UseJet(dbConnection)
                    .Options;
            else
            {
                throw new InvalidOperationException(
                    "Connection type " + dbConnection.GetType()
                        .Name + " not handled");
            }
        }

        protected abstract DbConnection GetConnection();
    }
}