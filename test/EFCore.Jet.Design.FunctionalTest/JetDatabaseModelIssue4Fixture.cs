using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EntityFramework.Jet.FunctionalTests;
using EntityFrameworkCore.Jet.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Jet.Design.FunctionalTests
{
    public class JetDatabaseModelIssue4Fixture : JetDatabaseModelFixture
    {

        private JetTestStore _TestStore;


        public override JetTestStore TestStore
        {
            get
            {
                if (_TestStore == null)
                    _TestStore = JetTestStore.Create("Issue_4.mdb");
                return _TestStore;
            }
        }
    }
}