using System;

namespace EFCore.Jet.Integration.Test.Model44_CaseSensitivity
{
    public abstract class Test : TestBase<Context>
    {
        public virtual void Run()
        {
            Context.Entities.Add(new Entity() { Name = "Duplicated" });
            Context.Entities.Add(new Entity() { Name = "DUPLICATED" });
            Context.SaveChanges();
        }
    }
}
