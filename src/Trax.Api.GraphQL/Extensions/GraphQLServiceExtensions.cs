using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;

namespace Trax.Api.GraphQL.Extensions;

public static class GraphQLServiceExtensions
{
    private const string SchemaName = "trax";

    /// <summary>
    /// Registers the Trax GraphQL schema on a named HotChocolate server ("trax").
    /// This avoids conflicts with a consumer's own default GraphQL schema.
    /// </summary>
    public static IServiceCollection AddTraxGraphQL(this IServiceCollection services)
    {
        services.AddTraxApi();
        services
            .AddGraphQLServer(SchemaName)
            .AddQueryType<TrainQueries>()
            .AddMutationType()
            .AddTypeExtension<TrainMutations>()
            .AddTypeExtension<SchedulerMutations>();

        return services;
    }

    /// <summary>
    /// Maps the Trax GraphQL endpoint at the specified route prefix.
    /// Uses a named schema so it coexists with other HotChocolate schemas
    /// in the same application.
    /// </summary>
    public static WebApplication UseTraxGraphQL(
        this WebApplication app,
        string routePrefix = "/trax/graphql"
    )
    {
        app.MapGraphQL(routePrefix, SchemaName);
        return app;
    }
}
