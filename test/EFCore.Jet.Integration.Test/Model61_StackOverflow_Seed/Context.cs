using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

// https://stackoverflow.com/questions/44972714/ef-seed-table-with-foreign-key

namespace EFCore.Jet.Integration.Test.Model61_StackOverflow_Seed
{
    public class Context : DbContext
    {
        public Context()
        { }


        public Context(DbConnection connection)
            : base(new DbContextOptionsBuilder<Context>().UseJet(connection).Options)
        { }

        public DbSet<ClassRoom> ClassRooms { get; set; }
        public DbSet<ClassSchedule> ClassSchedules { get; set; }

    }

}
