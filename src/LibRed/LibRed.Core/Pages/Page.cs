using LibRed.Formats;
using LibRed.IO;

namespace LibRed.Pages;

/// <summary>
/// Base class for a typed view over a raw page. Concrete pages declare their
/// <see cref="Type"/> and decode their fields from a <see cref="PageBuffer"/>.
/// </summary>
public abstract class Page
{
    /// <summary>Zero-based page number within the file.</summary>
    public int PageNumber { get; protected set; }

    /// <summary>The page type marker (byte 0 of the page).</summary>
    public abstract PageType Type { get; }

    /// <summary>Decodes this page's fields from the supplied buffer using version-specific offsets.</summary>
    public abstract void Read(PageBuffer buffer, JetFormatBase format);
}
