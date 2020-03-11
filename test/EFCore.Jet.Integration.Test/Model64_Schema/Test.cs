using System;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model64_Schema
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void Run()
        {
            {
                Context.Items.AddRange(
                    new Item
                    {
                        Description = "Description1"
                    },
                    new Item
                    {
                        Description = "Description2"
                    },
                    new Item
                    {
                        Description = "Description3"
                    },
                    new Item
                    {
                        Description = "Description4"
                    });
                Context.SaveChanges();
            }

            base.DisposeContext();
            base.CreateContext();

            {
                var connection = GetConnection();
                connection.Open();
                DbCommand command = connection.CreateCommand();
                command.CommandText = "Select * from TableWithSchema";
                command.ExecuteReader()
                    .Dispose();
            }
        }
    }
}