using System;
using Microsoft.EntityFrameworkCore;

// https://stackoverflow.com/questions/44923674/ef-code-first-multi-level-inheritence-issues

namespace EntityFrameworkCore.Jet.IntegrationTests.Model59_StackOverflow_TPT_TPH
{
    public class Context : DbContext
    {
        public Context()
        { }


        public Context(DbContextOptions options) : base (options)
        { }

        public DbSet<Activity> Activity { get; set; }   // To have a table named Activity as required
        public DbSet<DataCaptureActivity> DataCaptureActivities { get; set; }
        public DbSet<MasterDataCaptureActivity> MasterDataCaptureActivities { get; set; }

    }

}
