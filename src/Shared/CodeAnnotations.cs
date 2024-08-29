// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace JetBrains.Annotations
{
    [AttributeUsage(
        AttributeTargets.Method
        | AttributeTargets.Parameter
        | AttributeTargets.Property
        | AttributeTargets.Delegate
        | AttributeTargets.Field)]
    internal sealed class NotNullAttribute : Attribute
    {
    }

    [AttributeUsage(
        AttributeTargets.Method
        | AttributeTargets.Parameter
        | AttributeTargets.Property
        | AttributeTargets.Delegate
        | AttributeTargets.Field)]
    internal sealed class CanBeNullAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class InvokerParameterNameAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class NoEnumerationAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    internal sealed class ContractAnnotationAttribute([NotNull] string contract, bool forceFullStates) : Attribute
    {
        public string Contract { get; } = contract;

        public bool ForceFullStates { get; } = forceFullStates;

        public ContractAnnotationAttribute([NotNull] string contract)
            : this(contract, false)
        {
        }
    }

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class UsedImplicitlyAttribute(ImplicitUseKindFlags useKindFlags, ImplicitUseTargetFlags targetFlags)
        : Attribute
    {
        public UsedImplicitlyAttribute()
            : this(ImplicitUseKindFlags.Default, ImplicitUseTargetFlags.Default)
        {
        }

        public UsedImplicitlyAttribute(ImplicitUseKindFlags useKindFlags)
            : this(useKindFlags, ImplicitUseTargetFlags.Default)
        {
        }

        public UsedImplicitlyAttribute(ImplicitUseTargetFlags targetFlags)
            : this(ImplicitUseKindFlags.Default, targetFlags)
        {
        }

        public ImplicitUseKindFlags UseKindFlags { get; } = useKindFlags;
        public ImplicitUseTargetFlags TargetFlags { get; } = targetFlags;
    }

    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Delegate)]
    internal sealed class StringFormatMethodAttribute([NotNull] string formatParameterName) : Attribute
    {
        [NotNull]
        public string FormatParameterName { get; } = formatParameterName;
    }

    [Flags]
    internal enum ImplicitUseKindFlags
    {
        Default = Access | Assign | InstantiatedWithFixedConstructorSignature,
        Access = 1,
        Assign = 2,
        InstantiatedWithFixedConstructorSignature = 4,
        InstantiatedNoFixedConstructorSignature = 8
    }

    [Flags]
    internal enum ImplicitUseTargetFlags
    {
        Default = Itself,
        Itself = 1,
        Members = 2,
        WithMembers = Itself | Members
    }
}