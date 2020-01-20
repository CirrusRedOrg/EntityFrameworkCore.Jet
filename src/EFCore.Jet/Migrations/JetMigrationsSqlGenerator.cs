// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data.Jet;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Metadata.Internal;
using EntityFrameworkCore.Jet.Migrations.Operations;
using EntityFrameworkCore.Jet.Properties;
using EntityFrameworkCore.Jet.Storage.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using EntityFrameworkCore.Jet.Utilities;
using JetConnection = System.Data.Jet.JetConnection;

namespace EntityFrameworkCore.Jet.Migrations
{
    public class JetMigrationsSqlGenerator : MigrationsSqlGenerator
    {
        private readonly IMigrationsAnnotationProvider _migrationsAnnotations;
        [NotNull] private readonly IJetOptions _options;

        private IReadOnlyList<MigrationOperation> _operations;
        private int _variableCounter;

        public JetMigrationsSqlGenerator(
            [NotNull] MigrationsSqlGeneratorDependencies dependencies,
            [NotNull] IMigrationsAnnotationProvider migrationsAnnotations,
            [NotNull] IJetOptions options)
            : base(dependencies)
        {
            _migrationsAnnotations = migrationsAnnotations;
            _options = options;
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
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(TruncateName(operation.Name)))
                    .Append(" ");
            }

