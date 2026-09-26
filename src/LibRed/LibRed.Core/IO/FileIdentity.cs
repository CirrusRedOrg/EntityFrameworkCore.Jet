namespace LibRed.IO;

/// <summary>
/// The key that decides whether two opens name the <b>same file</b>, for the per-file registries every channel
/// on a database shares — the page cache and the lock manager. Two opens of one file must land on one entry
/// (two pools for one file reintroduce cross-handle staleness); two opens of <i>different</i> files must not
/// (one file's pages would be written into the other's).
/// </summary>
/// <remarks>
/// <para>The key is the full path, folded to lower case only where the platform's file names are
/// case-insensitive. Folding unconditionally is what would make <c>Test.accdb</c> and <c>test.accdb</c> — two
/// files on Linux — share one cache. Working it out touches no file, which matters because every open and close
/// pays for it.</para>
/// <para>One file reached by two different paths gets two entries — the staleness case rather than the
/// cross-contamination one: a symbolic link to the file or to a directory above it, a bind mount, a <c>subst</c>
/// drive, a UNC path to a local share, a hard link, and a case-sensitive volume on a platform whose default is
/// insensitive. Only the operating system's own file identity (device plus inode, volume serial plus file index)
/// sees through all of them, and that needs per-platform interop this assembly does not carry. Resolving only a
/// link to the file itself was tried and dropped: it cost a file-system call on every open and close to catch
/// the least common of these. ACE itself opens the path it is given and relies on the operating system's locks,
/// which follow the file whatever path reached it — the route to take when LibRed locks through the OS too.</para>
/// </remarks>
internal static class FileIdentity
{
    private static readonly bool CaseInsensitiveNames = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static string Key(string path)
    {
        string full = Path.GetFullPath(path);
        return CaseInsensitiveNames ? full.ToLowerInvariant() : full;
    }
}
