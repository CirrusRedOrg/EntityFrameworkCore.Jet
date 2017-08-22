using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// ReSharper disable VirtualMemberCallInConstructor

namespace EFCore.Jet.Integration.Test.Model60_StackOverflow_m2n_Person_Book
{

    [Table("Person60")]
    public class Person
    {
        public Person()
        {
            OwnedBooks = new List<Book>();
        }

        [Column("PersonId")]
        public int Id { get; set; }
        [MaxLength(50)]
        public string FirstName { get; set; }
        [MaxLength(50)]
        public string LastName { get; set; }

        public virtual ICollection<Book> OwnedBooks { get; set; }
    }


    public class Book
    {
        public Book()
        {
            Owners = new List<Person>();
        }

        [Column("BookId")]
        public int Id { get; set; }
        [Column("BookName")]
        [MaxLength(50)]
        public string Name { get; set; }
        [MaxLength(50)]
        public string Isbn { get; set; }
        [MaxLength(50)]
        public string Author { get; set; }

        public virtual ICollection<Person> Owners { get; set; }
    }


}
