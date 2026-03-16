namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Holds the resolved configuration for the Trax GraphQL schema,
/// including discovered query model registrations.
/// </summary>
public class GraphQLConfiguration
{
    public IReadOnlyList<QueryModelRegistration> ModelRegistrations { get; }

    public GraphQLConfiguration(IReadOnlyList<QueryModelRegistration> modelRegistrations)
    {
        ModelRegistrations = modelRegistrations;
    }
}
