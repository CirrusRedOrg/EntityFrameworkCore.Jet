using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCore.Jet.Integration.Test.Model71_MasterDetail
{
    public abstract class Test : TestBase<Context>
    {
        [TestMethod]
        public void AttachedScenario()
        {

            int masterId_1;
            int detailsCount;
            Master master;

            detailsCount = Context.Details.Count();

            master = new Master() { Description = "M1" };
            master.Details.Add("M1.D1");
            master.Details.Add("M1.D2");
            master.Details.Add("M1.D3");
            Context.Masters.Add(master);
            Context.SaveChanges();
            masterId_1 = master.Id;

            Assert.AreEqual(detailsCount + 3, Context.Details.Count());

            base.DisposeContext();

            base.CreateContext();

            master = Context.Masters.Find(masterId_1);
            Assert.IsNotNull(master);
            Assert.AreEqual(3, master.Details.Count);

            Assert.AreEqual(detailsCount + 3, Context.Details.Count());



            // Removing a detail using this statement
            // master.Details.RemoveAt(0);
            // This is the exception
            /*
             * The relationship could not be changed because one or 
             * more of the foreign-key properties is non-nullable. 
             * When a change is made to a relationship, the 
             * related foreign-key property is set to a null 
             * value. If the foreign-key does not support null 
             * values, a new relationship must be defined, the 
             * foreign-key property must be assigned another 
             * non-null value, or the unrelated object must be 
             * deleted.
            */

            Context.Entry(master.Details[0]).State = EntityState.Deleted;
            Context.SaveChanges();


            base.DisposeContext();


            base.CreateContext();

            master = Context.Masters.Find(masterId_1);
            Assert.IsNotNull(master);
            Assert.AreEqual(2, master.Details.Count);

            Assert.AreEqual(detailsCount + 2, Context.Details.Count());

        }


        [TestMethod]
        public void DetachedScenario()
        {

            int masterId;
            int detailsCount;
            Master detachedMaster;
            Master master;

            detailsCount = Context.Details.Count();

            master = new Master() { Description = "M1" };
            master.Details.Add("M1.D1");
            master.Details.Add("M1.D2");
            master.Details.Add("M1.D3");
            Context.Masters.Add(master);
            Context.SaveChanges();
            Assert.AreEqual(detailsCount + 3, Context.Details.Count());


            masterId = master.Id;

            detachedMaster = Context.Masters
                .Where(_ => _.Id == masterId)
                .Include(_ => _.Details)
                .AsNoTracking()
                .ToList()[0]
                ;




            detachedMaster.Details.RemoveAt(0);
            detachedMaster.Details[1].Description = "M1.D2 Description Updated";


            base.DisposeContext();

            base.CreateContext();

            // Master must be marked as Modified
            Context.Entry(detachedMaster).State = EntityState.Modified;
            // Every detail must be marked as Added or Modified according to Id
            detachedMaster.Details.ForEach(_ => Context.Entry(_).State = _.Id == 0 ? EntityState.Added : EntityState.Modified);
            // Delete deleted details
            int[] detailIdList = detachedMaster.Details.Select(_ => _.Id).ToArray();
            var deletedDetailList = Context.Details
                .Where(_ => _.Master.Id == detachedMaster.Id)
                .Where(_ => !detailIdList.Contains(_.Id))
                .ToList();
            Context.Details.RemoveRange(deletedDetailList);

            Context.SaveChanges();

            base.DisposeContext();

            base.CreateContext();

            master = Context.Masters.Find(detachedMaster.Id);
            Assert.IsNotNull(master);
            Assert.AreEqual(2, master.Details.Count);
            Assert.AreEqual("M1.D2 Description Updated", master.Details[0].Description);

            Assert.AreEqual(detailsCount + 2, Context.Details.Count());


        }


    }
}