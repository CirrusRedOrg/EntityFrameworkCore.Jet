using EntityFrameworkCore.Jet.Query.Internal;
using EntityFrameworkCore.LibRed.Infrastructure;
using EntityFrameworkCore.LibRed.Infrastructure.Internal;

namespace EntityFrameworkCore.LibRed.Query.Internal
{
    /// <summary>
    ///     Creates the SQL translating expression visitor for the configured SQL mode: the shared
    ///     <see cref="JetSqlTranslatingExpressionVisitor" /> in <see cref="LibRedSqlMode.Compatible" /> mode, whose
    ///     SQL ACE also has to run, and LibRed's own <see cref="LibRedSqlTranslatingExpressionVisitor" /> in
    ///     <see cref="LibRedSqlMode.Extended" /> mode.
    /// </summary>
    /// <remarks>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </remarks>
    public class LibRedSqlTranslatingExpressionVisitorFactory(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
        ILibRedOptions options) : IRelationalSqlTranslatingExpressionVisitorFactory
    {
        /// <summary>
        ///     Relational provider-specific dependencies for this service.
        /// </summary>
        protected virtual RelationalSqlTranslatingExpressionVisitorDependencies Dependencies { get; } = dependencies;

        public virtual RelationalSqlTranslatingExpressionVisitor Create(
            QueryCompilationContext queryCompilationContext,
            QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
            => options.SqlMode == LibRedSqlMode.Compatible
                ? new JetSqlTranslatingExpressionVisitor(Dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
                : new LibRedSqlTranslatingExpressionVisitor(Dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor);
    }
}