using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.Extensions;

public static class ApiServiceExtensions
{
    /// <summary>
    /// Registers Trax API core services. Core train discovery and execution services
    /// are provided by Trax.Mediator's <c>AddServiceTrainBus()</c>.
    /// </summary>
    public static IServiceCollection AddTraxApi(this IServiceCollection services)
    {
        return services;
    }
}
