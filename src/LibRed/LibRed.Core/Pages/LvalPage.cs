using LibRed.IO;

namespace LibRed.Pages;

/// <summary>
/// A long-value (LVAL) page storing memo, OLE and other overflow data that does not
/// fit inline in a row. Long values may be stored inline, on a single LVAL page, or
/// chained across many LVAL pages.
/// </summary>
public sealed class LvalPage : Page
{
    public override PageType Type => PageType.DataPage; // LVAL pages reuse the data-page type marker.

    public override void Read(PageBuffer buffer)
    {
        PageNumber = buffer.PageNumber;
        // TODO: decode LVAL chunk header and payload / next-chunk pointer.
    }
}
