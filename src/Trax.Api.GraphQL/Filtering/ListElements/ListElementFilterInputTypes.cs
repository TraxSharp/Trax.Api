using HotChocolate.Data.Filters;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.ListElements;

/// <summary>
/// Filter input types used for the <i>elements</i> of a scalar collection property.
/// They mirror HotChocolate's stock operation filter inputs with <c>neq</c> removed.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate exposes the same operation filter input for a scalar property and for
/// the elements of a collection property. That is a problem for one operation: inside a
/// collection, <c>neq</c> lowers to <c>Any(x =&gt; x != value)</c> over a primitive
/// collection, which no EF Core provider can translate. The query passes GraphQL
/// validation and then throws at execution:
/// </para>
/// <code>
/// The LINQ expression .Where(p => EF.Property&lt;Badge[]&gt;(p, "Badges")
///     .AsQueryable().Any(p0 => p0 != @p)) could not be translated.
/// </code>
/// <para>
/// Removing <c>neq</c> from the shared scalar input would also remove it from ordinary
/// scalar fields, where it translates perfectly well. So the element position gets its
/// own set of types instead, bound by property runtime type (see
/// <see cref="ListElementFilterBinding"/>). Every other operation is untouched:
/// <c>eq</c>, <c>in</c>, <c>nin</c> and the comparable operators all translate.
/// </para>
/// <para>
/// The one filter this costs is <c>all: { neq: X }</c>, which did translate.
/// <c>none: { eq: X }</c> is exactly equivalent and still available.
/// </para>
/// </remarks>
internal static class ListElementFilterNaming
{
    /// <summary>
    /// The GraphQL name HotChocolate gives a scalar's stock filter input, so the list
    /// wrapper can keep the stock <c>List{Scalar}OperationFilterInput</c> name and the
    /// only visible schema change is the missing <c>neq</c>.
    /// </summary>
    public static string ScalarName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying switch
        {
            _ when underlying == typeof(int) => "Int",
            _ when underlying == typeof(short) => "Short",
            _ when underlying == typeof(long) => "Long",
            _ when underlying == typeof(byte) => "Byte",
            _ when underlying == typeof(float) => "Float",
            _ when underlying == typeof(double) => "Float",
            _ when underlying == typeof(decimal) => "Decimal",
            _ when underlying == typeof(bool) => "Boolean",
            _ when underlying == typeof(string) => "String",
            _ when underlying == typeof(Guid) => "UUID",
            _ when underlying == typeof(DateTime) => "DateTime",
            _ when underlying == typeof(DateTimeOffset) => "DateTime",
            _ => underlying.Name,
        };
    }

    /// <summary>The element input's name, distinct from the scalar input's name.</summary>
    public static string ElementTypeName(Type elementType) =>
        ScalarName(elementType) + "ListElementFilterInput";
}

/// <summary>
/// The list filter for a scalar collection. Identical to
/// <see cref="ListFilterInputType{T}"/> apart from keeping HotChocolate's stock name
/// while pointing <c>some</c>/<c>all</c>/<c>none</c> at the restricted element type.
/// </summary>
/// <typeparam name="TElement">The collection's element runtime type.</typeparam>
/// <typeparam name="TElementFilter">The restricted element filter input.</typeparam>
internal sealed class ListElementFilterInputType<TElement, TElementFilter>
    : ListFilterInputType<TElementFilter>
    where TElementFilter : FilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(
            "List" + ListElementFilterNaming.ScalarName(typeof(TElement)) + "OperationFilterInput"
        );
    }
}

/// <summary>Enum elements, without <c>neq</c>.</summary>
internal sealed class EnumListElementFilterInputType<TEnum> : EnumOperationFilterInputType<TEnum>
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(TEnum)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}

/// <summary>Comparable scalar elements (numeric, date, Guid), without <c>neq</c>.</summary>
internal sealed class ComparableListElementFilterInputType<T>
    : ComparableOperationFilterInputType<T>
    where T : struct
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(T)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}

/// <summary>String elements, without <c>neq</c>.</summary>
internal sealed class StringListElementFilterInputType : StringOperationFilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(string)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}

/// <summary>Boolean elements, without <c>neq</c>.</summary>
internal sealed class BooleanListElementFilterInputType : BooleanOperationFilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(bool)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}
