using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model57_StackOverflow
{
    public abstract class Test : TestBase<Context>
    {

        [TestMethod]
        public void Model57_StackOverflowRun()
        {
            CreatePages();

            UpdateBookNumber();
        }

        private void CreatePages()
        {
            base.DisposeContext();
            base.CreateContext();
            for (int i = 0; i < 50; i++)
                Context.Pages.Add(new Page() { BookNumber = i, PageNumber = 100 });

            Context.SaveChanges();
        }

        private void UpdateBookNumber()
        {
            base.DisposeContext();
            base.CreateContext();
            {
                var pagesList = Context.Pages.OrderBy(x => x.PageNumber).ToList();

                for (int i = 0; i < pagesList.Count; i++)
                    pagesList[i].BookNumber = null;

                Context.SaveChanges();

                for (int i = 0; i < pagesList.Count; i++)
                    pagesList[i].BookNumber = i + 30;

                foreach (var blah2 in pagesList.OrderBy(x => x.Id))
                    Console.WriteLine("{{ID:{0}, BN:{1}, PN:{2}}}", blah2.Id, blah2.BookNumber, blah2.PageNumber);

                Context.SaveChanges();
            }
        }
    }
}
