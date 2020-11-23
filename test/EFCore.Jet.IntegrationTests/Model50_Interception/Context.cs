using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model50_Interception
{
    class Context : DbContext
    {
        public Context(DbConnection connection)
            : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        { }

        static Context()
        {
            DbInterception.Add(new CommandInterceptor());
        }


        public DbSet<Note> Notes { get; set; }
    }
}
