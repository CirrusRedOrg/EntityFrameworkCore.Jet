using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model39_DetachedEntities
{
    public class MyContext : DbContext
    {
        public MyContext(DbContextOptions options) : base(options)
        {}

        public DbSet<Grade> Grades { get; set; }
        public DbSet<GradeWidth> GradeWidths { get; set; }
    }
}
