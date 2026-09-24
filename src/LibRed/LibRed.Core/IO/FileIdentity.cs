namespace LibRed.IO;

/// <summary>
/// The key that decides whether two opens name the <b>same file</b>, for the per-file registries every channel
/// on a database shares — the page cache and the lock manager. Two opens of one file must land on one entry
/// (two pools for one file reintroduce cross-handle staleness); two opens of <i>different</i> files must not
/// (one file's pages would be written into the other's).
/// </summary>
/// <remarks>
/// <para>The key is the full path, with a symbolic link resolved to its target, folded to lower case only where
/// the platform's file names are case-insensitive. Folding unconditionally is what makes <c>Test.accdb</c> and
/// <c>test.accdb</c> — two files on Linux — share one cache.</para>
/// <para>Two cases are knowingly not covered, both needing the operating system's own file identity (volume
/// serial plus file index, device plus inode) and so a per-platform interop this assembly does not carry: a
/// case-sensitive volume on a platform whose default is insensitive (macOS can be formatted that way), and one
/// file reached by two different paths that are not a symbolic link — a <c>subst</c> drive, a UNC path to a
/// local share, a hard link. Each gives one file two entries, which is the staleness case rather than the
/// cross-contamination one.</para>
/// </remarks>
internal static class FileIdentity
{
    private static readonly bool CaseInsensitiveNames = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static string Key(string path)
    {
        string full = Path.GetFullPath(path);
        try
        {
            if (File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target)
                full = Path.GetFullPath(target.FullName);
        }
        catch (IOException)
        {
            // A broken link, or one whose chain cannot be followed: the path as given is the best key there is,
            // and a registry lookup is no place to fail an open.
        }
        catch (UnauthorizedAccessException)
        {
        }
        return CaseInsensitiveNames ? full.ToLowerInvariant() : full;
    }
}
