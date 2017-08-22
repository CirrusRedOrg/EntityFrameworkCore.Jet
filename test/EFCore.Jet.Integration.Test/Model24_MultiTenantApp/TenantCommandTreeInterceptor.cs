using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EFCore.Jet.Integration.Test.Model24_MultiTenantApp
{
    public class TenantCommandTreeInterceptor : IDbCommandTreeInterceptor
    {
        public void TreeCreated(DbCommandTreeInterceptionContext interceptionContext)
        {
            if (interceptionContext.OriginalResult.DataSpace != DataSpace.SSpace) return;

            if (interceptionContext.Result.CommandTreeKind == DbCommandTreeKind.Insert)
                InterceptInsertStatement(interceptionContext);
        }

        private void InterceptInsertStatement(DbCommandTreeInterceptionContext interceptionContext)
        {
            var insertCommand = interceptionContext.Result as DbInsertCommandTree;

            List<DbModificationClause> finalSetClauses = new List<DbModificationClause>((IEnumerable<DbModificationClause>)insertCommand.SetClauses);

            //TENANT AWARE
            string column = "TenantId";
            DbExpression newValue = "JustMe"; // Here we should insert the right value


            // TODO: Need to check if this entity is a Multitenant entity in the right way
            // You can use the attribute like in the original sample
            if (
                interceptionContext.ObjectContexts.Count() == 1 && 
                interceptionContext.ObjectContexts.First().DefaultContainerName == "HistoryContext")
                return;

            finalSetClauses.Add(
                GetInsertSetClause(column, newValue, insertCommand));

            // Construct the final clauses, object representation of sql insert command values

            // In insert probably you can avoid to change the newInstanceAfterInsert because you are using a Guid for the entity ID that is always unique (it does not matter the tenant). 

            var newInsertCommand = new DbInsertCommandTree(
                insertCommand.MetadataWorkspace,
                insertCommand.DataSpace,
                insertCommand.Target,
                new ReadOnlyCollection<DbModificationClause>(finalSetClauses),
                insertCommand.Returning);

            interceptionContext.Result = newInsertCommand;
        }

        private DbSetClause GetInsertSetClause(string column, DbExpression newValueToSetToDb, DbInsertCommandTree insertCommand)
        {
            // Create the variable reference in order to create the property
            DbVariableReferenceExpression variableReference = DbExpressionBuilder.Variable(insertCommand.Target.VariableType,
                insertCommand.Target.VariableName);
            // Create the property to which will assign the correct value
            DbPropertyExpression tenantProperty = DbExpressionBuilder.Property(variableReference, column);
            // Create the set clause, object representation of sql insert command
            DbSetClause newSetClause = DbExpressionBuilder.SetClause(tenantProperty, newValueToSetToDb);
            return newSetClause;
        }
    }
}
