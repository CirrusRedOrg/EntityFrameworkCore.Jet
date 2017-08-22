using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model11
{
    public abstract class Test
    {
        [TestMethod]
        public void Run()
        {
            using (DbConnection connection = GetConnection())
            using (TestContext db = new TestContext(connection))
            {
                var modelList = new List<Model>
                {
                    new Model {Code = "001-230", Description = "Model 230"},
                    new Model {Code = "001-231", Description = "Model 231"},
                    new Model {Code = "001-232", Description = "Model 232"},
                    new Model {Code = "001-233", Description = "Model 233"},
                    // and many more
                };

                var versions = new List<Version>
                {
                    new Version
                    {
                        VersionNumber = 2.1F,
                        Description = "Version 2.1 for Model Group A",
                        EffectiveDate = new DateTime(1995, 1, 15),
                        Models = new List<Model>
                        {
                            modelList.Find(m => m.Code == "001-230"),
                            modelList.Find(m => m.Code == "001-231"),
                            modelList.Find(m => m.Code == "001-232"),
                            modelList.Find(m => m.Code == "001-233"),
                        },
                        Status = "Draft"
                    },
                    new Version
                    {
                        VersionNumber = 2.2F,
                        Description = "Version 2.2 for Model Group A",
                        EffectiveDate = new DateTime(1995, 7, 15),
                        Models = new List<Model>
                        {
                            modelList.Find(m => m.Code == "001-230"),
                            modelList.Find(m => m.Code == "001-231"),
                            modelList.Find(m => m.Code == "001-232"),
                            modelList.Find(m => m.Code == "001-233"),
                        },
                        Status = "Draft"
                    },
                    new Version
                    {
                        VersionNumber = 2.3F,
                        Description = "Version 2.3 for Model Group A",
                        EffectiveDate = new DateTime(1996, 1, 15),
                        Models = new List<Model>
                        {
                            modelList.Find(m => m.Code == "001-230"),
                            modelList.Find(m => m.Code == "001-231"),
                            modelList.Find(m => m.Code == "001-232"),
                            modelList.Find(m => m.Code == "001-233"),
                        },
                        Status = "Draft"
                    }
                };

                db.Versions.AddRange(versions);

                db.SaveChanges();
            }
        }

        protected abstract DbConnection GetConnection();

    }
}
