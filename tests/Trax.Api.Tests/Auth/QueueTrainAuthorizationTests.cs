using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Api.Exceptions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Queueing a train through the operations surface applies that train's own authorization, on
/// top of whatever gates the namespace. A caller who may not run the train gets the same
/// authorization error as anywhere else in the API, and not the reason they were refused.
/// </summary>
[TestFixture]
public class QueueTrainAuthorizationTests
{
    private const string QueueTrainMutation = """
        mutation {
          operations {
            workQueue {
              queueTrain(input: { trainName: "Trax.X.IGuardedTrain", inputJson: "{}" }) {
                success
              }
            }
          }
        }
        """;

    [Test]
    public async Task A_caller_who_may_not_run_the_train_gets_an_authorization_error()
    {
        var operations = Substitute.For<IOperationsService>();
        operations
            .QueueTrainAsync(Arg.Any<QueueTrainInput>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new TrainAuthorizationException(
                    "Trax.X.IGuardedTrain",
                    "missing role GuardedTrainOperators"
                )
            );

        using var host = await StartHostAsync(operations);

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            QueueTrainMutation
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("it is refused like any other authorization failure, not as a failed result");
        doc.RootElement.GetRawText()
            .Should()
            .NotContain(
                "GuardedTrainOperators",
                "which requirement was missing is the server's to know, not the caller's"
            );

        await host.StopAsync();
    }

    private static async Task<IHost> StartHostAsync(IOperationsService operations)
    {
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
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());

                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .AllowAnonymousOperations()
                        );

                        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
                        services.AddScoped(_ => operations);
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
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
}
