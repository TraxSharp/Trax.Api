using FluentAssertions;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Data.Filters;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Filtering;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Coverage for the opt-in case-insensitive string filter operations (<c>icontains</c>,
/// <c>ieq</c>) added via <c>ConfigureFiltering(f =&gt; f.AddCaseInsensitiveStringOperations())</c>.
/// Tests run end-to-end against a fully-wired HotChocolate executor over the EF Core
/// InMemory provider, which evaluates the <c>lower()</c> expression tree in process, so
/// the same operations that translate to <c>lower(col) LIKE lower(@p)</c> on Npgsql are
/// exercised here without a database.
/// </summary>
[TestFixture]
public class CaseInsensitiveFilterTests
{
    #region Behavior — icontains

    [Test]
    public async Task IContains_UpperCaseTerm_MatchesRegardlessOfCase()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        var ids = await QueryIdsAsync(executor, "name: { icontains: \"WALL\" }");

        // Wally (1) and WALLACE (2); bob (3) does not match.
        ids.Should().Equal(1, 2);
    }

    [Test]
    public async Task IContains_LowerCaseTerm_MatchesRegardlessOfCase()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        var ids = await QueryIdsAsync(executor, "name: { icontains: \"wall\" }");

        ids.Should().Equal(1, 2);
    }

    [Test]
    public async Task IContains_EmptyTerm_MatchesEveryNonNullRow()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        var ids = await QueryIdsAsync(executor, "name: { icontains: \"\" }");

        // string.Contains("") is true; every seeded row has a non-null name.
        ids.Should().Equal(1, 2, 3);
    }

    [Test]
    public async Task IContains_NullColumn_DoesNotThrowAndExcludesNullRows()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        // Nickname is null on WALLACE (2). The null-guard must exclude it without a
        // NullReferenceException when ToLower() is applied.
        var ids = await QueryIdsAsync(executor, "nickname: { icontains: \"wall\" }");

        // Champ (1) no, null (2) excluded, WALL-E (3) yes.
        ids.Should().Equal(3);
    }

    [Test]
    public async Task IContains_NoMatch_ReturnsEmpty()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        var ids = await QueryIdsAsync(executor, "name: { icontains: \"zzz\" }");

        ids.Should().BeEmpty();
    }

    #endregion

    #region Behavior — ieq

    [Test]
    public async Task IEq_MatchesRegardlessOfCase()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        (await QueryIdsAsync(executor, "name: { ieq: \"wally\" }")).Should().Equal(1);
        (await QueryIdsAsync(executor, "name: { ieq: \"WALLY\" }")).Should().Equal(1);
        (await QueryIdsAsync(executor, "name: { ieq: \"WaLLy\" }")).Should().Equal(1);
    }

    [Test]
    public async Task IEq_IsExact_NotSubstring()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        // "wall" is a substring of Wally/WALLACE but not equal to either.
        (await QueryIdsAsync(executor, "name: { ieq: \"wall\" }"))
            .Should()
            .BeEmpty();
    }

    #endregion

    #region Behavior — stock operations remain case-sensitive (non-breaking)

    [Test]
    public async Task StockContains_StaysCaseSensitive_WhenCaseInsensitiveOpsEnabled()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        // Lower-case "wall" matches nothing case-sensitively: Wally has a capital W,
        // WALLACE is all upper. icontains would return 1 and 2 here.
        (await QueryIdsAsync(executor, "name: { contains: \"wall\" }"))
            .Should()
            .BeEmpty();

        // Upper-case "WALL" matches only WALLACE (2) case-sensitively.
        (await QueryIdsAsync(executor, "name: { contains: \"WALL\" }"))
            .Should()
            .Equal(2);
    }

    [Test]
    public async Task StockEq_StaysCaseSensitive_WhenCaseInsensitiveOpsEnabled()
    {
        var executor = await BuildExecutorAsync<PeopleDbContext>(seedData: true);

        (await QueryIdsAsync(executor, "name: { eq: \"wally\" }")).Should().BeEmpty();
        (await QueryIdsAsync(executor, "name: { eq: \"Wally\" }")).Should().Equal(1);
    }

    #endregion

    #region Schema — operators present only when enabled, and applied globally

    [Test]
    public async Task Schema_WhenEnabled_StringFilterInputExposesCaseInsensitiveOps()
    {
        var schema = await BuildSchemaAsync<PeopleDbContext>(caseInsensitive: true);

        var stringFilter = schema.GetType<InputObjectType>("StringOperationFilterInput");
        var fieldNames = stringFilter.Fields.Select(f => f.Name).ToHashSet();

        fieldNames.Should().Contain("icontains");
        fieldNames.Should().Contain("ieq");
        // Stock operations are still there, unchanged.
        fieldNames.Should().Contain("contains");
        fieldNames.Should().Contain("eq");
    }

    [Test]
    public async Task Schema_WhenNotEnabled_StringFilterInputHasNoCaseInsensitiveOps()
    {
        var schema = await BuildSchemaAsync<PeopleDbContext>(caseInsensitive: false);

        var stringFilter = schema.GetType<InputObjectType>("StringOperationFilterInput");
        var fieldNames = stringFilter.Fields.Select(f => f.Name).ToHashSet();

        // Default behavior is untouched: stock ops present, custom ops absent.
        fieldNames.Should().Contain("contains");
        fieldNames.Should().Contain("eq");
        fieldNames.Should().NotContain("icontains");
        fieldNames.Should().NotContain("ieq");
    }

    [Test]
    public async Task IContains_WorksOnExposeAsProjectedStringField()
    {
        // ExposeAs restricts the filter input to interface properties, but the string
        // fields still use the shared StringOperationFilterInput, so the global
        // convention operators apply.
        var executor = await BuildExecutorAsync<MemberRefDbContext>(seedData: true);

        var result = await executor.ExecuteAsync(
            "{ discover { memberRefs(where: { name: { icontains: \"WALL\" } }) { totalCount } } }"
        );

        var op = result.ExpectOperationResult();
        op.Errors.Should().BeNullOrEmpty();
        TotalCount(op).Should().Be(2);
    }

    [Test]
    public async Task IContains_WorksOnCustomFilterTypeStringField()
    {
        // A custom AddFilterType binds only `name`; the operations on that string field
        // still come from the global convention.
        var executor = await BuildExecutorAsync<PeopleDbContext>(
            seedData: true,
            customize: b => b.AddFilterType<Person, PersonNameOnlyFilter>()
        );

        var ids = await QueryIdsAsync(executor, "name: { icontains: \"WALL\" }");
        ids.Should().Equal(1, 2);
    }

    #endregion

    #region Builder — threading, idempotency, validation

    [Test]
    public void ConfigureFiltering_ThreadsModuleIntoConfiguration()
    {
        var config = BuildConfig<PeopleDbContext>(b =>
            b.ConfigureFiltering(f => f.AddCaseInsensitiveStringOperations())
        );

        config
            .FilterModules.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<CaseInsensitiveStringFilterModule>();
    }

    [Test]
    public void ConfigureFiltering_WithoutCall_LeavesNoFilterModules()
    {
        var config = BuildConfig<PeopleDbContext>(_ => { });

        config.FilterModules.Should().BeEmpty();
    }

    [Test]
    public void AddCaseInsensitiveStringOperations_CalledTwiceInOneBuilder_RegistersOnce()
    {
        var config = BuildConfig<PeopleDbContext>(b =>
            b.ConfigureFiltering(f =>
                f.AddCaseInsensitiveStringOperations().AddCaseInsensitiveStringOperations()
            )
        );

        config.FilterModules.Should().ContainSingle();
    }

    [Test]
    public void ConfigureFiltering_CalledTwice_RegistersModuleOnce()
    {
        var config = BuildConfig<PeopleDbContext>(b =>
            b.ConfigureFiltering(f => f.AddCaseInsensitiveStringOperations())
                .ConfigureFiltering(f => f.AddCaseInsensitiveStringOperations())
        );

        config.FilterModules.Should().ContainSingle();
    }

    [Test]
    public void ConfigureFiltering_WithFilteringDisabledEverywhere_ThrowsAtBuild()
    {
        var services = new ServiceCollection();
        services.AddDbContext<NoFilterDbContext>(o =>
            o.UseInMemoryDatabase("CiFilterTest_" + Guid.NewGuid())
        );

        var builder = new TraxGraphQLBuilder(services);
        builder.AddDbContext<NoFilterDbContext>();
        builder.ConfigureFiltering(f => f.AddCaseInsensitiveStringOperations());

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*filtering*");
    }

    #endregion

    #region Helpers

    private static async Task<List<int>> QueryIdsAsync(IRequestExecutor executor, string where)
    {
        var result = await executor.ExecuteAsync(
            $"{{ discover {{ people(where: {{ {where} }}) {{ nodes {{ id }} }} }} }}"
        );

        var op = result.ExpectOperationResult();
        op.Errors.Should().BeNullOrEmpty();

        var discover = (IReadOnlyDictionary<string, object?>)op.Data!["discover"]!;
        var people = (IReadOnlyDictionary<string, object?>)discover["people"]!;
        var nodes = (IReadOnlyList<object?>)people["nodes"]!;

        return nodes
            .Select(n => Convert.ToInt32(((IReadOnlyDictionary<string, object?>)n!)["id"]))
            .OrderBy(id => id)
            .ToList();
    }

    private static int TotalCount(IOperationResult op)
    {
        var discover = (IReadOnlyDictionary<string, object?>)op.Data!["discover"]!;
        var connection = (IReadOnlyDictionary<string, object?>)discover["memberRefs"]!;
        return Convert.ToInt32(connection["totalCount"]);
    }

    private static GraphQLConfiguration BuildConfig<TContext>(Action<TraxGraphQLBuilder> customize)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddDbContext<TContext>(o =>
            o.UseInMemoryDatabase("CiFilterTest_" + Guid.NewGuid())
        );

        var builder = new TraxGraphQLBuilder(services);
        builder.AddDbContext<TContext>();
        customize(builder);
        return builder.Build();
    }

    private static async Task<ISchema> BuildSchemaAsync<TContext>(bool caseInsensitive)
        where TContext : DbContext
    {
        var executor = await BuildExecutorAsync<TContext>(caseInsensitive: caseInsensitive);
        return executor.Schema;
    }

    private static async Task<IRequestExecutor> BuildExecutorAsync<TContext>(
        Action<TraxGraphQLBuilder>? customize = null,
        bool seedData = false,
        bool caseInsensitive = true
    )
        where TContext : DbContext
    {
        // AddDbContext rebuilds options per scope, so the database name and root must be
        // stable values (not a fresh Guid inside the lambda) or the seed scope and the
        // query scope would get different in-memory stores.
        var dbName = "CiFilterTest_" + Guid.NewGuid();
        var dbRoot = new InMemoryDatabaseRoot();

        var services = new ServiceCollection();
        services.AddDbContext<TContext>(o => o.UseInMemoryDatabase(dbName, dbRoot));

        var builder = new TraxGraphQLBuilder(services);
        builder.AddDbContext<TContext>();
        if (caseInsensitive)
            builder.ConfigureFiltering(f => f.AddCaseInsensitiveStringOperations());
        customize?.Invoke(builder);
        var config = builder.Build();

        services.AddSingleton(config);
        services.AddSingleton<QueryModelTypeModule>();

        var graphql = services
            .AddGraphQLServer()
            .AddQueryType<CiTestRootQuery>()
            .AddType<CiDiscoverQueriesType>()
            .AddTypeModule<QueryModelTypeModule>()
            .AddSorting()
            .AddProjections();

        // Mirror GraphQLServiceExtensions: apply opt-in filter modules to the convention.
        if (config.FilterModules.Count > 0)
            graphql.AddFiltering(convention =>
            {
                convention.AddDefaults();
                foreach (var module in config.FilterModules)
                    module.Apply(convention);
            });
        else
            graphql.AddFiltering();

        var provider = services.BuildServiceProvider();

        if (seedData)
            await SeedAsync<TContext>(provider);

        return await provider
            .GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();
    }

    private static async Task SeedAsync<TContext>(IServiceProvider provider)
        where TContext : DbContext
    {
        switch (provider.GetRequiredService<TContext>())
        {
            case PeopleDbContext people:
                people.People.AddRange(
                    new Person
                    {
                        Id = 1,
                        Name = "Wally",
                        Nickname = "Champ",
                    },
                    new Person
                    {
                        Id = 2,
                        Name = "WALLACE",
                        Nickname = null,
                    },
                    new Person
                    {
                        Id = 3,
                        Name = "bob",
                        Nickname = "WALL-E",
                    }
                );
                await people.SaveChangesAsync();
                break;
            case MemberRefDbContext members:
                members.Members.AddRange(
                    new MemberRef
                    {
                        Id = 1,
                        Name = "Wally",
                        Secret = "s1",
                    },
                    new MemberRef
                    {
                        Id = 2,
                        Name = "WALLACE",
                        Secret = "s2",
                    },
                    new MemberRef
                    {
                        Id = 3,
                        Name = "bob",
                        Secret = "s3",
                    }
                );
                await members.SaveChangesAsync();
                break;
        }
    }

    #endregion

    #region Test query types — minimal RootQuery exposing `discover`

    public class CiTestRootQuery
    {
        public DiscoverQueries Discover() => new();
    }

    public class CiDiscoverQueriesType : ObjectType<DiscoverQueries>
    {
        protected override void Configure(IObjectTypeDescriptor<DiscoverQueries> descriptor)
        {
            descriptor.Name("DiscoverQueries");
        }
    }

    #endregion
}

#region Test entities, contexts, and filter types

[TraxQueryModel(Name = "people")]
public class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Nickname { get; set; }
}

public class PeopleDbContext(DbContextOptions<PeopleDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
}

public class PersonNameOnlyFilter : FilterInputType<Person>
{
    protected override void Configure(IFilterInputTypeDescriptor<Person> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(p => p.Name);
    }
}

public interface IMemberReference
{
    int Id { get; }
    string Name { get; }
}

[TraxQueryModel(Name = "memberRefs", ExposeAs = typeof(IMemberReference))]
public class MemberRef : IMemberReference
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    // Hidden by ExposeAs; present to prove the projection still works with the
    // case-insensitive convention.
    public string Secret { get; set; } = "";
}

public class MemberRefDbContext(DbContextOptions<MemberRefDbContext> options) : DbContext(options)
{
    public DbSet<MemberRef> Members => Set<MemberRef>();
}

[TraxQueryModel(Name = "noFilter", Filtering = false)]
public class NoFilterPerson
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class NoFilterDbContext(DbContextOptions<NoFilterDbContext> options) : DbContext(options)
{
    public DbSet<NoFilterPerson> NoFilter => Set<NoFilterPerson>();
}

#endregion
