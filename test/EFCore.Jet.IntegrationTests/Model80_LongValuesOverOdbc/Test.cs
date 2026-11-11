using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model80_LongValuesOverOdbc
{
    // The Access ODBC driver refuses a string parameter longer than 255 characters, and a binary one longer than
    // 510 bytes, with HY104 "Invalid precision value" unless it is bound as a long type (GitHub issue #306).
    public abstract class Test
    {
        // One database per test method: MSTest runs them in parallel.
        protected abstract DbConnection GetConnection(string storeName);

        [TestMethod]
        public void Model80_LongValuesOverOdbc_Insert()
        {
            using var connection = GetConnection("Model80_LongValuesOverOdbc_Insert.accdb");
            using (var context = new Context(connection))
            {
                context.Table.Add(
                    new Table
                    {
                        Text = new string('a', 256),
                        Binary = Enumerable.Range(0, 511).Select(i => (byte)i).ToArray(),
                        Document = new Document { Body = new string('b', 300) }
                    });
                context.SaveChanges();
            }

            using (var context = new Context(connection))
            {
                var row = context.Table.Single();
                Assert.AreEqual(new string('a', 256), row.Text);
                CollectionAssert.AreEqual(Enumerable.Range(0, 511).Select(i => (byte)i).ToArray(), row.Binary);
                Assert.AreEqual(new string('b', 300), row.Document.Body);
            }
        }

        [TestMethod]
        public void Model80_LongValuesOverOdbc_Update()
        {
            using var connection = GetConnection("Model80_LongValuesOverOdbc_Update.accdb");
            using (var context = new Context(connection))
            {
                context.Table.Add(new Table { Text = "short", Binary = [1, 2, 3], Document = new Document { Body = "short" } });
                context.SaveChanges();
            }

            using (var context = new Context(connection))
            {
                var row = context.Table.Single();
                row.Text = new string('a', 300);
                row.Binary = new byte[8001];
                row.Document = new Document { Body = new string('b', 300) };
                context.SaveChanges();
            }

            using (var context = new Context(connection))
            {
                var row = context.Table.Single();
                Assert.AreEqual(new string('a', 300), row.Text);
                Assert.AreEqual(8001, row.Binary.Length);
                Assert.AreEqual(new string('b', 300), row.Document.Body);
            }
        }

        [TestMethod]
        public void Model80_LongValuesOverOdbc_Where()
        {
            using var connection = GetConnection("Model80_LongValuesOverOdbc_Where.accdb");
            using (var context = new Context(connection))
            {
                context.Table.Add(new Table { Text = new string('a', 300), Document = new Document { Body = "x" } });
                context.Table.Add(new Table { Text = "short", Document = new Document { Body = "y" } });
                context.SaveChanges();
            }

            using (var context = new Context(connection))
            {
                var value = new string('a', 300);
                Assert.AreEqual(1, context.Table.Count(x => x.Text == value));
            }
        }
    }
}
