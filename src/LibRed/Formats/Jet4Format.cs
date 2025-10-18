using System;
using System.Collections.Generic;
using System.Text;

namespace LibRed.Formats
{
    internal class Jet4Format : JetFormatBase
    {
        public Jet4Format()
        {
            PageSize = 4096; // Default page size for Jet 4.0
        }
    }
}
