using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Top-level Trax integration entry point. Sits at the same API-surface level as
/// <c>AddTraxDashboard()</c> and <c>AddTraxGraphQL()</c> - not nested inside <c>AddTrax(...)</c>,
/// because a GraphQL client is a library consumed inside subsystems, not a subsystem itself.
/// </summary>
public static class AddTraxGraphQLClientExtensions
{
    /// <summary>
    /// Layers Trax-aware features onto a kernel client already registered via
    /// <see cref="ServiceExtensions.AddGraphQLClient"/>. Currently configures the
    /// <see cref="AssemblySchemaProvider"/> as the schema source when a configurator is given.
    /// </summary>
    /// <param name="services">DI container - must already have <c>AddGraphQLClient(...)</c> called.</param>
    /// <param name="configureSchema">
    /// Same delegate the server's <c>Program.cs</c> uses for <c>AddGraphQLServer().ConfigureSchema(...)</c>.
    /// When supplied, replaces the default <see cref="IntrospectingSchemaProvider"/> with
    /// <see cref="AssemblySchemaProvider"/>.
    /// </param>
    public static IServiceCollection AddTraxGraphQLClient(
        this IServiceCollection services,
        Action<IRequestExecutorBuilder>? configureSchema = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureSchema is not null)
        {
            var provider = new AssemblySchemaProvider(configureSchema);
            services.Replace(ServiceDescriptor.Singleton<ISchemaProvider>(provider));
        }

        return services;
    }
}
