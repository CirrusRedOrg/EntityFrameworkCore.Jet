// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace EFCore.Jet.Integration.Test.GearOfWar
{
    public abstract class Faction
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public string CapitalName { get; set; }
        public City Capital { get; set; }
    }
}
