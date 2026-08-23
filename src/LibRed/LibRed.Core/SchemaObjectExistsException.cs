namespace LibRed;

/// <summary>
/// Raised when DDL would create a schema object whose name is already taken — CREATE TABLE, CREATE VIEW,
/// SELECT ... INTO, and ALTER TABLE ... RENAME all reject a duplicate rather than writing a second catalog
/// row that shadows the existing object. The ADO layer translates it into a <c>LibRedException</c> (a
/// <see cref="System.Data.Common.DbException"/>) at the provider boundary, which is what ADO.NET callers
/// expect for a database-operation error.
///
/// The distinct type matters for the same reason <see cref="ConstraintViolationException"/>'s does: provider
/// code has to recognise this failure without reading message text. EF Core's migration lock creates its
/// lock table under a racy exists-then-create guard and relies on catching the loser's "already exists" —
/// but it catches <c>DbException</c>, so an untranslated <see cref="InvalidOperationException"/> escapes the
/// guard entirely and fails the migration. ACE raises an <c>OleDbException</c> there, so this is a case
/// where matching the ADO.NET contract is what makes LibRed behave like the engine it replaces.
///
/// It derives from <see cref="InvalidOperationException"/> — what the engine threw before this type
/// existed — so <c>catch (InvalidOperationException)</c> still works. Note that this does NOT keep
/// existing <c>Assert.Throws&lt;InvalidOperationException&gt;</c> assertions passing: xUnit's
/// <c>Assert.Throws</c> requires an exact type match, and <c>Assert.ThrowsAny</c> is the one that
/// accepts derived types. Tests asserting on this path name this type directly.
/// </summary>
public sealed class SchemaObjectExistsException(string message, string objectName)
    : InvalidOperationException(message)
{
    /// <summary>The name that was already taken.</summary>
    public string ObjectName { get; } = objectName;
}
