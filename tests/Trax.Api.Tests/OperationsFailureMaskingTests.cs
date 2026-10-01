using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.Auth;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// What a GraphQL caller sees when an operations mutation fails on the server's infrastructure
/// rather than being refused: a masked error carrying nothing about the server. The real
/// <see cref="OperationsService"/> runs here, over a mediator that fails the way a database
/// outage does, so the test covers the service's rethrow and the error filter's masking together.
/// </summary>
[TestFixture]
public class OperationsFailureMaskingTests
{
    private const string TrainName = "Trax.Api.Tests.OperationsFailureMaskingTests+IMaskedTrain";

    private const string UnreachableHost = "10.0.0.5";

    [Test]
    public async Task QueueTrain_WhenTheDatabaseIsUnreachable_FailsMaskedWithNothingAboutTheServer()
    {
        var execution = Substitute.For<ITrainExecutionService>();
        execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new NpgsqlException($"Failed to connect to {UnreachableHost}:5432"));

        using var host = await StartHostAsync(execution);

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            $$"""
            mutation {
              operations {
                workQueue {
                  queueTrain(input: { trainName: "{{TrainName}}", inputJson: "{}" }) {
                    success
                    message
                  }
                }
              }
            }
            """
        );

        var raw = doc.RootElement.GetRawText();
        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeTrue("an infrastructure failure is not a refusal, so it is not a failed result");
        errors[0].GetProperty("message").GetString().Should().Be("Unexpected Execution Error");
        raw.Should().NotContain(UnreachableHost).And.NotContain("Npgsql");

        await host.StopAsync();
    }

    internal static TrainRegistration Registration() =>
        new()
        {
            ServiceType = typeof(IMaskedTrain),
            ImplementationType = typeof(MaskedTrain),
            InputType = typeof(MaskedInput),
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = nameof(IMaskedTrain),
            ImplementationTypeName = nameof(MaskedTrain),
            InputTypeName = nameof(MaskedInput),
            OutputTypeName = nameof(Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

    private static async Task<IHost> StartHostAsync(ITrainExecutionService execution)
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Registration()]);
        IDataContextProviderFactory dataContextFactory = new InMemoryContextProviderFactory(
            new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot()
        );

        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddAuthentication();
                        services.AddAuthorization();

                        services.AddSingleton<TraxMarker>();
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddSingleton(dataContextFactory);

                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .AllowAnonymousOperations()
                        );

                        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        services.AddScoped(_ => execution);
                        services.AddScoped<IOperationsService>(sp => new OperationsService(
                            discovery,
                            dataContextFactory,
                            new SchedulerConfiguration(),
                            execution,
                            sp
                        ));
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    internal interface IMaskedTrain;

    private sealed class MaskedTrain : IMaskedTrain;

    internal sealed record MaskedInput(string? Value = null);
}
