// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntityFrameworkCore.Jet.IntegrationTests.GearOfWar
{
    public class Mission
    {
        public int Id { get; set; }

        public string CodeName { get; set; }

        public DateTimeOffset Timeline { get; set; }

        public virtual ICollection<SquadMission> ParticipatingSquads { get; set; }
    }
}
