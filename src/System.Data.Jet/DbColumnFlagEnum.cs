using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Data.Jet
{
    [Flags]
    enum DbColumnFlag
    {
        None = 0,
        IsBookMark = 1,
        MayDefer = 2,
        Write = 4,
        WriteUnknown = 8,
        IsFixedLength = 16,
        IsNullable = 32,
        MayBeNull = 64,
        IsLong = 128,
        IsRowId = 256,
        IsRowVer = 512,
        CachedDeferred = 4096
    }
}
