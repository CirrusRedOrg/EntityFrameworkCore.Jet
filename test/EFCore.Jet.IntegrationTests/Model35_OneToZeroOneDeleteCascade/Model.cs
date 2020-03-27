using System;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model35_OneToZeroOneDeleteCascade
{
    public class Principal
    {
        public int Id { get; private set; }

        public virtual Dependent Dependent { get; private set; }
    }

    public class Dependent
    {
        public int Id { get; private set; }

        public virtual Principal Principal { get; private set; }
    }
}
