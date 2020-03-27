using System;
using System.Collections.ObjectModel;

namespace EntityFrameworkCore.Jet.IntegrationTests.Model07
{
    public class EntityA
    {
        public int Id { get; set; }
        public Collection<EntityB> EntityBCollection { get; set; }
    }

    public class EntityA_Child
    {
        public int Id { get; set; }

        public EntityA EntityA { get; set; }

        public EntityC EntityC { get; set; }
    }

    public class EntityB
    {
        public int Id { get; set; }
        public Collection<EntityC> EntityCCollection { get; set; }
    }

    public class EntityC
    {
        public int Id { get; set; }
    }
}
