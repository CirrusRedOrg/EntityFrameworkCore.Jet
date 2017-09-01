// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace EFCore.Jet.Integration.Test.GearOfWar
{
    public class Officer : Gear
    {
        public Officer()
        {
            Reports = new List<Gear>();
        }

        // 1 - many self reference
        public virtual ICollection<Gear> Reports { get; set; }
    }
}
