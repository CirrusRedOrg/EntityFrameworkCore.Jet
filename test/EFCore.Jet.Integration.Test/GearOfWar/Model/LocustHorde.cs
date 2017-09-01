// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.GearOfWar
{
    public class LocustHorde : Faction
    {
        public LocustCommander Commander { get; set; }
        public List<LocustLeader> Leaders { get; set; }

        public string CommanderName { get; set; }
        public bool? Eradicated { get; set; }
    }
}
