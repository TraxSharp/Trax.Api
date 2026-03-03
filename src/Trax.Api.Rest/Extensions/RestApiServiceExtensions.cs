using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Extensions;
using Trax.Api.Rest.Endpoints;

namespace Trax.Api.Rest.Extensions;

public static class RestApiServiceExtensions
{
    /// <summary>
    /// Registers Trax REST API services.
    /// </summary>
    public static IServiceCollection AddTraxRestApi(this IServiceCollection services)
    {
        services.AddTraxApi();
        return services;
    }

    /// <summary>
    /// Maps all Trax REST API endpoints under the specified route prefix.
    /// </summary>
    public static WebApplication UseTraxRestApi(
        this WebApplication app,
        string routePrefix = "/api"
    )
    {
        var group = app.MapGroup(routePrefix);
        group.MapTrainEndpoints();
        group.MapSchedulerEndpoints();
        group.MapManifestEndpoints();
        group.MapManifestGroupEndpoints();
        group.MapExecutionEndpoints();
        return app;
    }
}
