using System;

namespace EFCore.Jet.Integration.Test.Model36_DetachedScenario
{
    static class Repository
    {
        public static void Update(Context Context, Holder holder)
        {
            var thing = holder.Thing;
            holder.Thing = new Thing() {Id = 2};
            var attachedHolder = Context.Holders.Attach(holder);
            attachedHolder.Entity.Thing = thing;
            Context.Entry(holder).Property("Some").IsModified = true;

            //var manager = ((IObjectContextAdapter)Context).ObjectContext.ObjectStateManager;
            //manager.ChangeRelationshipState(holder, holder.Thing, "Thing", EntityState.Added);
            
            Context.SaveChanges();
        }



    }

}