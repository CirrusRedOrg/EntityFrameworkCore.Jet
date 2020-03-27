using System;
using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model60_StackOverflow_m2n_Person_Book
{
    public abstract class Test
    {
        protected abstract DbConnection GetConnection();

        [TestMethod]
        public void Run()
        {
            using (var context = new Context(GetConnection()))
            {
                Book book = new Book()
                {
                    Author = "Niccolò Ammaniti",
                    Name = "Branchie"
                };

                book.Owners.Add(new Person() { FirstName = "Mastero" });
                book.Owners.Add(new Person() { FirstName = "bubi" });

                context.Books.Add(book);
                context.SaveChanges();

                book = new Book()
                {
                    Author = "Niccolò Ammaniti",
                    Name = "Come Dio comanda"
                };

                Person person = context.People.Single(_ => _.FirstName == "bubi");
                person.OwnedBooks.Add(book);

                context.SaveChanges();

                book.Owners.Remove(person);
                context.SaveChanges();


            }
        }
    }
}
