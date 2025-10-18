using System;
using System.Collections.Generic;
using System.Text;

namespace LibRed.Formats
{
    internal abstract class JetFormatBase
    {
        // Database definition page constants
        public int PageSize { get; protected set; } = 2048; // Default page size for Jet formats
        public int VersionOffset { get; protected set; } = 0x14;
    }
}
