// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;

namespace System.Reflection
{
    internal static class MemberInfoExtensions
    {
        public static Type GetMemberType(this MemberInfo memberInfo)
        => (memberInfo as PropertyInfo)?.PropertyType ?? ((FieldInfo)memberInfo).FieldType;

        public static bool IsSameAs(this MemberInfo? propertyInfo, MemberInfo? otherPropertyInfo)
            => propertyInfo == null
                ? otherPropertyInfo == null
                : (otherPropertyInfo != null
                    && (Equals(propertyInfo, otherPropertyInfo)
                        || (propertyInfo.Name == otherPropertyInfo.Name
                            && propertyInfo.DeclaringType != null
                            && otherPropertyInfo.DeclaringType != null
                            && (propertyInfo.DeclaringType == otherPropertyInfo.DeclaringType
                                || propertyInfo.DeclaringType.GetTypeInfo().IsSubclassOf(otherPropertyInfo.DeclaringType)
                                || otherPropertyInfo.DeclaringType.GetTypeInfo().IsSubclassOf(propertyInfo.DeclaringType)
                                || propertyInfo.DeclaringType.GetTypeInfo().ImplementedInterfaces.Contains(otherPropertyInfo.DeclaringType)
                                || otherPropertyInfo.DeclaringType.GetTypeInfo().ImplementedInterfaces
                                    .Contains(propertyInfo.DeclaringType)))));

        public static bool IsOverriddenBy(this MemberInfo? propertyInfo, MemberInfo? otherPropertyInfo)
            => propertyInfo == null
                ? otherPropertyInfo == null
                : (otherPropertyInfo != null
                    && (Equals(propertyInfo, otherPropertyInfo)
                        || (propertyInfo.Name == otherPropertyInfo.Name
                            && propertyInfo.DeclaringType != null
                            && otherPropertyInfo.DeclaringType != null
                            && (propertyInfo.DeclaringType == otherPropertyInfo.DeclaringType
                                || otherPropertyInfo.DeclaringType.GetTypeInfo().IsSubclassOf(propertyInfo.DeclaringType)
                                || otherPropertyInfo.DeclaringType.GetTypeInfo().ImplementedInterfaces
                                    .Contains(propertyInfo.DeclaringType)))));

        public static string GetSimpleMemberName(this MemberInfo member)
        {
            var name = member.Name;
            var index = name.LastIndexOf('.');
            return index >= 0 ? name[(index + 1)..] : name;
        }

        public static bool IsReallyVirtual(this MethodInfo method)
            => method.IsVirtual && !method.IsFinal;
    }
}
