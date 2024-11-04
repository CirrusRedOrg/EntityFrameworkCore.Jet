// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Metadata;

namespace Microsoft.EntityFrameworkCore.Diagnostics
{
    /// <summary>
    ///     A <see cref="DiagnosticSource" /> event payload class for events that have
    ///     a property.
    /// </summary>
    /// <remarks>
    ///     Constructs the event payload.
    /// </remarks>
    /// <param name="eventDefinition"> The event definition. </param>
    /// <param name="messageGenerator"> A delegate that generates a log message for this event. </param>
    /// <param name="jetValueGenerationStrategy"> The JET value generation strategy. </param>
    /// <param name="otherValueGenerationStrategy"> The other value generation strategy. </param>
    /// <param name="property"> The property. </param>
    public class ConflictingValueGenerationStrategiesEventData(
        EventDefinitionBase eventDefinition,
        Func<EventDefinitionBase, EventData, string> messageGenerator,
        JetValueGenerationStrategy jetValueGenerationStrategy,
        string otherValueGenerationStrategy,
        IProperty property) : EventData(eventDefinition, messageGenerator)
    {

        /// <summary>
        ///     The Jet value generation strategy.
        /// </summary>
        public virtual JetValueGenerationStrategy JetValueGenerationStrategy { get; } = jetValueGenerationStrategy;

        /// <summary>
        ///     The other value generation strategy.
        /// </summary>
        public virtual string OtherValueGenerationStrategy { get; } = otherValueGenerationStrategy;

        /// <summary>
        ///     The property.
        /// </summary>
        public virtual IProperty Property { get; } = property;
    }
}
