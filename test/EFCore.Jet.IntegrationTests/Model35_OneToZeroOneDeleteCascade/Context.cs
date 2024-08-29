﻿using System;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model35_OneToZeroOneDeleteCascade
{
    public class Context(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Principal> Principals { get; set; }
        public DbSet<Dependent> Dependents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new PrincipalMap());
            modelBuilder.ApplyConfiguration(new DependentMap());
        }

    }
}
