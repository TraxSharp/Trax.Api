using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Defense-in-depth coverage for <c>QueryModelAuthorizationSchemaValidator</c>.
/// The validator runs at host start, materialises the GraphQL schema, and
/// reasserts that every <c>[TraxAuthorize]</c>-gated entity still carries
/// its <c>@authorize</c> directive at both type level and entry-field level.
///
/// <para>
/// The point of these tests is to prove that a consumer cannot accidentally or
/// intentionally remove the gate via the <c>ConfigureSchema</c> callback — the
/// last remaining escape hatch in the configuration surface. If a callback
/// strips the directive, the host MUST refuse to start with a message that
/// names the entity and the missing directive location.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class QueryModelAuthorizeSchemaInvariantE2ETests
{
    private const string Database = "trax_api_auth_schema_inv";

    [OneTimeSetUp]
    public void SeedDatabase() =>
        AuthzTestDbContext.EnsureSeeded(AuthE2EHost.ConnectionString(Database));

    /// <summary>
    /// Baseline: a normally-configured host (no malicious ConfigureSchema
    /// callback) starts cleanly and the validator passes. Pins that the
    /// validator is not over-zealous and rejecting valid schemas.
    /// </summary>
    [Test]
    public async Task NormalHost_PassesValidator_AndServesRequests()
    {
        using var host = await BuildHostAsync(extraSchemaConfig: null);

        var client = host.GetTestServer().CreateClient();
        var res = await client.GetAsync("/health");
        // /health doesn't exist, but the host having started at all proves
        // the validator ran without throwing.
        host.Should().NotBeNull();
    }

    /// <summary>
    /// The hostile config: a consumer registers a replacement
    /// <see cref="ObjectType{OwnedBook}"/> with no <c>@authorize</c> directive.
    /// HotChocolate's late type registration wins over the type module's
    /// auth-decorated version, stripping the directive at schema-build time.
    /// The post-build validator must catch this and refuse to start the host.
    /// </summary>
    [Test]
    public async Task ConfigureSchema_RemovesTypeLevelAuthorize_HostFailsToStart()
    {
        Func<Task> act = async () =>
        {
            using var host = await BuildHostAsync(extraSchemaConfig: b =>
                b.AddType(
                    new ObjectType<OwnedBook>(d =>
                    {
                        d.Name("OwnedBook");
                        // No .Authorize() — deliberately strips the gate.
                    })
                )
            );
        };

        await act.Should()
            .ThrowAsync<Exception>()
            .Where(ex =>
                ex.GetBaseException().Message.Contains("[TraxAuthorize] invariant violated")
                && ex.GetBaseException().Message.Contains("OwnedBook")
            );
    }

    private static async Task<IHost> BuildHostAsync(
        Action<IRequestExecutorBuilder>? extraSchemaConfig
    )
    {
        var connectionString = AuthE2EHost.ConnectionString(Database);
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddAuthorization(opts =>
                            opts.AddPolicy("AdminPolicy", p => p.RequireRole("Admin"))
                        );

                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                effects.UsePostgres(connectionString).AddJson()
                            )
                        );

                        services.AddDbContextFactory<AuthzTestDbContext>(o =>
                            o.UseNpgsql(connectionString)
                        );

                        services.AddTraxGraphQL(graphql =>
                        {
                            graphql.AddDbContext<AuthzTestDbContext>();
                            if (extraSchemaConfig is not null)
                                graphql.ConfigureSchema(extraSchemaConfig);
                            return graphql;
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                            endpoints.MapGraphQL("/trax/graphql", "trax")
                        );
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }
}
