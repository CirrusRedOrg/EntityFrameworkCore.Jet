using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model02
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<TableWithSeveralFieldsType> TableWithSeveralFieldsTypes { get; set; }
    }
}
