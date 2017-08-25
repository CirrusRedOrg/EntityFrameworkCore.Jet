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


        static JetEntityMaterializerSource()
        {
            _readValue = typeof(ValueBuffer).GetTypeInfo().DeclaredProperties
                .Single(p => p.GetIndexParameters().Any()).GetMethod;
        }



        public override Expression CreateReadValueExpression(
            Expression valueBuffer,
            Type type,
            int index,
            IProperty property)
            => Expression.Call(
                TryReadValueMethod.MakeGenericMethod(type),
                valueBuffer,
                Expression.Constant(index),
                Expression.Constant(property, typeof(IPropertyBase)));

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public override  Expression CreateMaterializeExpression(
            IEntityType entityType,
            Expression valueBufferExpression,
            int[] indexMap = null)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            var materializer = entityType as IEntityMaterializer;

            if (materializer != null)
            {
                return Expression.Call(
                    Expression.Constant(materializer),
                    ((Func<ValueBuffer, object>)materializer.CreateEntity).GetMethodInfo(),
                    valueBufferExpression);
            }

            if (!entityType.HasClrType())
            {
                throw new InvalidOperationException(CoreStrings.NoClrType(entityType.DisplayName()));
            }

            if (entityType.IsAbstract())
            {
                throw new InvalidOperationException(CoreStrings.CannotMaterializeAbstractType(entityType));
            }

            var constructorInfo = entityType.ClrType.GetDeclaredConstructor(null);

            if (constructorInfo == null)
            {
                throw new InvalidOperationException(CoreStrings.NoParameterlessConstructor(entityType.DisplayName()));
            }

            var instanceVariable = Expression.Variable(entityType.ClrType, "instance");

            var blockExpressions
                = new List<Expression>
                {
                    Expression.Assign(
                        instanceVariable,
                        Expression.New(constructorInfo))
                };

            blockExpressions.AddRange(
                from property in entityType.GetProperties().Where(p => !p.IsShadowProperty)
                let targetMember = Expression.MakeMemberAccess(
                    instanceVariable,
                    property.GetMemberInfo(forConstruction: true, forSet: true))
                select
                Expression.Assign(
                    targetMember,
                    CreateReadValueExpression(
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

                return (TValue)untypedValue;
            }
            catch (Exception e)
            {
                ThrowReadValueExceptionMethod.MakeGenericMethod(typeof(TValue)).Invoke(null, new[] {e, untypedValue, property});
            }

            return default(TValue);
        }



    }
}
