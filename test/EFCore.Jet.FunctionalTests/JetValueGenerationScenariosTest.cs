// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

#nullable disable
// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class JetValueGenerationScenariosTest : JetValueGenerationScenariosTestBase
    {
        protected override string DatabaseName
            => "JetValueGenerationScenariosTest";

        protected override Guid GuidSentinel
            => new();

        protected override int IntSentinel
            => 0;

        protected override uint UIntSentinel
            => 0;

        protected override IntKey IntKeySentinel
            => IntKey.Zero;

        protected override ULongKey ULongKeySentinel
            => ULongKey.Zero;

        protected override int? NullableIntSentinel
            => null;

        protected override string StringSentinel
            => null;

        protected override DateTime DateTimeSentinel
            => new();

        protected override NeedsConverter NeedsConverterSentinel
            => new(0);

        /*protected override GeometryCollection GeometryCollectionSentinel
            => null;*/

        protected override byte[] TimestampSentinel
            => null;
    }
}
