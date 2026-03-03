using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;

namespace Trax.Api.GraphQL.Extensions;

public static class GraphQLServiceExtensions
{
    /// <summary>
    /// Registers Trax GraphQL schema and services using HotChocolate.
    /// </summary>
    public static IServiceCollection AddTraxGraphQL(this IServiceCollection services)
    {
        services.AddTraxApi();
        services
            .AddGraphQLServer()
            .AddQueryType<TrainQueries>()
            .AddMutationType()
            .AddTypeExtension<TrainMutations>()
            .AddTypeExtension<SchedulerMutations>();

        return services;
    }

    /// <summary>
    /// Maps the Trax GraphQL endpoint at the specified route prefix.
    /// </summary>
    public static WebApplication UseTraxGraphQL(
        this WebApplication app,
        string routePrefix = "/graphql"
    )
    {
        app.MapGraphQL(routePrefix);
        return app;
    }
}
