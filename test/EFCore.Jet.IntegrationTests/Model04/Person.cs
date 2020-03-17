using System;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model04
{
    public class Person
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public virtual Car OwnedCar { get; set; }
    }
}
