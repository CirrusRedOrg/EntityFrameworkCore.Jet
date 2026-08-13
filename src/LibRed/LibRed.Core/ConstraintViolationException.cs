namespace LibRed;

/// <summary>
/// Raised when a write would violate a constraint the engine enforces — currently a unique or primary
/// key index. It derives from <see cref="InvalidOperationException"/> because that is what the engine
/// threw before this type existed, so existing callers and tests catching that keep working; the ADO
/// layer translates it into a <c>LibRedException</c> (a <see cref="System.Data.Common.DbException"/>)
/// at the provider boundary, which is what ADO.NET callers expect for a database-operation error.
///
/// The distinct type matters because provider code has to recognise a duplicate-key failure to
/// implement things like EF Core's migration lock, where losing the race to INSERT the lock row is the
/// normal path and must be retried rather than propagated. Matching on message text is the alternative,
/// and it is wrong: the wording differs between LibRed and ACE.
/// </summary>
public sealed class ConstraintViolationException(string message, string constraintName, bool isPrimaryKey)
    : InvalidOperationException(message)
{
    /// <summary>The index whose uniqueness was violated.</summary>
    public string ConstraintName { get; } = constraintName;

    /// <summary>Whether that index is the table's primary key rather than a plain unique index.</summary>
    public bool IsPrimaryKey { get; } = isPrimaryKey;
}
