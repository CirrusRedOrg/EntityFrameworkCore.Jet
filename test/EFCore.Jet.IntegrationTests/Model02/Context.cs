using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model02
{
    public class Context : DbContext
    {

        public Context(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<TableWithSeveralFieldsType> TableWithSeveralFieldsTypes { get; set; }
    }
}
