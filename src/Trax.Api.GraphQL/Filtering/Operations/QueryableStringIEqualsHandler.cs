using System.Linq.Expressions;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Filters.Expressions;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.Operations;

/// <summary>
/// Handles the <c>ieq</c> operation. Emits
/// <c>x != null &amp;&amp; x.ToLower() == term.ToLower()</c>, which Npgsql translates to
/// <c>lower(col) = lower(@p)</c> (sargable against a <c>btree(lower(col))</c> index) and
/// the InMemory provider runs in process via the BCL methods.
/// </summary>
internal sealed class QueryableStringIEqualsHandler : QueryableStringOperationHandler
{
    public QueryableStringIEqualsHandler(InputParser inputParser)
        : base(inputParser)
    {
        CanBeNull = false;
    }

    protected override int Operation => TraxFilterOperations.IEquals;

    public override Expression HandleOperation(
        QueryableFilterContext context,
        IFilterOperationField field,
        IValueNode value,
        object? parsedValue
    )
    {
        var property = context.GetInstance();

        if (parsedValue is not string term)
            throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage("`ieq` does not accept null. Provide a string value.")
                    .Build()
            );

        var equal = Expression.Equal(
            CaseInsensitiveExpression.Lower(property),
            CaseInsensitiveExpression.Lower(Expression.Constant(term, typeof(string)))
        );

        return Expression.AndAlso(CaseInsensitiveExpression.NotNull(property), equal);
    }
}
