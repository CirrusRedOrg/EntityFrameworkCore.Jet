using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model02
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
