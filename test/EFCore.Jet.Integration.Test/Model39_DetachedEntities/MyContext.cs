using System;
using Microsoft.EntityFrameworkCore;

namespace EFCore.Jet.Integration.Test.Model39_DetachedEntities
{
    public class MyContext : DbContext
    {
        public MyContext(DbContextOptions options) : base(options)
        {}

        public DbSet<Grade> Grades { get; set; }
        public DbSet<GradeWidth> GradeWidths { get; set; }
    }
}
