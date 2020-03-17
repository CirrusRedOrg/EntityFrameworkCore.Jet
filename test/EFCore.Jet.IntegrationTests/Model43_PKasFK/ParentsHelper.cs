using System;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model43_PKasFK
{
    static class ParentsHelper
    {
        public static void AddOrUpdate(this Context Context, string name, Parent parent)
        {
            var dbParent = Context.Parents.Find(name);
            if (dbParent == null)
                Context.Parents.Add(parent);
            else
                throw new NotImplementedException();

            Context.SaveChanges();

        }
    }
}
