using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.Client;

public static class ServiceExtensions
{
    /// <summary>
    /// Registers the kernel GraphQL client: configuration, schema provider, validator, executor.
    /// The default schema provider is <see cref="IntrospectingSchemaProvider"/>; replace it with
    /// <see cref="FileSchemaProvider"/> or another <see cref="ISchemaProvider"/> implementation
    /// by calling <c>services.Replace(...)</c> after this method.
    /// </summary>
    public static IServiceCollection AddGraphQLClient(
        this IServiceCollection services,
        Uri baseAddress,
        Action<GraphQLClientConfigurationBuilder>? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var builder = new GraphQLClientConfigurationBuilder(baseAddress);
        options?.Invoke(builder);
        var configuration = builder.Build();

        return services
            .AddSingleton<IGraphQLClientConfiguration>(configuration)
            .AddSingleton<ISchemaProvider, IntrospectingSchemaProvider>()
            .AddSingleton<IGraphQLClientValidator, GraphQLClientValidator>()
            .AddSingleton<IGraphQLClientExecutor, GraphQLClientExecutor>();
    }

    /// <summary>
    /// Walks the given assemblies, instantiates every <see cref="IGenericGraphQLClientRequest"/>
    /// type without invoking its constructor, and validates each <c>Query</c> against the schema.
    /// Call this after <c>app.Build()</c> to fail fast on schema-incompatible queries.
    /// </summary>
    public static Task ValidateGraphQLClientAssembliesAsync(
        this IServiceProvider services,
        params Assembly[] assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        var validator = services.GetRequiredService<IGraphQLClientValidator>();
        return validator.ValidateAssembliesAsync(assemblies);
    }
}
