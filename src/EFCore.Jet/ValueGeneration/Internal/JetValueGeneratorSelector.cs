// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EntityFrameworkCore.Jet.Metadata;
using EntityFrameworkCore.Jet.Storage.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using EntityFrameworkCore.Jet.Utilities;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace EntityFrameworkCore.Jet.ValueGeneration.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class JetValueGeneratorSelector : RelationalValueGeneratorSelector
    {
        private readonly IJetSequenceValueGeneratorFactory _sequenceFactory;

        private readonly IJetRelationalConnection _relationalConnection;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public JetValueGeneratorSelector(
            [NotNull] ValueGeneratorSelectorDependencies dependencies,
            [NotNull] IJetSequenceValueGeneratorFactory sequenceFactory,
            [NotNull] IJetRelationalConnection relationalConnection)
            : base(dependencies)
        {
            Check.NotNull(sequenceFactory, nameof(sequenceFactory));
            Check.NotNull(relationalConnection, nameof(relationalConnection));

            _sequenceFactory = sequenceFactory;
            _relationalConnection = relationalConnection;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public new virtual IJetValueGeneratorCache Cache => (IJetValueGeneratorCache)base.Cache;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override ValueGenerator Select(IProperty property, IEntityType entityType)
        {
            Check.NotNull(property, nameof(property));
            Check.NotNull(entityType, nameof(entityType));

            return property.GetValueGeneratorFactory() == null
                   && property.Jet().ValueGenerationStrategy == JetValueGenerationStrategy.SequenceHiLo
                ? _sequenceFactory.Create(property, Cache.GetOrAddSequenceState(property), _relationalConnection)
                : base.Select(property, entityType);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override ValueGenerator Create(IProperty property, IEntityType entityType)
        {
            Check.NotNull(property, nameof(property));
            Check.NotNull(entityType, nameof(entityType));

            return property.ClrType.UnwrapNullableType() == typeof(Guid)
                ? property.ValueGenerated == ValueGenerated.Never
                  || property.Jet().DefaultValueSql != null
                    ? (ValueGenerator)new TemporaryGuidValueGenerator()
                    : new SequentialGuidValueGenerator()
                : base.Create(property, entityType);
        }
    }
}
