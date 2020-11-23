using System;
using System.Linq;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model04
{
    public static class SeedHelper
    {
        public static void SeedPersons(CarsContext Context)
        {

            if (Context.Persons.Count() != 0)
                return;

            for (int i = 0; i < 10; i++)
            {
                Context.Persons.Add(new Person()
                {
                    Name = "PersonName " + (10 - i)
                }
                    );
            }

            Context.SaveChanges();

        }
    }
}
