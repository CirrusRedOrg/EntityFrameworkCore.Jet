using LibRed.Formats;
using LibRed.IO;

namespace LibRed.Pages;

/// <summary>
/// A B-tree index page — either an intermediate (node) page pointing at child
/// pages, or a leaf page holding index entries that point at rows.
/// </summary>
public sealed class IndexPage : Page
{
    public IndexPage(bool isLeaf) => IsLeaf = isLeaf;

    public bool IsLeaf { get; }

    public override PageType Type => IsLeaf ? PageType.LeafIndexPage : PageType.IntermediateIndexPage;

    public override void Read(PageBuffer buffer, JetFormatBase format)
    {
        PageNumber = buffer.PageNumber;
        // TODO: decode index entries (column-order-preserving encoded keys) and child pointers.
    }
}
