using System;
using System.Collections.Generic;
using System.Data.Jet;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Metadata.Internal
{
    public class JetEntityMaterializerSource : EntityMaterializerSource
    {
        private static readonly MethodInfo _readValue;

        private static readonly MethodInfo _getValueBufferMethod;

        private static readonly ConstructorInfo _obsoleteConstructor;

        static JetEntityMaterializerSource()
        {
            _readValue = typeof(ValueBuffer).GetTypeInfo().DeclaredProperties
                .Single(p => p.GetIndexParameters().Any()).GetMethod;
            _getValueBufferMethod = typeof(MaterializationContext).GetProperty(nameof(MaterializationContext.ValueBuffer)).GetMethod;
            _obsoleteConstructor = typeof(MaterializationContext).GetConstructor(new[] { typeof(ValueBuffer) });
        }



        public override Expression CreateReadValueExpression(
            Expression valueBuffer,
            Type type,
            int index,
            IPropertyBase property)
            => Expression.Call(
                TryReadValueMethod.MakeGenericMethod(type),
                valueBuffer,
                Expression.Constant(index),
                Expression.Constant(property, typeof(IPropertyBase)));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override Expression CreateMaterializeExpression(
            IEntityType entityType,
            Expression materializationExpression,
            int[] indexMap = null)
        {
            if (!entityType.HasClrType())
            {
                throw new InvalidOperationException(CoreStrings.NoClrType(entityType.DisplayName()));
            }

            if (entityType.IsAbstract())
            {
                throw new InvalidOperationException(CoreStrings.CannotMaterializeAbstractType(entityType));
            }

            var constructorBinding = (ConstructorBinding)entityType[CoreAnnotationNames.ConstructorBinding];

            if (constructorBinding == null)
            {
                var constructorInfo = entityType.ClrType.GetDeclaredConstructor(null);

                if (constructorInfo == null)
                {
                    throw new InvalidOperationException(CoreStrings.NoParameterlessConstructor(entityType.DisplayName()));
                }

                constructorBinding = new DirectConstructorBinding(constructorInfo, Array.Empty<ParameterBinding>());
            }

            // This is to avoid breaks because this method used to expect ValueBuffer but now expects MaterializationContext
            var valueBufferExpression = materializationExpression;
            if (valueBufferExpression.Type == typeof(MaterializationContext))
            {
                valueBufferExpression = Expression.Call(materializationExpression, _getValueBufferMethod);
            }
            else
            {
                materializationExpression = Expression.New(_obsoleteConstructor, materializationExpression);
            }

            var bindingInfo = new ParameterBindingInfo(
                entityType,
                materializationExpression,
                indexMap);

            var properties = new HashSet<IPropertyBase>(
                entityType.GetServiceProperties().Cast<IPropertyBase>()
                    .Concat(
                        entityType
                            .GetProperties()
                            .Where(p => !p.IsShadowProperty)));

            foreach (var consumedProperty in constructorBinding
                .ParameterBindings
                .SelectMany(p => p.ConsumedProperties))
            {
                properties.Remove(consumedProperty);
            }

            var constructorExpression = constructorBinding.CreateConstructorExpression(bindingInfo);

            if (properties.Count == 0)
            {
                return constructorExpression;
            }

            var instanceVariable = Expression.Variable(constructorBinding.RuntimeType, "instance");

            var blockExpressions
                = new List<Expression>
                {
                    Expression.Assign(
                        instanceVariable,
                        constructorExpression)
                };

            blockExpressions.AddRange(
                from property in properties
                let targetMember = Expression.MakeMemberAccess(
                    instanceVariable,
                    property.GetMemberInfo(forConstruction: true, forSet: true))
                select
                    Expression.Assign(
                        targetMember,
                        property is IServiceProperty
                            ? ((IServiceProperty)property).GetParameterBinding().BindToParameter(bindingInfo)
                            : CreateReadValueExpression(
                                valueBufferExpression,
                                targetMember.Type,
                                indexMap?[property.GetIndex()] ?? property.GetIndex(),
                                property)));

            blockExpressions.Add(instanceVariable);

            return Expression.Block(new[] { instanceVariable }, blockExpressions);
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [Obsolete]
        public override Expression CreateReadValueCallExpression(Expression valueBuffer, int index)
            => Expression.Call(valueBuffer, _readValue, Expression.Constant(index));


        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public new static readonly MethodInfo TryReadValueMethod
            = typeof(JetEntityMaterializerSource).GetTypeInfo()
                .GetDeclaredMethod(nameof(TryReadValue));

        private static TValue TryReadValue<TValue>(
            ValueBuffer valueBuffer,
            int index,
            IPropertyBase property = null)
        {
            object untypedValue = valueBuffer[index];

            try
            {
                if (untypedValue != null && !typeof(TValue).IsAssignableFrom(untypedValue.GetType()))
                {
                    if (typeof(TValue).IsAssignableFrom(typeof(TimeSpan)))
                        untypedValue = ((DateTime)untypedValue - JetConfiguration.TimeSpanOffset);
                    if (typeof(TValue).IsAssignableFrom(typeof(bool)))
                        untypedValue = Convert.ToBoolean(untypedValue);
                }

                // Relax requirement in case that on the DB there is null
                // This is usefull with foreign keys
                if (untypedValue == null && typeof(TValue).IsValueType && !typeof(TValue).IsNullableType())
                    return default(TValue);
                else
                    return (TValue)untypedValue;
            }
            catch (Exception e)
            {
                throw e;
            }

        }



    }
}
