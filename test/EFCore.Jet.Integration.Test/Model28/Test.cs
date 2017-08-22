using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model28
{
    public abstract class Test : TestBase<Context>
    {


        [TestMethod]
        public void Run()
        {
            var ad = new Advertisement
            {
                AdImages = new List<AdImage>
                {
                    new AdImage {Image = "MyImage"}
                },

                Message = "MyMessage",
                Title = "MyTitle",
                User = new User()
            };

            Context.Advertisements.Add(ad);
            Context.SaveChanges();
        }
    }
}
