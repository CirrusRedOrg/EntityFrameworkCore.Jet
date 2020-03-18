// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    [Flags]
    public enum JetCondition
    {
        IsNotCI = 1 << 7,
    }
}
