using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EFCore.Jet.Integration.Test.Model76_FullCreate
{
    class Context : DbContext
    {
        public Context(DbConnection connection) :
            base(TestBase<Context>.GetContextOptions(connection))
        {
            this.Database.Migrate();
        }

        public DbSet<MyEntity> MyEntities { get; set; }

    }
}
