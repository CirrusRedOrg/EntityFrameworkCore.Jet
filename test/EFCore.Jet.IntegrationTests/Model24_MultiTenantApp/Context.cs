using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model24_MultiTenantApp
{
    public class Context : DbContext
    {
        private readonly IDbInterceptor _dbTreeInterceptor;

        public Context(DbConnection connection)
            : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        {
            // NOT THE RIGHT PLACE TO DO THIS!!!
            _dbTreeInterceptor = new TenantCommandTreeInterceptor();
            DbInterception.Add(_dbTreeInterceptor);
        }

        public DbSet<MyEntity> MyEntities { get; set; }

        protected override void Dispose(bool disposing)
        {
            DbInterception.Remove(_dbTreeInterceptor);

            base.Dispose(disposing);
        }
    }
}
