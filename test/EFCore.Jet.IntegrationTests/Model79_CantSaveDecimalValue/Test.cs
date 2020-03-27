using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model79_CantSaveDecimalValue
{
    public abstract class Test
    {

        protected abstract DbConnection GetConnection();


        [TestMethod]
        public void Model79_CantSaveDecimalValue()
        {

            using (DbConnection connection = GetConnection())
            {
                using (var context = new Context(connection))
                {
                    var t = new Table();
                    context.Table.Add(t);
                    t.DecimalValue = 1.23M;
                    context.SaveChanges();
                }
            }


        }
    }
}
