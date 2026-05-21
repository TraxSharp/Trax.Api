using GraphQL.Types;
using GraphQLParser.AST;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// A graphql-dotnet <see cref="ScalarGraphType"/> implementation that accepts any value
/// shape. Used to back custom scalars discovered in an introspected schema (e.g.,
/// HotChocolate's <c>Any</c>, <c>DateTime</c>, <c>Uuid</c>, <c>JSON</c>) for client-side
/// query validation.
///
/// The client validator only cares that fields and types exist and that operations are
/// well-formed. It never serializes or deserializes scalar values, so permissive
/// pass-through implementations of <see cref="ParseValue"/> / <see cref="Serialize"/> /
/// <see cref="ParseLiteral"/> are correct here. Server-side execution applies the real
/// scalar semantics.
/// </summary>
internal sealed class PermissiveScalarGraphType : ScalarGraphType
{
    public PermissiveScalarGraphType(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public override object? ParseValue(object? value) => value;

    public override object? Serialize(object? value) => value;

    public override object? ParseLiteral(GraphQLValue value) =>
        value switch
        {
            GraphQLStringValue s => s.Value.ToString(),
            GraphQLIntValue i => i.Value.ToString(),
            GraphQLFloatValue f => f.Value.ToString(),
            GraphQLBooleanValue b => b.Value.Length > 0 && b.Value.Span[0] == 't',
            GraphQLNullValue => null,
            _ => null,
        };
}
