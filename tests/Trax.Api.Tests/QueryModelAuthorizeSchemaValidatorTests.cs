using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Startup;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Direct unit coverage for <see cref="QueryModelAuthorizationSchemaValidator"/>.
/// Builds GraphQL schemas by hand where the <c>@authorize</c> directive has
/// been deliberately stripped from a gated entity, then verifies the validator
/// throws at <c>StartAsync</c> with a message naming the entity and the
/// missing gate location. This is the suite that proves the validator's
/// failure paths — the corresponding success path runs through the full
/// <c>AddTraxGraphQL</c> pipeline in
/// <c>QueryModelAuthorizeSchemaInvariantE2ETests</c>.
/// </summary>
[TestFixture]
public class QueryModelAuthorizeSchemaValidatorTests
{
    [TraxQueryModel]
    [TraxAuthorize(Roles = "Admin")]
    private class GatedThing
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class GatedDbContext(DbContextOptions<GatedDbContext> options) : DbContext(options)
    {
        public DbSet<GatedThing> Things { get; set; } = null!;
    }

    [Test]
    public async Task Validator_BothDirectivesStripped_ThrowsNamingEntity()
    {
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: false,
            includeAuthorizeOnField: false
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[TraxAuthorize] invariant violated*")
            .WithMessage("*GatedThing*");
    }

    [Test]
    public async Task Validator_OnlyFieldDirectiveStripped_ThrowsNamingEntryField()
    {
        // Type-level gate is present, but the entry field has been stripped.
        // This is the more subtle failure — transitive navigation would still
        // be blocked, but `totalCount` and `pageInfo` would leak. The
        // validator must still catch it.
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: true,
            includeAuthorizeOnField: false
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[TraxAuthorize] invariant violated*")
            .WithMessage("*entry field*")
            .WithMessage("*gatedThings*");
    }

    [Test]
    public async Task Validator_OnlyTypeDirectiveStripped_ThrowsNamingType()
    {
        // Entry field guarded but the type itself isn't — transitive nav
        // through this type from another (ungated) entity would leak rows.
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: false,
            includeAuthorizeOnField: true
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[TraxAuthorize] invariant violated*")
            .WithMessage("*ObjectType*")
            .WithMessage("*GatedThing*");
    }

    [Test]
    public async Task Validator_BothDirectivesPresent_DoesNotThrow()
    {
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: true,
            includeAuthorizeOnField: true
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    /// <summary>
    /// Builds a minimal but realistic schema containing the gated entity. When
    /// <paramref name="includeAuthorizeOnType"/> is <c>false</c>, the ObjectType
    /// is registered WITHOUT the <c>@authorize</c> directive even though the
    /// entity carries <c>[TraxAuthorize]</c> — simulating the post-build state
    /// produced by a hostile <c>ConfigureSchema</c> or <c>TypeInterceptor</c>
    /// that strips the gate.
    /// </summary>
    private static async Task<(
        GraphQLConfiguration Config,
        IServiceProvider Services
    )> BuildSchemaAsync(bool includeAuthorizeOnType, bool includeAuthorizeOnField = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();

        var config = new TraxGraphQLBuilder(services).AddDbContext<GatedDbContext>().Build();
        services.AddSingleton(config);
        services.AddDbContextFactory<GatedDbContext>(o => o.UseInMemoryDatabase("svtests"));

        // Build a minimal schema by hand so we can control exactly which
        // directives are attached. This bypasses the QueryModelTypeModule
        // entirely — the production pipeline always wires the directive,
        // so to test the FAILURE path we have to construct the broken
        // schema ourselves.
        var gql = services.AddGraphQLServer("trax").AddAuthorization();

        ObjectType<GatedThing> objectType = includeAuthorizeOnType
            ? new ObjectType<GatedThing>(d => d.Authorize(new[] { "Admin" }))
            : new ObjectType<GatedThing>();

        gql.AddType(objectType);

        gql.AddQueryType(d =>
        {
            d.Name("RootQuery");
            d.Field("discover").Type<DiscoverObjectType>().Resolve(_ => new object());
        });

        gql.AddTypeExtension(
            new ObjectTypeExtension(d =>
            {
                d.Name("DiscoverQueries");
                var field = d.Field("gatedThings")
                    .Type<ListType<ObjectType<GatedThing>>>()
                    .Resolve(_ => Array.Empty<GatedThing>());
                if (includeAuthorizeOnField)
                    field.Authorize(new[] { "Admin" });
            })
        );

        var sp = services.BuildServiceProvider();
        // Materialise the schema so the resolver caches the executor; the
        // validator will resolve it via IRequestExecutorResolver later.
        var resolver = sp.GetRequiredService<IRequestExecutorResolver>();
        _ = await resolver.GetRequestExecutorAsync("trax");

        return (config, sp);
    }

    private sealed class DiscoverObjectType : ObjectType
    {
        protected override void Configure(IObjectTypeDescriptor descriptor) =>
            descriptor.Name("DiscoverQueries");
    }
}
