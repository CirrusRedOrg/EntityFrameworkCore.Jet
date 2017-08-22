// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
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
    public class JetValueGeneratorCache : ValueGeneratorCache, IJetValueGeneratorCache
    {
        private readonly ConcurrentDictionary<string, JetSequenceValueGeneratorState> _sequenceGeneratorCache
            = new ConcurrentDictionary<string, JetSequenceValueGeneratorState>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="ValueGeneratorCache" /> class.
        /// </summary>
        /// <param name="dependencies"> Parameter object containing dependencies for this service. </param>
        public JetValueGeneratorCache([NotNull] ValueGeneratorCacheDependencies dependencies)
            : base(dependencies)
        {
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual JetSequenceValueGeneratorState GetOrAddSequenceState(IProperty property)
        {
            Check.NotNull(property, nameof(property));

            var sequence = property.Jet().FindHiLoSequence();

            Debug.Assert(sequence != null);

            return _sequenceGeneratorCache.GetOrAdd(
                GetSequenceName(sequence),
                sequenceName => new JetSequenceValueGeneratorState(sequence));
        }

        private static string GetSequenceName(ISequence sequence)
            => (sequence.Schema == null ? "" : sequence.Schema + ".") + sequence.Name;
    }
}
