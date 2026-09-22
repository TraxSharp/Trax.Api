using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Describes what a caller holding a queue receipt can actually ask the operations surface.
///
/// <para>A <c>mode: QUEUE</c> mutation returns a <c>workQueueId</c> and an <c>externalId</c>.
/// The first is a usable key: <c>workQueue(id:)</c> yields the entry's status and the
/// <c>metadataId</c> of the execution it became, which <c>execution(id:)</c> then resolves.
/// The second is not — every execution lookup is keyed on the database id, so the correlation
/// id the framework hands out and later reuses for the execution cannot be queried by.</para>
/// </summary>
[TestFixture]
public class ExecutionCorrelationSurfaceTests
{
    private ServiceProvider? _serviceProvider;

    [TearDown]
    public async Task TearDown()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
    }

    [Test]
    public async Task WorkQueueReceipt_ResolvesToTheExecutionItBecame()
    {
        var types = await IntrospectAsync();

        ArgumentsOf(types, "WorkQueueQueries", "workQueue").Should().Contain("id");

        FieldsOf(types, "WorkQueueSummary")
            .Should()
            .Contain(
                ["status", "metadataId"],
                "following a receipt to its outcome needs the entry's state and the execution it created"
            );

        ArgumentsOf(types, "OperationsQueries", "execution").Should().Contain("id");
        FieldsOf(types, "ExecutionSummary").Should().Contain(["trainState", "failureReason"]);
    }

    [Test]
    public async Task NoExecutionLookup_IsKeyedOnExternalId()
    {
        var types = await IntrospectAsync();

        var byExternalId = new[] { "OperationsQueries", "WorkQueueQueries" }
            .SelectMany(type =>
                FieldsOf(types, type)
                    .Where(field => ArgumentsOf(types, type, field).Contains("externalId"))
                    .Select(field => $"{type}.{field}")
            )
            .ToList();

        byExternalId
            .Should()
            .BeEmpty(
                "the operations surface takes database ids only — a caller that kept just the "
                    + "externalId from its mutation response has no query to spend it on"
            );
    }

    [Test]
    public async Task ExternalId_IsReturnedByBothSidesOfTheCorrelation()
    {
        var types = await IntrospectAsync();

        FieldsOf(types, "WorkQueueSummary").Should().Contain("externalId");
        FieldsOf(types, "ExecutionSummary")
            .Should()
            .Contain(
                "externalId",
                "the same id identifies the queue entry and the execution it became — it is "
                    + "emitted on both, which is what makes the missing lookup a gap rather than a design"
            );
    }

    private static IReadOnlyList<string> FieldsOf(JsonElement types, string typeName) =>
        Type(types, typeName)
            .GetProperty("fields")
            .EnumerateArray()
            .Select(f => f.GetProperty("name").GetString()!)
            .ToList();

    private static IReadOnlyList<string> ArgumentsOf(
        JsonElement types,
        string typeName,
        string fieldName
    ) =>
        Type(types, typeName)
            .GetProperty("fields")
            .EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == fieldName)
            .GetProperty("args")
            .EnumerateArray()
            .Select(a => a.GetProperty("name").GetString()!)
            .ToList();

    private static JsonElement Type(JsonElement types, string typeName)
    {
        var type = types.GetProperty(typeName);
        type.ValueKind.Should()
            .NotBe(JsonValueKind.Null, $"the schema should declare a '{typeName}' type");
        return type;
    }

    /// <summary>
    /// Builds the operations surface and introspects the types a queue receipt would be
    /// spent against. Aliased by type name so the assertions read as schema facts.
    /// </summary>
    private async Task<JsonElement> IntrospectAsync()
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);

        var services = new ServiceCollection();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());

        _serviceProvider = services.BuildServiceProvider();

        var executor = await _serviceProvider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var result = await executor.ExecuteAsync(
            """
            {
              OperationsQueries: __type(name: "OperationsQueries") { fields { name args { name } } }
              WorkQueueQueries: __type(name: "WorkQueueQueries") { fields { name args { name } } }
              WorkQueueSummary: __type(name: "WorkQueueSummary") { fields { name args { name } } }
              ExecutionSummary: __type(name: "ExecutionSummary") { fields { name args { name } } }
            }
            """
        );

        var operationResult = result as OperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();

        using var document = JsonDocument.Parse(operationResult.ToJson());
        return document.RootElement.GetProperty("data").Clone();
    }
}
