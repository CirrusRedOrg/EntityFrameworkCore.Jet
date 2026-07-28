// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class LibRedConditionAttribute(LibRedCondition conditions) : Attribute, ITestCondition
    {
        public LibRedCondition Conditions { get; set; } = conditions;

        public ValueTask<bool> IsMetAsync()
        {
            var isMet = true;

            if (Conditions.HasFlag(LibRedCondition.IsNotCI))
            {
                isMet &= !TestEnvironment.IsCI;
            }

            return ValueTask.FromResult(isMet);
        }

        public string SkipReason =>
            // ReSharper disable once UseStringInterpolation
            string.Format(
                "The test LibRed does not meet these conditions: '{0}'",
                string.Join(
                    ", ", Enum.GetValues(typeof(LibRedCondition))
                        .Cast<Enum>()
                        .Where(f => Conditions.HasFlag(f))
                        .Select(f => Enum.GetName(typeof(LibRedCondition), f))));
    }
}
