using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model10
{
    public abstract class Test : TestBase<TestContext>
    {
        //[TestMethod]
        public void Run()
        {
            var someClass = new SomeClass() {Name = "A"};
            someClass.Behavior = new BehaviorA() {BehaviorASpecific = "Behavior A"};
            Context.SomeClasses.Add(someClass);

            // Here I have two classes with the state of added which make sense
            var modifiedEntities = Context.ChangeTracker.Entries()
                .Where(entity => entity.State != EntityState.Unchanged).ToList();
            // They save with no problem
            Context.SaveChanges();

            //someClass.Behavior = null;
            //Context.SaveChanges();

            // Now I want to change the behavior and it causes entity to try to remove the behavior and add it again
            someClass.Behavior = new BehaviorB() {BehaviorBSpecific = "Behavior B"};

            // Here it can be seen that we have a behavior A with the state of deleted and 
            // behavior B with the state of added
            modifiedEntities = Context.ChangeTracker.Entries()
                .Where(entity => entity.State != EntityState.Unchanged).ToList();

            foreach (var modifiedEntity in modifiedEntities)
                Console.WriteLine("{0} {1}", modifiedEntity.Entity, modifiedEntity.State);

            // But in reality when entity sends the query to the database it replaces the 
            // remove and insert with an update query (this can be seen in the SQL Profiler) 
            // which causes the discrimenator to remain the same where it should change.
            Context.SaveChanges();

            Behavior b = Context.Behaviors.First();
            someClass.Behavior = b;

            Context.SaveChanges();

            someClass.Behavior = new BehaviorA() {BehaviorASpecific = "Behavior C"};

            Context.SaveChanges();
        }
    }
}
