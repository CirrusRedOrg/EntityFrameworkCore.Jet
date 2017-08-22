using System;
using System.Data.Common;
using System.Data.Jet;
using System.Data.SqlClient;
using System.Data.SqlServerCe;
using System.Reflection;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test
{
    public abstract class TestBase<T> where T : DbContext
    {

        protected DbConnection Connection { get; set; }
        protected T Context { get; set; }

        [TestInitialize]
        public virtual void Initialize()
        {
            CreateContext();

            CreateTables(Context);

            try
            {
                Seed();
            }
            catch (Exception e)
            {
                Console.WriteLine("E R R O R - Seed - " + GetType().Name + " === Dump Begin ==============");
                Console.WriteLine(e);
                Console.WriteLine("= Dump End ============================================= ");
                throw;
            }

        }

        public static void CreateTables(T Context)
        {
            RelationalDatabaseCreator databaseCreator = (RelationalDatabaseCreator)Context.Database.GetService<IDatabaseCreator>();
            try
            {
                databaseCreator.CreateTables();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
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
                typeof(T).GetConstructor(new Type[] { typeof(DbContextOptions<T>) }) ??
                typeof(T).GetConstructor(new Type[] { typeof(DbContextOptions) });

            if (constructorInfo == null)
                throw new InvalidOperationException("The Context does not have the expected constructor Context(DbContextOptions)");
            Context = (T)constructorInfo.Invoke(new object[] { options });
        }


        protected virtual DbContextOptions GetContextOptions()
        {
            return GetContextOptions(Connection);
        }

        public static DbContextOptions GetContextOptions(DbConnection dbConnection)
        {
            if (dbConnection is SqlCeConnection)
                return new DbContextOptionsBuilder<T>().UseSqlCe(dbConnection).Options;
            else if (dbConnection is JetConnection)
                return new DbContextOptionsBuilder<T>().UseJet(dbConnection).Options;
            else if (dbConnection is SqlConnection)
                return new DbContextOptionsBuilder<T>().UseSqlServer(dbConnection).Options;
            else
            {
                throw new InvalidOperationException("Connection type " + dbConnection.GetType().Name + " not handled");
            }
        }

        protected abstract DbConnection GetConnection();

    }
}