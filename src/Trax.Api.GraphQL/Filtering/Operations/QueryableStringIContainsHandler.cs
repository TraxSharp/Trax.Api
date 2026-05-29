using System.Linq.Expressions;
using System.Reflection;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Filters.Expressions;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.Operations;

/// <summary>
/// Handles the <c>icontains</c> operation. Emits
/// <c>x != null &amp;&amp; x.ToLower().Contains(term.ToLower())</c>, which Npgsql
/// translates to <c>lower(col) LIKE lower(@p)</c> and the InMemory provider runs in
/// process via the BCL methods.
/// </summary>
internal sealed class QueryableStringIContainsHandler : QueryableStringOperationHandler
{
    private static readonly MethodInfo ContainsMethod = typeof(string).GetMethod(
        nameof(string.Contains),
        [typeof(string)]
    )!;

    public QueryableStringIContainsHandler(InputParser inputParser)
        : base(inputParser)
    {
        // The operand is required: `icontains: null` is rejected, matching the
        // built-in `contains` handler.
        CanBeNull = false;
    }

    protected override int Operation => TraxFilterOperations.IContains;

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
                    .SetMessage("`icontains` does not accept null. Provide a string value.")
                    .Build()
            );

        var contains = Expression.Call(
            CaseInsensitiveExpression.Lower(property),
            ContainsMethod,
            CaseInsensitiveExpression.Lower(Expression.Constant(term, typeof(string)))
        );

        return Expression.AndAlso(CaseInsensitiveExpression.NotNull(property), contains);
    }
}
