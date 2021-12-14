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
        private readonly IMigrationsAnnotationProvider _migrationsAnnotations;
        private readonly IJetOptions _options;

        private IReadOnlyList<MigrationOperation> _operations;
        private RelationalTypeMapping _stringTypeMapping;
        private RelationalTypeMapping _keepBreakingCharactersStringTypeMapping;

        /// <summary>
        ///     Creates a new <see cref="JetMigrationsSqlGenerator" /> instance.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this service. </param>
        /// <param name="migrationsAnnotations"> Provider-specific Migrations annotations to use. </param>
        /// <param name="options"> Provider-specific options. </param>
        public JetMigrationsSqlGenerator(
            [NotNull] MigrationsSqlGeneratorDependencies dependencies,
            [NotNull] IMigrationsAnnotationProvider migrationsAnnotations,
            [NotNull] IJetOptions options)
            : base(dependencies)
        {
            _migrationsAnnotations = migrationsAnnotations;
            _options = options;
            _stringTypeMapping = dependencies.TypeMappingSource.FindMapping(typeof(string));
            _keepBreakingCharactersStringTypeMapping = ((JetStringTypeMapping) _stringTypeMapping).Clone(keepLineBreakCharacters: true);
        }

        // ReSharper disable once OptionalParameterHierarchyMismatch
        public override IReadOnlyList<MigrationCommand> Generate(IReadOnlyList<MigrationOperation> operations, IModel model)
        {
            _operations = operations;
            try
            {
                return base.Generate(operations, model);
            }
            finally
            {
                _operations = null;
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
        protected override void Generate(MigrationOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

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
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            if (IsIdentity(operation))
            {
                // NB: This gets added to all added non-nullable columns by MigrationsModelDiffer. We need to suppress
                //     it, here because SQL Server can't have both IDENTITY and a DEFAULT constraint on the same column.
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
        ///     Builds commands for the given <see cref="AlterColumnOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            AlterColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            IEnumerable<IIndex> indexesToRebuild = null;
            var property = FindProperty(model, operation.Schema, operation.Table, operation.Name);

            if (operation.ComputedColumnSql != null)
            {
                var dropColumnOperation = new DropColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name
                };
                if (property != null)
                {
                    dropColumnOperation.AddAnnotations(_migrationsAnnotations.ForRemove(property));
                }

                var addColumnOperation = new AddColumnOperation
                {
                    Schema = operation.Schema,
                    Table = operation.Table,
                    Name = operation.Name,
                    ClrType = operation.ClrType,
                    ColumnType = operation.ColumnType,
                    IsUnicode = operation.IsUnicode,
                    MaxLength = operation.MaxLength,
                    IsRowVersion = operation.IsRowVersion,
                    IsNullable = operation.IsNullable,
                    DefaultValue = operation.DefaultValue,
                    DefaultValueSql = operation.DefaultValueSql,
                    ComputedColumnSql = operation.ComputedColumnSql,
                    IsFixedLength = operation.IsFixedLength
                };
                addColumnOperation.AddAnnotations(operation.GetAnnotations());

                // TODO: Use a column rebuild instead
                indexesToRebuild = GetIndexesToRebuild(property, operation)
                    .ToList();
                DropIndexes(indexesToRebuild, builder);
                Generate(dropColumnOperation, model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                Generate(addColumnOperation, model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                CreateIndexes(indexesToRebuild, builder);
                builder.EndCommand();

                return;
            }

            var narrowed = false;
            if (IsOldColumnSupported(model))
            {
                if (IsIdentity(operation) != IsIdentity(operation.OldColumn))
                {
                    throw new InvalidOperationException(JetStrings.AlterIdentityColumn);
                }

                var type = operation.ColumnType
                           ?? GetColumnType(
                               operation.Schema,
                               operation.Table,
                               operation.Name,
                               operation,
                               model);
                var oldType = operation.OldColumn.ColumnType
                              ?? GetColumnType(
                                  operation.Schema,
                                  operation.Table,
                                  operation.Name,
                                  operation.OldColumn,
                                  model);
                narrowed = type != oldType || !operation.IsNullable && operation.OldColumn.IsNullable;
            }

            if (narrowed)
            {
                indexesToRebuild = GetIndexesToRebuild(property, operation)
                    .ToList();
                DropIndexes(indexesToRebuild, builder);
            }

            DropDefaultConstraint(operation.Table, operation.Name, builder);

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table))
                .Append(" ALTER COLUMN ");

            // NB: DefaultValue, DefaultValueSql, and identity are handled elsewhere. Don't copy them here.
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
                IsRowVersion = operation.IsRowVersion,
                IsNullable = operation.IsNullable,
                ComputedColumnSql = operation.ComputedColumnSql,
                OldColumn = operation.OldColumn
            };
            definitionOperation.AddAnnotations(
                operation.GetAnnotations()
                    .Where(
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

            if (operation.DefaultValue != null
                || operation.DefaultValueSql != null)
            {
                builder
                    .Append("ALTER TABLE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table))
                    .Append(" ADD");
                DefaultValue(operation.DefaultValue, operation.DefaultValueSql, operation.ColumnType, builder);
                builder
                    .Append(" FOR ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            // TODO: Implement comment/description support. (ADOX/DAO)
            /*
            if (operation.OldColumn.Comment != operation.Comment)
            {
                var dropDescription = operation.OldColumn.Comment != null;
                if (dropDescription)
                {
                    DropDescription(
                        builder,
                        operation.Schema,
                        operation.Table,
                        operation.Name);
                }

                if (operation.Comment != null)
                {
                    AddDescription(
                        builder, operation.Comment,
                        operation.Schema,
                        operation.Table,
                        operation.Name,
                        omitSchemaVariable: dropDescription);
                }
            }
            */

            if (narrowed)
            {
                CreateIndexes(indexesToRebuild, builder);
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
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

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
        ///     Builds commands for the given <see cref="RenameTableOperation" />
        ///     by making calls on the given <see cref="MigrationCommandListBuilder" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected override void Generate(
            RenameTableOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            // CHECK: Rename table operations require extensions like ADOX or DAO.
            //       A native way to do this would be to:
            //           1. CREATE TABLE `destination table`
            //           2. INSERT INTO ... SELECT ... FROM
            //           3. DROP TABLE `source table`
            //           4. Recrete indices and references.
            builder.Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" RENAME TO ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.NewName))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand();
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
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder.Append("CREATE ");

            if (operation.IsUnique)
            {
                builder.Append("UNIQUE ");
            }

            IndexTraits(operation, model, builder);

            builder
                .Append("INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" ON ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table))
                .Append(" (")
                .Append(ColumnList(operation.Columns))
                .Append(")");

            if (!string.IsNullOrEmpty(operation.Filter))
            {
                builder
                    .Append(" WHERE ")
                    .Append(operation.Filter);
            }

            if (terminate)
            {
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                EndStatement(builder);
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
            [NotNull] JetCreateDatabaseOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("CREATE DATABASE ")
                .Append(_keepBreakingCharactersStringTypeMapping.GenerateSqlLiteral(operation.Name));

            if (!string.IsNullOrEmpty(operation.Password))
            {
                builder
                    .Append(" PASSWORD ")
                    .Append(_keepBreakingCharactersStringTypeMapping.GenerateSqlLiteral(operation.Password));
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
            [NotNull] JetDropDatabaseOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("DROP DATABASE ")
                //.Append(ExpandFileName(operation.Name))
                .Append(_keepBreakingCharactersStringTypeMapping.GenerateSqlLiteral(operation.Name))
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
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));
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
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table))
                .Append(" DROP CONSTRAINT ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name));

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand();
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
            [NotNull] DropIndexOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("DROP INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .Append(" ON ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table));

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
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

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
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

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
        protected override void Generate(SqlOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            // TODO: Jet does not support batches and should never generate a "GO" in the first place.
            //       So this code should do nothing.

            var batches = Regex.Split(
                Regex.Replace(
                    operation.Sql,
                    @"\\\r?\n",
                    string.Empty,
                    default(RegexOptions),
                    TimeSpan.FromMilliseconds(1000.0)),
                @"^\s*(GO[ \t]+[0-9]+|GO)(?:\s+|$)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline,
                TimeSpan.FromMilliseconds(1000.0));
            for (var i = 0; i < batches.Length; i++)
            {
                if (batches[i]
                        .StartsWith("GO", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(batches[i]))
                {
                    continue;
                }

                var count = 1;
                if (i != batches.Length - 1
                    && batches[i + 1]
                        .StartsWith("GO", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(
                        batches[i + 1], "([0-9]+)",
                        default(RegexOptions),
                        TimeSpan.FromMilliseconds(1000.0));
                    if (match.Success)
                    {
                        count = int.Parse(match.Value);
                    }
                }

                for (var j = 0; j < count; j++)
                {
                    builder.Append(batches[i]);

                    if (i == batches.Length - 1)
                    {
                        builder.AppendLine();
                    }

                    EndStatement(builder, operation.SuppressTransaction);
                }
            }
        }
        
        protected override void ColumnDefinition(
            string schema,
            string table,
            string name,
            ColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));
            
            if (operation.ComputedColumnSql != null)
            {
                ComputedColumnDefinition(schema, table, name, operation, model, builder);
                return;
            }

            var columnType = GetColumnType(schema, table, name, operation, model);
            builder
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                .Append(" ")
                .Append(columnType);

            builder.Append(operation.IsNullable ? " NULL" : " NOT NULL");

            DefaultValue(operation.DefaultValue, operation.DefaultValueSql, columnType, builder);
        }

        protected override string GetColumnType(
            [CanBeNull] string schema,
            [NotNull] string table,
            [NotNull] string name,
            [NotNull] ColumnOperation operation,
            [CanBeNull] IModel model)
        {
            var storeType = operation.ColumnType;
            
            if (IsIdentity(operation))
            {
                // This column represents the actual identity.
                if (storeType != null &&
                    Dependencies.TypeMappingSource.FindMapping(storeType) is JetIntTypeMapping)
                {
                    storeType = "counter";
                }
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

            return storeType;
        }

        /// <summary>
        ///     Generates a SQL fragment for the given referential action.
        /// </summary>
        /// <param name="referentialAction"> The referential action. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void ForeignKeyAction(ReferentialAction referentialAction, MigrationCommandListBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

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
            [CanBeNull] object defaultValue,
            [CanBeNull] string defaultValueSql,
            [CanBeNull] string columnType,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

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
                    .Append(defaultValue);
            }
        }

        /// <summary>
        ///     Generates a SQL fragment for a foreign key constraint of an <see cref="AddForeignKeyOperation" />.
        /// </summary>
        /// <param name="operation"> The operation. </param>
        /// <param name="model"> The target model which may be <c>null</c> if the operations exist without a model. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void ForeignKeyConstraint(
            AddForeignKeyOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            if (operation.Name != null)
            {
                builder
                    .Append("CONSTRAINT ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .Append(" ");
            }

            builder
                .Append("FOREIGN KEY (")
                .Append(ColumnList(operation.Columns))
                .Append(") REFERENCES ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.PrincipalTable));

            if (operation.PrincipalColumns != null)
            {
                builder
                    .Append(" (")
                    .Append(ColumnList(operation.PrincipalColumns))
                    .Append(")");
            }

            if (operation.OnUpdate != ReferentialAction.NoAction)
            {
                builder.Append(" ON UPDATE ");
                ForeignKeyAction(operation.OnUpdate, builder);
            }

            if (operation.OnDelete != ReferentialAction.NoAction)
            {
                builder.Append(" ON DELETE ");
                ForeignKeyAction(operation.OnDelete, builder);
            }
        }

        /// <summary>
        ///     Generates a SQL fragment to drop default constraints for a column.
        /// </summary>
        /// <param name="tableName"> The table that contains the column.</param>
        /// <param name="columnName"> The column. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected virtual void DropDefaultConstraint(
            [NotNull] string tableName,
            [NotNull] string columnName,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(tableName, nameof(tableName));
            Check.NotEmpty(columnName, nameof(columnName));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
                .Append(" ALTER COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columnName))
                .Append(" DROP DEFAULT")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        }

        /// <summary>
        ///     Gets the list of indexes that need to be rebuilt when the given property is changing.
        /// </summary>
        /// <param name="property"> The property. </param>
        /// <param name="currentOperation"> The operation which may require a rebuild. </param>
        /// <returns> The list of indexes affected. </returns>
        protected virtual IEnumerable<IIndex> GetIndexesToRebuild(
            [CanBeNull] IProperty property,
            [NotNull] MigrationOperation currentOperation)
        {
            Check.NotNull(currentOperation, nameof(currentOperation));

            if (property == null)
            {
                yield break;
            }

            var createIndexOperations = _operations.SkipWhile(o => o != currentOperation)
                .Skip(1)
                .OfType<CreateIndexOperation>()
                .ToList();
            foreach (var index in property.DeclaringEntityType.GetIndexes()
                .Concat(
                    property.DeclaringEntityType.GetDerivedTypes()
                        .SelectMany(et => et.GetDeclaredIndexes())))
            {
                var indexName = index.GetName();
                if (createIndexOperations.Any(o => o.Name == indexName))
                {
                    continue;
                }

                if (index.Properties.Any(p => p == property))
                {
                    yield return index;
                }
                else if (index.GetIncludeProperties() is IReadOnlyList<string> includeProperties)
                {
                    if (includeProperties.Contains(property.Name))
                    {
                        yield return index;
                    }
                }
            }
        }

        /// <summary>
        ///     Generates SQL to drop the given indexes.
        /// </summary>
        /// <param name="indexes"> The indexes to drop. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void DropIndexes(
            [NotNull] IEnumerable<IIndex> indexes,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(indexes, nameof(indexes));
            Check.NotNull(builder, nameof(builder));

            foreach (var index in indexes)
            {
                var operation = new DropIndexOperation
                {
                    Schema = index.DeclaringEntityType.GetSchema(),
                    Table = index.DeclaringEntityType.GetTableName(),
                    Name = index.GetName()
                };
                operation.AddAnnotations(_migrationsAnnotations.ForRemove(index));

                Generate(operation, index.DeclaringEntityType.Model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        /// <summary>
        ///     Generates SQL to create the given indexes.
        /// </summary>
        /// <param name="indexes"> The indexes to create. </param>
        /// <param name="builder"> The command builder to use to build the commands. </param>
        protected virtual void CreateIndexes(
            [NotNull] IEnumerable<IIndex> indexes,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(indexes, nameof(indexes));
            Check.NotNull(builder, nameof(builder));

            foreach (var index in indexes)
            {
                var operation = new CreateIndexOperation
                {
                    IsUnique = index.IsUnique,
                    Name = index.GetName(),
                    Schema = index.DeclaringEntityType.GetSchema(),
                    Table = index.DeclaringEntityType.GetTableName(),
                    Columns = index.Properties.Select(p => p.GetColumnName())
                        .ToArray(),
                    Filter = index.GetFilter()
                };
                operation.AddAnnotations(_migrationsAnnotations.For(index));

                Generate(operation, index.DeclaringEntityType.Model, builder, terminate: false);
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

        #region Schemas not supported

        protected override void Generate(EnsureSchemaOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));
        }

        protected override void Generate(DropSchemaOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));
        }

        #endregion

        #region Sequences not supported

        // JET does not have sequences
        protected override void Generate(RestartSequenceOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        protected override void Generate(CreateSequenceOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        protected override void Generate(RenameSequenceOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        protected override void Generate(AlterSequenceOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        protected override void Generate(DropSequenceOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            throw new NotSupportedException("JET does not support sequences");
        }

        #endregion

        private string DelimitValue<T>(T value)
            => Dependencies.TypeMappingSource.FindMapping(typeof(T))
                .GenerateSqlLiteral(value);
    }
}