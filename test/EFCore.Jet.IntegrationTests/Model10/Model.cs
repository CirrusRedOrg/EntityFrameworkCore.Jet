using System;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model10
{
    public class SomeClass
    {
        public int SomeClassId { get; set; }
        public string Name { get; set; }
        public virtual Behavior Behavior { get; set; }
    }

    public abstract class Behavior
    {
        public int BehaviorId { get; set; }
    }

    public class BehaviorA : Behavior
    {
        public string BehaviorASpecific { get; set; }
    }

    public class BehaviorB : Behavior
    {
        public string BehaviorBSpecific { get; set; }
    }
}
