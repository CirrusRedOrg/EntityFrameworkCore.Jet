using System;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model39_DetachedEntities
{
    public class MyContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Grade> Grades { get; set; }
        public DbSet<GradeWidth> GradeWidths { get; set; }
    }
}
