using System;
using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.Model62_InnerQueryBug_DbInitializerSeed
{
    // Seed and initializer are not supported
    /*
    class DbInitializer : CreateDatabaseIfNotExists<Context>
    {
        protected sealed override void Seed(Context context)
        {
            SampleData.Seed(context);
        }
    }
    */

    static class SampleData
    {
        public static void Seed(Context context)
        {
            #region Item
            var items = new List<Item>()
            {
                new Item()
                {
                    Name = "item1"
                },
                new Item()
                {
                    Name = "item2"
                },
                new Item()
                {
                    Name = "item3"
                },
                new Item()
                {
                    Name = "service1", IsService = true
                }
            };

            foreach (var i in items)
            {
                context.Items.Add(i);
            }

            context.SaveChanges();

            #endregion

            #region Sale

            var sales = new List<Sale>()
            {
                new Sale()
                {
                    SaleNo = "1",
                    SaleDetails = new List<SaleDetail>()
                    {
                        new SaleDetail(){ItemId = 1, Rate = 21}
                    }
                },

                new Sale()
                {
                    SaleNo = "2",
                    SaleDetails = new List<SaleDetail>()
                    {
                        new SaleDetail(){ItemId = 2, Rate = 15}
                    }
                },
                new Sale()
                {
                    SaleNo = "3",
                    SaleDetails = new List<SaleDetail>()
                    {
                        new SaleDetail(){ItemId = 4, Rate = 500}
                    }
                }
            };

            foreach (var s in sales)
            {
                context.Sales.Add(s);
            }

            context.SaveChanges();

            #endregion
        }

    }

}
