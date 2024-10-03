// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Internal;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Migrations.Operations;
using EntityFrameworkCore.Jet.Storage.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using EntityFrameworkCore.Jet.Update.Internal;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Migrations
{
    /// <summary>
    ///     <para>
    ///         Jet-specific implementation of <see cref="MigrationsSqlGenerator" />.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Scoped" />. This means that each
    ///         <see cref="DbContext" /> instance will use its own instance of this service.
    ///         The implementation may depend on other services registered with any lifetime.
    ///         The implementation does not need to be thread-safe.
    ///     </para>
    /// </summary>
    public class JetMigrationsSqlGenerator : MigrationsSqlGenerator
    {
        private IReadOnlyList<MigrationOperation> _operations = null!;
        private readonly ICommandBatchPreparer _commandBatchPreparer;
        /// <summary>
        ///     Creates a new <see cref="JetMigrationsSqlGenerator" /> instance.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this service. </param>
        /// <param name="commandBatchPreparer">The command batch preparer.</param>
        public JetMigrationsSqlGenerator(
            MigrationsSqlGeneratorDependencies dependencies,
            ICommandBatchPreparer commandBatchPreparer)
            : base(dependencies)
        {
            _commandBatchPreparer = commandBatchPreparer;
        }

        /// <summary>
        ///     Generates commands from a list of operations.
        /// </summary>
        /// <param name="operations"> The operations. </param>
        /// <param name="model"> The target model which may be <see langword="null" /> if the operations exist without a model. </param>
        /// <param name="options"> The options to use when generating commands. </param>
        /// <returns> The list of commands to be executed or scripted. </returns>
        public override IReadOnlyList<MigrationCommand> Generate(
            IReadOnlyList<MigrationOperation> operations,
            IModel? model = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            _operations = operations;
            try
            {
                return base.Generate(operations, model, options);
            }
            finally
            {
                _operations = null!;
            }
        }

        /// <summary>
        ///     <para>
        ///         Builds commands for the given <see cref="MigrationOperation" /> by making calls on the given
        ///         <see cref="MigrationCommandListBuilder" />.
        ///     </para>
        ///     <para>
        ///         This method uses a double-dispatch mechanism to call one of the 'Generate' methods that are
        ///         specific to a certain subtype of <see cref="MigrationOperation" />. Typically database providers
        ///         will override these specific methods rather than this method. However, providers can override
        ///         this methods to handle provider-specific operations.
        ///     </para>
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            switch (operation)
            {
                case JetCreateDatabaseOperation createDatabaseOperation:
                    Generate(createDatabaseOperation, model, builder);
                    break;
                case JetDropDatabaseOperation dropDatabaseOperation:
                    Generate(dropDatabaseOperation, model, builder);
                    break;
                default:
                    base.Generate(operation, model, builder);
                    break;
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected override void Generate(
            AddColumnOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            if (IsIdentity(operation))
            {
                // NB: This gets added to all added non-nullable columns by MigrationsModelDiffer. We need to suppress
                //     it, here because Jet can't have both IDENTITY and a DEFAULT constraint on the same column.
                operation.DefaultValue = null;
            }

            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddForeignKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
        /// <param name="builder">The command builder to use to build the commands.</param>
        /// <param name="terminate">Indicates whether or not to terminate the command after generating SQL for the operation.</param>
        protected override void Generate(
            AddForeignKeyOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AddPrimaryKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
        /// <param name="builder">The command builder to use to build the commands.</param>
        /// <param name="terminate">Indicates whether or not to terminate the command after generating SQL for the operation.</param>
        protected override void Generate(
            AddPrimaryKeyOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AlterColumnOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            AlterColumnOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
            if (operation[RelationalAnnotationNames.ColumnOrder] != operation.OldColumn[RelationalAnnotationNames.ColumnOrder])
            {
                Dependencies.MigrationsLogger.ColumnOrderIgnoredWarning(operation);
            }

            IEnumerable<ITableIndex>? indexesToRebuild = null;
            var column = model?.GetRelationalModel().FindTable(operation.Table, operation.Schema)
                ?.Columns.FirstOrDefault(c => c.Name == operation.Name);

            if (operation.ComputedColumnSql != operation.OldColumn.ComputedColumnSql)
            {
                var dropColumnOperation = new DropColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name
                };
                if (column != null)
                {
                    dropColumnOperation.AddAnnotations(column.GetAnnotations());
                }

                var addColumnOperation = new AddColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name,
                    ClrType = operation.ClrType,
                    ColumnType = operation.ColumnType,
                    IsUnicode = operation.IsUnicode,
                    IsFixedLength = operation.IsFixedLength,
                    MaxLength = operation.MaxLength,
                    Precision = operation.Precision,
                    Scale = operation.Scale,
                    IsRowVersion = operation.IsRowVersion,
                    IsNullable = operation.IsNullable,
                    DefaultValue = operation.DefaultValue,
                    DefaultValueSql = operation.DefaultValueSql,
                    ComputedColumnSql = operation.ComputedColumnSql,
                    IsStored = operation.IsStored,
                    Comment = operation.Comment,
                    Collation = operation.Collation
                };
                addColumnOperation.AddAnnotations(operation.GetAnnotations());

                // TODO: Use a column rebuild instead
                indexesToRebuild = GetIndexesToRebuild(column, operation).ToList();
                DropIndexes(indexesToRebuild, builder);
                Generate(dropColumnOperation, model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                Generate(addColumnOperation, model, builder);
                CreateIndexes(indexesToRebuild, builder);
                builder.EndCommand();

                return;
            }

            var columnType = operation.ColumnType
                ?? GetColumnType(
                    operation.Schema,
                    operation.Table,
                    operation.Name,
                    operation,
                    model);

            var narrowed = false;
            var oldColumnSupported = IsOldColumnSupported(model);

            if (oldColumnSupported)
            {
                if (IsIdentity(operation) != IsIdentity(operation.OldColumn))
                {
                    throw new InvalidOperationException(JetStrings.AlterIdentityColumn);
                }

                var oldType = operation.OldColumn.ColumnType
                    ?? GetColumnType(
                        operation.Schema,
                        operation.Table,
                        operation.Name,
                        operation.OldColumn,
                        model);
                narrowed = columnType != oldType
                    || operation.Collation != operation.OldColumn.Collation
                    || operation is { IsNullable: false, OldColumn.IsNullable: true };
            }

            if (narrowed)
            {
                indexesToRebuild = GetIndexesToRebuild(column, operation).ToList();
                DropIndexes(indexesToRebuild, builder);
            }

            var newAnnotations = operation.GetAnnotations().Where(a => a.Name != JetAnnotationNames.Identity);
            var oldAnnotations = operation.OldColumn.GetAnnotations().Where(a => a.Name != JetAnnotationNames.Identity);

            var alterStatementNeeded = narrowed
                || !oldColumnSupported
                || operation.ClrType != operation.OldColumn.ClrType
                || columnType != operation.OldColumn.ColumnType
                || operation.IsUnicode != operation.OldColumn.IsUnicode
                || operation.IsFixedLength != operation.OldColumn.IsFixedLength
                || operation.MaxLength != operation.OldColumn.MaxLength
                || operation.Precision != operation.OldColumn.Precision
                || operation.Scale != operation.OldColumn.Scale
                || operation.IsRowVersion != operation.OldColumn.IsRowVersion
                || operation.IsNullable != operation.OldColumn.IsNullable
                || operation.Collation != operation.OldColumn.Collation
                || operation.DefaultValue != operation.OldColumn.DefaultValue
                || HasDifferences(newAnnotations, oldAnnotations);

            var (oldDefaultValue, oldDefaultValueSql) = (operation.OldColumn.DefaultValue, operation.OldColumn.DefaultValueSql);

            if (alterStatementNeeded
                || !Equals(operation.DefaultValue, oldDefaultValue)
                || operation.DefaultValueSql != oldDefaultValueSql)
            {
                DropDefaultConstraint(operation.Table, operation.Name, builder);
                (oldDefaultValue, oldDefaultValueSql) = (null, null);
            }

            // The column is being made non-nullable. Generate an update statement before doing that, to convert any existing null values to
            // the default value
            if (operation is { IsNullable: false, OldColumn.IsNullable: true }
                && (operation.DefaultValueSql is not null || operation.DefaultValue is not null))
            {
                string defaultValueSql;
                if (operation.DefaultValueSql is not null)
                {
                    defaultValueSql = operation.DefaultValueSql;
                }
                else
                {
                    Check.DebugAssert(operation.DefaultValue is not null, "operation.DefaultValue is not null");

                    var typeMapping = (columnType != null
                            ? Dependencies.TypeMappingSource.FindMapping(operation.DefaultValue.GetType(), columnType)
                            : null)
                        ?? Dependencies.TypeMappingSource.GetMappingForValue(operation.DefaultValue);

                    defaultValueSql = typeMapping.GenerateSqlLiteral(operation.DefaultValue);
                }

                var updateBuilder = new StringBuilder()
                    .Append("UPDATE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                    .Append(" SET ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" = ")
                    .Append(defaultValueSql)
                    .Append(" WHERE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" IS NULL");

                if (Options.HasFlag(MigrationsSqlGenerationOptions.Idempotent))
                {
                    builder
                        .Append("EXEC(N'")
                        .Append(updateBuilder.ToString().TrimEnd('\n', '\r', ';').Replace("'", "''"))
                        .Append("')");
                }
                else
                {
                    builder.Append(updateBuilder.ToString());
                }

                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            if (alterStatementNeeded)
            {
                builder
                    .Append("ALTER TABLE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                    .Append(" ALTER COLUMN ");

                // NB: ComputedColumnSql, IsStored, DefaultValueSql, Comment, ValueGenerationStrategy, and Identity are
                //     handled elsewhere. Don't copy them here.
                //JET: DefaultValue is part of the ALTER COLUMN. Not a separate ALTER
                var definitionOperation = new AlterColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name,
                    ClrType = operation.ClrType,
                    ColumnType = operation.ColumnType,
                    IsUnicode = operation.IsUnicode,
                    IsFixedLength = operation.IsFixedLength,
                    MaxLength = operation.MaxLength,
                    Precision = operation.Precision,
                    Scale = operation.Scale,
                    IsRowVersion = operation.IsRowVersion,
                    IsNullable = operation.IsNullable,
                    Collation = operation.Collation,
                    OldColumn = operation.OldColumn,
                    DefaultValue = operation.DefaultValue,
                    DefaultValueSql = operation.DefaultValueSql
                };
                definitionOperation.AddAnnotations(
                    operation.GetAnnotations().Where(
                        a => a.Name != JetAnnotationNames.ValueGenerationStrategy
                            && a.Name != JetAnnotationNames.Identity));

                ColumnDefinition(
                    operation.Schema,
                    operation.Table,
                    operation.Name,
                    definitionOperation,
                    model,
                    builder);

                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            if (narrowed)
            {
                CreateIndexes(indexesToRebuild!, builder);
            }

            builder.EndCommand();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RenameIndexOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            RenameIndexOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
            if (string.IsNullOrEmpty(operation.Table))
            {
                throw new InvalidOperationException(JetStrings.IndexTableRequired);
            }

            builder.Append("RENAME INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table))
                .Append(".")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" TO ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="CreateTableOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
        /// <param name="builder">The command builder to use to build the commands.</param>
        /// <param name="terminate">Indicates whether or not to terminate the command after generating SQL for the operation.</param>
        protected override void Generate(
            CreateTableOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            base.Generate(operation, model, builder, terminate: false);
            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RenameTableOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            RenameTableOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
            // CHECK: Rename table operations require extensions like ADOX or DAO.
            //       A native way to do this would be to:
            //           1. CREATE TABLE `destination table`
            //           2. INSERT INTO ... SELECT ... FROM
            //           3. DROP TABLE `source table`
            //           4. Recrete indices and references.
            builder.Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" RENAME TO ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName!))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropTableOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
        /// <param name="builder">The command builder to use to build the commands.</param>
        /// <param name="terminate">Indicates whether or not to terminate the command after generating SQL for the operation.</param>
        protected override void Generate(
            DropTableOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="CreateIndexOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected override void Generate(
            CreateIndexOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            base.Generate(operation, model, builder, terminate: false);
            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropPrimaryKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
        /// <param name="builder">The command builder to use to build the commands.</param>
        /// <param name="terminate">Indicates whether or not to terminate the command after generating SQL for the operation.</param>
        protected override void Generate(
            DropPrimaryKeyOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            base.Generate(operation, model, builder, terminate: false);
            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Generates a SQL fragment for extras (filter, included columns, options) of an index from a <see cref="CreateIndexOperation" />.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="model">The target model which may be <see langword="null" /> if the operations exist without a model.</param>
        /// <param name="builder">The command builder to use to add the SQL fragment.</param>
        protected override void IndexOptions(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            if (operation is CreateIndexOperation createIndexOperation
                && !string.IsNullOrEmpty(createIndexOperation.Filter))
            {
                builder
                    .Append(" WITH ")
                    .Append(createIndexOperation.Filter);
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="JetCreateDatabaseOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            JetCreateDatabaseOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));
            builder
                .Append("CREATE DATABASE ")
                .Append(stringTypeMapping.GenerateSqlLiteral(operation.Name));

            if (!string.IsNullOrEmpty(operation.Password))
            {
                builder
                    .Append(" PASSWORD ")
                    .Append(stringTypeMapping.GenerateSqlLiteral(operation.Password));
            }

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true);
        }

        // TODO: The file name and data directory should be expanded when the CREATE/DROP DATABASE operation gets
        // executed, not when it gets created.
        /*
        private static string ExpandFileName(string fileName)
        {
            Check.NotNull(fileName, nameof(fileName));

            if (fileName.StartsWith("|DataDirectory|", StringComparison.OrdinalIgnoreCase))
            {
                var dataDirectory = AppDomain.CurrentDomain.GetData("DataDirectory") as string;
                if (string.IsNullOrEmpty(dataDirectory))
                {
                    dataDirectory = AppDomain.CurrentDomain.BaseDirectory;
                }

                fileName = Path.Combine(dataDirectory, fileName.Substring("|DataDirectory|".Length));
            }

            return Path.GetFullPath(fileName);
        }
        */

        /// <summary>
        ///     Builds commands for the given <see cref="JetDropDatabaseOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void Generate(
            JetDropDatabaseOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));
            builder
                .Append("DROP DATABASE ")
                //.Append(ExpandFileName(operation.Name))
                .Append(stringTypeMapping.GenerateSqlLiteral(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true);
        }

        /// <summary>
        ///     Builds commands for the given <see cref="AlterDatabaseOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            AlterDatabaseOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropForeignKeyOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected override void Generate(
            DropForeignKeyOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropIndexOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected override void Generate(
            DropIndexOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            if (string.IsNullOrEmpty(operation.Table))
            {
                throw new InvalidOperationException(JetStrings.IndexTableRequired);
            }
            builder
                .Append("DROP INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" ON ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table!));

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="DropColumnOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        /// <param name="terminate"> Indicates whether or not to terminate the command after generating SQL for the operation. </param>
        protected override void Generate(
            DropColumnOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            DropDefaultConstraint(operation.Table, operation.Name, builder);
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        /// <summary>
        ///     Builds commands for the given <see cref="RenameColumnOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            RenameColumnOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
            builder.Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table))
                .Append(" RENAME COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" TO ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand();
        }

        /// <summary>
        ///     Builds commands for the given <see cref="SqlOperation" /> by making calls on the given
        ///     <see cref="MigrationCommandListBuilder" />, and then terminates the final command.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(SqlOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            builder.AppendLine(operation.Sql);

            EndStatement(builder, operation.SuppressTransaction);
        }

        protected override void Generate(
            InsertDataOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            var sqlBuilder = new StringBuilder();

            var modificationCommands = GenerateModificationCommands(operation, model).ToList();
            var updateSqlGenerator = (IJetUpdateSqlGenerator)Dependencies.UpdateSqlGenerator;

            foreach (var batch in _commandBatchPreparer.CreateCommandBatches(modificationCommands, moreCommandSets: true))
            {
                updateSqlGenerator.AppendBulkInsertOperation(sqlBuilder, batch.ModificationCommands, commandPosition: 0);
            }

            if (Options.HasFlag(MigrationsSqlGenerationOptions.Idempotent))
            {
                builder
                    .Append("EXEC('")
                    .Append(sqlBuilder.ToString().TrimEnd('\n', '\r', ';').Replace("'", "''"))
                    .Append("')")
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
            else
            {
                builder.Append(sqlBuilder.ToString());
            }

            if (terminate)
            {
                builder.EndCommand();
            }
        }

        protected override void ColumnDefinition(
            string? schema,
            string table,
            string name,
            ColumnOperation operation,
            IModel? model,
            MigrationCommandListBuilder builder)
        {
            if (operation.ComputedColumnSql != null)
            {
                if (decimal.TryParse(operation.ComputedColumnSql, out decimal result))
                {
                    operation.DefaultValue = result;
                }
                else
                {
                    ComputedColumnDefinition(schema, table, name, operation, model, builder);
                    return;
                }
            }

            var columnType = GetColumnType(schema, table, name, operation, model);
            //int has no size - ignore
            if (columnType != null && columnType.StartsWith("int("))
            {
                columnType = "int";
            }
            builder
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                .Append(" ")
                .Append(columnType ?? "");

            builder.Append(operation.IsNullable ? " NULL" : " NOT NULL");

            DefaultValue(operation.DefaultValue, operation.DefaultValueSql, columnType, builder);
        }

        protected override string? GetColumnType(
            string? schema,
            string table,
            string name,
            ColumnOperation operation,
            IModel? model)
        {
            var storeType = operation.ColumnType;

            if (IsIdentity(operation) &&
                (storeType == null || Dependencies.TypeMappingSource.FindMapping(storeType) is JetIntTypeMapping or ShortTypeMapping))
            {
                // This column represents the actual identity.
                storeType = "counter";
            }
            else if (storeType != null &&
                     IsExplicitIdentityColumnType(storeType))
            {
                // While this column uses an identity type (e.g. counter), it is not an actual identity column, because
                // it was not marked as one.
                storeType = "integer";
            }

            storeType ??= base.GetColumnType(schema, table, name, operation, model);

            if (string.Equals(storeType, "counter", StringComparison.OrdinalIgnoreCase) &&
                operation[JetAnnotationNames.Identity] is string identity &&
                !string.IsNullOrEmpty(identity) &&
                identity != "1, 1")
            {
                storeType += $"({identity})";
            }

            if (storeType != null && storeType.Contains("bigint", StringComparison.OrdinalIgnoreCase))
            {
                storeType = storeType.Replace("bigint", "decimal");
            }

            return storeType;
        }

        /// <summary>
        ///     Generates a SQL fragment for the given referential action.
        /// </summary>
        /// <param name="referentialAction"> The referential action. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void ForeignKeyAction(ReferentialAction referentialAction, MigrationCommandListBuilder builder)
        {
            if (referentialAction == ReferentialAction.Restrict)
            {
                builder.Append("NO ACTION");
            }
            else
            {
                base.ForeignKeyAction(referentialAction, builder);
            }
        }

        /// <summary>
        ///     Generates a SQL fragment for the default constraint of a column.
        /// </summary>
        /// <param name="defaultValue"> The default value for the column. </param>
        /// <param name="defaultValueSql"> The SQL expression to use for the column's default constraint. </param>
        /// <param name="columnType"> Store/database type of the column. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void DefaultValue(
            object? defaultValue,
            string? defaultValueSql,
            string? columnType,
            MigrationCommandListBuilder builder)
        {
            if (defaultValueSql != null)
            {
                builder
                    .Append(" DEFAULT ")
                    .Append(defaultValueSql);
            }
            else if (defaultValue != null)
            {
                var typeMapping = (columnType != null
                                      ? Dependencies.TypeMappingSource.FindMapping(defaultValue.GetType(), columnType)
                                      : null) ??
                                  Dependencies.TypeMappingSource.GetMappingForValue(defaultValue);

                // All time related type mappings derive from JetDateTimeTypeMapping.
                defaultValue = typeMapping is JetDateTimeTypeMapping dateTimeTypeMapping
                    ? dateTimeTypeMapping.GenerateNonNullSqlLiteral(defaultValue, defaultClauseCompatible: true)
                    : typeMapping.GenerateSqlLiteral(defaultValue);

                builder
                    .Append(" DEFAULT ")
                    .Append((string)defaultValue);
            }
        }

        /// <summary>
        ///     Generates a SQL fragment to drop default constraints for a column.
        /// </summary>
        /// <param name="tableName"> The table that contains the column.</param>
        /// <param name="columnName"> The column. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void DropDefaultConstraint(
            string tableName,
            string columnName,
            MigrationCommandListBuilder builder)
        {
            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
                .Append(" ALTER COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columnName))
                .Append(" DROP DEFAULT")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        }

        /// <summary>
        ///     Gets the list of indexes that need to be rebuilt when the given column is changing.
        /// </summary>
        /// <param name="column"> The column. </param>
        /// <param name="currentOperation"> The operation which may require a rebuild. </param>
        /// <returns> The list of indexes affected. </returns>
        protected virtual IEnumerable<ITableIndex> GetIndexesToRebuild(
            IColumn? column,
            MigrationOperation currentOperation)
        {
            if (column == null)
            {
                yield break;
            }

            var table = column.Table;
            var createIndexOperations = _operations.SkipWhile(o => o != currentOperation)
                .Skip(1)
                .OfType<CreateIndexOperation>()
                .ToList();
            foreach (var index in table.Indexes)
            {
                var indexName = index.Name;
                if (createIndexOperations.Any(o => o.Name == indexName))
                {
                    continue;
                }

                if (index.Columns.Any(c => c == column))
                {
                    yield return index;
                }
                else if (index[JetAnnotationNames.Include] is IReadOnlyList<string> includeColumns
                         && includeColumns.Contains(column.Name))
                {
                    yield return index;
                }
            }
        }

        /// <summary>
        ///     Generates SQL to drop the given indexes.
        /// </summary>
        /// <param name="indexes"> The indexes to drop. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void DropIndexes(
            IEnumerable<ITableIndex> indexes,
            MigrationCommandListBuilder builder)
        {
            foreach (var index in indexes)
            {
                var table = index.Table;
                var operation = new DropIndexOperation
                {
                    Schema = table.Schema,
                    Table = table.Name,
                    Name = index.Name
                };
                operation.AddAnnotations(index.GetAnnotations());

                Generate(operation, table.Model.Model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        /// <summary>
        ///     Generates SQL to create the given indexes.
        /// </summary>
        /// <param name="indexes"> The indexes to create. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void CreateIndexes(
            IEnumerable<ITableIndex> indexes,
            MigrationCommandListBuilder builder)
        {
            foreach (var index in indexes)
            {
                Generate(CreateIndexOperation.CreateFrom(index), index.Table.Model.Model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        private static bool IsIdentity(ColumnOperation operation)
            => operation[JetAnnotationNames.Identity] != null
               || operation[JetAnnotationNames.ValueGenerationStrategy] as JetValueGenerationStrategy?
               == JetValueGenerationStrategy.IdentityColumn;

        private static bool IsExplicitIdentityColumnType(string columnType)
            => string.Equals("counter", columnType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals("identity", columnType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals("autoincrement", columnType, StringComparison.OrdinalIgnoreCase);

        private static bool HasDifferences(IEnumerable<IAnnotation> source, IEnumerable<IAnnotation> target)
        {
            var targetAnnotations = target.ToDictionary(a => a.Name);

            var count = 0;
            foreach (var sourceAnnotation in source)
            {
                if (!targetAnnotations.TryGetValue(sourceAnnotation.Name, out var targetAnnotation)
                    || !Equals(sourceAnnotation.Value, targetAnnotation.Value))
                {
                    return true;
                }

                count++;
            }

            return count != targetAnnotations.Count;
        }

        #region Schemas not supported

        protected override void Generate(EnsureSchemaOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
        }

        protected override void Generate(DropSchemaOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
        }

        #endregion

        #region Sequences not supported

        // JET does not have sequences
        protected override void Generate(RestartSequenceOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        protected override void Generate(CreateSequenceOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        protected override void Generate(RenameSequenceOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        protected override void Generate(AlterSequenceOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        protected override void Generate(DropSequenceOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        #endregion

    }
}