            builder
                .Append("FOREIGN KEY (")
                .Append(ColumnList(operation.Columns))
                .Append(") REFERENCES ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.PrincipalTable, operation.PrincipalSchema));

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

        protected override void Generate(MigrationOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var createDatabaseOperation = operation as JetCreateDatabaseOperation;
            var dropDatabaseOperation = operation as JetDropDatabaseOperation;
            if (createDatabaseOperation != null)
            {
                Generate(createDatabaseOperation, model, builder);
            }
            else if (dropDatabaseOperation != null)
            {
                Generate(dropDatabaseOperation, model, builder);
            }
            else
            {
                base.Generate(operation, model, builder);
            }
        }

        protected override void Generate(
            AddColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        protected override void Generate(
            AddColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate)
        {
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
            }
        }

        protected override void Generate(
            AddForeignKeyOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        protected override void Generate(
            AddPrimaryKeyOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            base.Generate(operation, model, builder, terminate: false);

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        protected override void Generate(
            AlterColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            List<IIndex> indexesToRebuild = null;
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
                    ComputedColumnSql = operation.ComputedColumnSql
                };
                addColumnOperation.AddAnnotations(operation.GetAnnotations());

                // TODO: Use a column rebuild instead
                indexesToRebuild = GetIndexesToRebuild(property, operation).ToList();
                DropIndexes(indexesToRebuild, builder);
                Generate(dropColumnOperation, model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                Generate(addColumnOperation, model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
                CreateIndexes(indexesToRebuild, builder);
                builder.EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));

                return;
            }

            var narrowed = false;
            if (IsOldColumnSupported(model))
            {
                var valueGenerationStrategy = operation[
                    JetAnnotationNames.ValueGenerationStrategy] as JetValueGenerationStrategy?;
                var identity = valueGenerationStrategy == JetValueGenerationStrategy.IdentityColumn;
                var oldValueGenerationStrategy = operation.OldColumn[
                    JetAnnotationNames.ValueGenerationStrategy] as JetValueGenerationStrategy?;
                var oldIdentity = oldValueGenerationStrategy == JetValueGenerationStrategy.IdentityColumn;
                if (identity != oldIdentity)
                {
                    throw new InvalidOperationException(JetStrings.AlterIdentityColumn);
                }

                var type = operation.ColumnType
                           ?? GetColumnType(
                               operation.Schema,
                               operation.Table,
                               operation.Name,
                               operation.ClrType,
                               operation.IsUnicode,
                               operation.MaxLength,
                               operation.IsRowVersion,
                               model);
                var oldType = operation.OldColumn.ColumnType
                              ?? GetColumnType(
                                  operation.Schema,
                                  operation.Table,
                                  operation.Name,
                                  operation.OldColumn.ClrType,
                                  operation.OldColumn.IsUnicode,
                                  operation.OldColumn.MaxLength,
                                  operation.OldColumn.IsRowVersion,
                                  model);
                narrowed = type != oldType || !operation.IsNullable && operation.OldColumn.IsNullable;
            }

            if (narrowed)
            {
                indexesToRebuild = GetIndexesToRebuild(property, operation).ToList();
                DropIndexes(indexesToRebuild, builder);
            }

            DropDefaultConstraint(operation.Schema, operation.Table, operation.Name, builder);

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" ALTER COLUMN ");

            ColumnDefinition(
                operation.Schema,
                operation.Table,
                operation.Name,
                operation.ClrType,
                operation.ColumnType,
                operation.IsUnicode,
                operation.MaxLength,
                operation.IsRowVersion,
                operation.IsNullable,
                /*defaultValue:*/ null,
                /*defaultValueSql:*/ null,
                operation.ComputedColumnSql,
                /*identity:*/ false,
                operation,
                model,
                builder);

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            if (operation.DefaultValue != null
                || operation.DefaultValueSql != null)
            {
                builder
                    .Append("ALTER TABLE ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                    .Append(" ADD");
                DefaultValue(operation.DefaultValue, operation.DefaultValueSql, builder);
                builder
                    .Append(" FOR ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name))
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            if (narrowed)
            {
                CreateIndexes(indexesToRebuild, builder);
            }

            builder.EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

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

            builder.Append($"RENAME INDEX [{operation.Table}].[{TruncateName(operation.Name)}] TO [{TruncateName(operation.NewName)}]");
            builder.EndCommand();
        }

        /// <summary>
        ///     Generates a SQL fragment for the default constraint of a column.
        /// </summary>
        /// <param name="defaultValue"> The default value for the column. </param>
        /// <param name="defaultValueSql"> The SQL expression to use for the column's default constraint. </param>
        /// <param name="builder"> The command builder to use to add the SQL fragment. </param>
        protected override void DefaultValue(
            object defaultValue,
            string defaultValueSql,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(builder, nameof(builder));

            if (defaultValueSql != null)
            {
                builder
                    .Append(" DEFAULT (")
                    .Append(defaultValueSql)
                    .Append(")");
            }
            else if (defaultValue != null)
            {
                var typeMapping = Dependencies.TypeMappingSource.GetMappingForValue(defaultValue);

                // Jet does not support defaults for hh:mm:ss in create table statement
                bool isDateTimeValue =
                    defaultValue.GetType().UnwrapNullableType() == typeof(DateTime) ||
                    defaultValue.GetType().UnwrapNullableType() == typeof(DateTimeOffset);

                builder
                    .Append(" DEFAULT ")
                    .Append(
                        isDateTimeValue ? 
                        JetDateTimeTypeMapping.GenerateSqlLiteral(defaultValue, true) : 
                        typeMapping.GenerateSqlLiteral(defaultValue));
            }
        }


        protected override void Generate(
            RenameTableOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            if (operation.NewName != null)
            {
                builder.Append($"RENAME TABLE [{TruncateName(operation.Name)}] TO [{TruncateName(operation.NewName)}]");
                builder.EndCommand();
            }

            builder.EndCommand();
        }

        protected override void Generate(
            CreateIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        protected override void Generate(
            CreateIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate)
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
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(TruncateName(operation.Name)))
                .Append(" ON ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
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


        protected virtual void Generate(
            [NotNull] JetCreateDatabaseOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var connectionStringBuilder = JetProviderFactory.Instance.CreateConnectionStringBuilder();
            connectionStringBuilder.ConnectionString = _options.ConnectionString;

            var provider = connectionStringBuilder.GetProvider();
            if (string.IsNullOrEmpty(provider))
            {
                provider = JetConfiguration.OleDbDefaultProvider;
            }
            
            builder
                .Append("CREATE DATABASE ")
                .Append(JetConnection.GetConnectionString(provider, ExpandFileName(operation.Name)));

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true);
        }

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

        protected virtual void Generate(
            [NotNull] JetDropDatabaseOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("DROP DATABASE ")
                .Append(ExpandFileName(operation.Name))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: true);
        }

        protected override void Generate(
            AlterDatabaseOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

        }

        protected override void Generate(AlterTableOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            if (IsMemoryOptimized(operation)
                ^ IsMemoryOptimized(operation.OldTable))
            {
                throw new InvalidOperationException(JetStrings.AlterMemoryOptimizedTable);
            }

            base.Generate(operation, model, builder);
        }

        protected override void Generate(DropForeignKeyOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" DROP CONSTRAINT ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(TruncateName(operation.Name)));

            builder
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
        }

        protected override void Generate(
            DropIndexOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        protected virtual void Generate(
            [NotNull] DropIndexOperation operation,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder
                .Append("DROP INDEX ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(TruncateName(operation.Name)))
                .Append(" ON ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema));

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand();
            }
        }

        protected override void Generate(
            DropColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
            => Generate(operation, model, builder, terminate: true);

        protected override void Generate(
            DropColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder,
            bool terminate)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            DropDefaultConstraint(operation.Schema, operation.Table, operation.Name, builder);
            base.Generate(operation, model, builder, terminate: false);

            if (terminate)
            {
                builder
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                    .EndCommand(suppressTransaction: IsMemoryOptimized(operation, model, operation.Schema, operation.Table));
            }
        }

        protected override void Generate(
            RenameColumnOperation operation,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            builder.Append($"RENAME COLUMN [{operation.Table}].[{TruncateName(operation.Name)}] TO [{TruncateName(operation.NewName)}]");
            builder.EndCommand();
        }

        protected override void Generate(SqlOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

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
                if (batches[i].StartsWith("GO", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(batches[i]))
                {
                    continue;
                }

                var count = 1;
                if (i != batches.Length - 1
                    && batches[i + 1].StartsWith("GO", StringComparison.OrdinalIgnoreCase))
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
            Type clrType,
            string type,
            bool? unicode,
            int? maxLength,
            bool rowVersion,
            bool nullable,
            object defaultValue,
            string defaultValueSql,
            string computedColumnSql,
            IAnnotatable annotatable,
            IModel model,
            MigrationCommandListBuilder builder)
        {
            var valueGenerationStrategy = annotatable[
                JetAnnotationNames.ValueGenerationStrategy] as JetValueGenerationStrategy?;

            ColumnDefinition(
                schema,
                table,
                name,
                clrType,
                type,
                unicode,
                maxLength,
                rowVersion,
                nullable,
                defaultValue,
                defaultValueSql,
                computedColumnSql,
                valueGenerationStrategy == JetValueGenerationStrategy.IdentityColumn,
                annotatable,
                model,
                builder);
        }

        protected virtual void ColumnDefinition(
            [CanBeNull] string schema,
            [NotNull] string table,
            [NotNull] string name,
            [NotNull] Type clrType,
            [CanBeNull] string type,
            bool? unicode,
            int? maxLength,
            bool rowVersion,
            bool nullable,
            [CanBeNull] object defaultValue,
            [CanBeNull] string defaultValueSql,
            [CanBeNull] string computedColumnSql,
            bool identity,
            [NotNull] IAnnotatable annotatable,
            [CanBeNull] IModel model,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(clrType, nameof(clrType));
            Check.NotNull(annotatable, nameof(annotatable));
            Check.NotNull(builder, nameof(builder));

            if (computedColumnSql != null)
            {
                builder
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(name))
                    .Append(" AS ")
                    .Append(computedColumnSql);

                return;
            }

            base.ColumnDefinition(
                schema,
                table,
                name,
                clrType,
                type,
                unicode,
                maxLength,
                rowVersion,
                nullable,
                identity
                    ? null
                    : defaultValue,
                defaultValueSql,
                computedColumnSql,
                annotatable,
                model,
                builder);

            if (identity)
            {
                builder.Append(" IDENTITY");
            }
        }

        protected override void IndexTraits(MigrationOperation operation, IModel model, MigrationCommandListBuilder builder)
        {
            Check.NotNull(operation, nameof(operation));
            Check.NotNull(builder, nameof(builder));

            var clustered = operation[JetAnnotationNames.Clustered] as bool?;
            if (clustered.HasValue)
            {
                builder.Append(clustered.Value ? "CLUSTERED " : "NONCLUSTERED ");
            }
        }

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

        protected virtual void DropDefaultConstraint(
            [CanBeNull] string schema,
            [NotNull] string tableName,
            [NotNull] string columnName,
            [NotNull] MigrationCommandListBuilder builder)
        {
            Check.NotEmpty(tableName, nameof(tableName));
            Check.NotEmpty(columnName, nameof(columnName));
            Check.NotNull(builder, nameof(builder));

            var variable = "@var" + _variableCounter++;

            builder
                .Append("ALTER TABLE ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableName))
                .Append(" ALTER COLUMN ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columnName))
                .Append(" DROP DEFAULT")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        }

        protected virtual IEnumerable<IIndex> GetIndexesToRebuild(
            [CanBeNull] IProperty property,
            [NotNull] MigrationOperation currentOperation)
        {
            Check.NotNull(currentOperation, nameof(currentOperation));

            if (property == null)
            {
                yield break;
            }

            var createIndexOperations = _operations.SkipWhile(o => o != currentOperation).Skip(1)
                .OfType<CreateIndexOperation>().ToList();
            foreach (var index in property.GetContainingIndexes())
            {
                var indexName = index.Relational().Name;
                if (createIndexOperations.Any(o => o.Name == indexName))
                {
                    continue;
                }

                yield return index;
            }
        }

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
                    Schema = index.DeclaringEntityType.Relational().Schema,
                    Table = index.DeclaringEntityType.Relational().TableName,
                    Name = index.Relational().Name
                };
                operation.AddAnnotations(_migrationsAnnotations.ForRemove(index));

                Generate(operation, index.DeclaringEntityType.Model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

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
                    Name = index.Relational().Name,
                    Schema = index.DeclaringEntityType.Relational().Schema,
                    Table = index.DeclaringEntityType.Relational().TableName,
                    Columns = index.Properties.Select(p => p.Relational().ColumnName).ToArray(),
                    Filter = index.Relational().Filter
                };
                operation.AddAnnotations(_migrationsAnnotations.For(index));

                Generate(operation, index.DeclaringEntityType.Model, builder, terminate: false);
                builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        private bool IsMemoryOptimized(Annotatable annotatable, IModel model, string schema, string tableName)
            => annotatable[JetAnnotationNames.MemoryOptimized] as bool?
               ?? FindEntityTypes(model, schema, tableName)?.Any(t => t.Jet().IsMemoryOptimized) == true;

        private static bool IsMemoryOptimized(Annotatable annotatable)
            => annotatable[JetAnnotationNames.MemoryOptimized] as bool? == true;

        private string TruncateName(string operationName)
        {
            if (operationName.Length <= 64)
                return operationName;

            return operationName.Substring(0, 56) + operationName.GetHashCode().ToString("X8");
        }


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

    }
}
