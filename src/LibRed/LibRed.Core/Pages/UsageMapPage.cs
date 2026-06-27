using LibRed.IO;

namespace LibRed.Pages;

/// <summary>
/// A page-usage bitmap page. Usage maps track which pages belong to a table (or are
/// free). They come in two flavours — inline (stored in the TDEF) and reference
/// (a chain of dedicated bitmap pages) — handled by <see cref="Storage.UsageMap"/>.
/// </summary>
public sealed class UsageMapPage : Page
{
    public override PageType Type => PageType.PageUsageBitmap;

    public override void Read(PageBuffer buffer)
    {
        PageNumber = buffer.PageNumber;
        // TODO: decode the bitmap payload.
    }
}
