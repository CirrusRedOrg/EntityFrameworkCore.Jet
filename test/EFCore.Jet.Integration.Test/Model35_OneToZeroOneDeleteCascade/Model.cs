using System;

namespace EFCore.Jet.Integration.Test.Model35_OneToZeroOneDeleteCascade
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
