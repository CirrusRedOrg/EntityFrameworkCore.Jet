using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using EntityFrameworkCore.Jet;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model26_Decompile
{
    public class ParentEntity
    {
        public ParentEntity()
        {
            Children = [];
        }
        public int Id { get; set; }
        public string Name { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime DateOfBirth { get; set; }
        public virtual List<ChildEntity> Children { get; set; }

        [NotMapped]
        public string FullName
        {
            get { return FirstName + " " + LastName; }
        }
    }

    public class ChildEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime DateOfBirth { get; set; }

        [NotMapped]
        public string FullName
        {
            get { return FirstName + " " + LastName; }
        }

        public int ParentId { get; set; }
        public virtual ParentEntity Parent { get; set; }
    }

    public class ParentChildModel(DbContextOptions options) : DbContext(options)
    {
        public virtual DbSet<ParentEntity> Parents { get; set; }
        public virtual DbSet<ChildEntity> Children { get; set; }
    }
}
