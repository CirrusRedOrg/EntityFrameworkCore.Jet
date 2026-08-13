namespace LibRed;

/// <summary>
/// Raised when a write would violate a constraint the engine enforces — currently a unique or primary
/// key index, on both the insert and update paths. The ADO layer translates it into a
/// <c>LibRedException</c> (a <see cref="System.Data.Common.DbException"/>) at the provider boundary,
/// which is what ADO.NET callers expect for a database-operation error.
///
/// The distinct type matters because provider code has to recognise a duplicate-key failure to
/// implement things like EF Core's migration lock, where losing the race to INSERT the lock row is the
/// normal path and must be retried rather than propagated. Matching on message text is the alternative,
/// and it is wrong: the wording differs between LibRed and ACE.
///
/// It derives from <see cref="InvalidOperationException"/> — what the engine threw before this type
/// existed — so <c>catch (InvalidOperationException)</c> still works. Note that this does NOT keep
/// existing <c>Assert.Throws&lt;InvalidOperationException&gt;</c> assertions passing: xUnit's
/// <c>Assert.Throws</c> requires an exact type match, and <c>Assert.ThrowsAny</c> is the one that
/// accepts derived types. Tests asserting on this path name this type directly.
/// </summary>
public sealed class ConstraintViolationException(string message, string constraintName, bool isPrimaryKey)
    : InvalidOperationException(message)
{
    /// <summary>The index whose uniqueness was violated.</summary>
    public string ConstraintName { get; } = constraintName;

    /// <summary>Whether that index is the table's primary key rather than a plain unique index.</summary>
    public bool IsPrimaryKey { get; } = isPrimaryKey;
}
