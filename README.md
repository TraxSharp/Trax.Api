# Trax.Api

[![Build](https://github.com/TraxSharp/Trax.Api/actions/workflows/nuget_release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax.Api/actions/workflows/nuget_release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Api.GraphQL)](https://www.nuget.org/packages/Trax.Api.GraphQL)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Api/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Api)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Api/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/api)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

Trax.Api generates a typed GraphQL API from your trains and `[TraxQueryModel]` entities, with fail-closed
authorization, request audit and typed clients. It builds on [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler)
and runs on HotChocolate; [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) builds on it.

## Install

```bash
dotnet add package Trax.Api.GraphQL
dotnet add package Trax.Effect.Data.Postgres   # storage for the run records and the work queue
dotnet add package Trax.Api.Auth.ApiKey        # or Trax.Api.Auth.Jwt, Trax.Api.Auth.Oidc
```

## Example

Adapted from the game server sample. Two attributes put the leaderboard train on the schema as a queued mutation:

```csharp
[TraxMutation(GraphQLOperation.Queue)]
[TraxAuthorize(Roles = "Admin")]
public class RecalculateLeaderboardTrain
    : ServiceTrain<RecalculateLeaderboardInput, RecalculateLeaderboardOutput>,
        IRecalculateLeaderboardTrain

// Program.cs: Trax, an auth scheme, and the GraphQL endpoint gated behind it
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly));

// Keys come from your secret manager, never from source control.
builder.Services.AddTraxApiKeyAuth(keys => keys
    .Add(builder.Configuration["ApiKeys:Admin"]!, id: "admin", "Admin"));
builder.Services.AddAuthorization();
builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();   // maps at /trax/graphql
```

```graphql
mutation {
  dispatch {
    recalculateLeaderboard(input: { region: "na" }) {
      workQueueId
    }
  }
}
```

A caller without the `Admin` role gets a `TRAX_AUTHORIZATION` error. For an admin the mutation writes a work queue row
and returns its id, and a scheduler on the same database runs the train. `GraphQLOperation.Run` runs it in-process.

## Authorization fails closed

Every `[TraxQuery]` or `[TraxMutation]` train and every `[TraxQueryModel]` entity has to say who may call it. On an
endpoint not gated by the builder's `RequireAuthorization()`, a surface with neither `[TraxAuthorize]` nor
`[TraxAllowAnonymous]` stops the host from starting, as does declaring both, or `[TraxAllowAnonymous]` under a gate.

| Declaration | Means |
|---|---|
| `[TraxAuthorize]` | Any authenticated caller |
| `[TraxAuthorize("PolicyName")]` | The caller must satisfy that ASP.NET Core policy |
| `[TraxAuthorize(Roles = "Admin,Ops")]` | The caller must hold at least one of the roles |
| `[TraxAllowAnonymous]` | Deliberately public, on an ungated endpoint only |

`RequireAuthorization()` with no argument accepts any scheme an `AddTrax*Auth` call registered; pass a policy name
to require one. The built-in `operations` namespace (health, executions, manifests, dead
letters, scheduler control) is off by default. `ExposeOperationQueries()` and `ExposeOperationMutations()` turn it
on, and the host then refuses to start unless it is gated by `RequireAuthorization()` or `GateOperations(policy, roles)`.

Trax auth is plumbing, not a security product, and comes with no warranty. Read
[SECURITY-DISCLAIMER.md](https://github.com/TraxSharp/Trax.Api/blob/main/SECURITY-DISCLAIMER.md) before you deploy.

## Packages

| Package | What it adds |
|---|---|
| [Trax.Api.GraphQL](https://www.nuget.org/packages/Trax.Api.GraphQL) | The schema generated from your trains and entities: queries, mutations, lifecycle subscriptions |
| [Trax.Api](https://www.nuget.org/packages/Trax.Api) | Train catalog, health checks and shared types |
| [Trax.Api.GraphQL.PersistedOperations](https://www.nuget.org/packages/Trax.Api.GraphQL.PersistedOperations) | Server-managed persisted operations (`UsePersistedOperations`), so shipped clients can be fixed server side |
| [Trax.Api.GraphQL.Audit](https://www.nuget.org/packages/Trax.Api.GraphQL.Audit) | A record of each request to your `ITraxAuditSink` (`AddAudit<TSink>()`), through a batched writer |
| [Trax.Api.GraphQL.Client](https://www.nuget.org/packages/Trax.Api.GraphQL.Client) | A GraphQL client whose hand-written queries are checked against the server schema at startup |
| [Trax.Api.GraphQL.Client.Typed](https://www.nuget.org/packages/Trax.Api.GraphQL.Client.Typed) | Queries whose selection set is a plain C# class |
| [Trax.Api.GraphQL.Client.Trax](https://www.nuget.org/packages/Trax.Api.GraphQL.Client.Trax) | Run GraphQL queries as junctions |
| [Trax.Api.GraphQL.Testing](https://www.nuget.org/packages/Trax.Api.GraphQL.Testing) | Architecture guards for cross-schema edges and data loaders |
| [Trax.Api.Auth](https://www.nuget.org/packages/Trax.Api.Auth) | `TraxPrincipal` and the claim types every scheme shares |
| [Trax.Api.Auth.ApiKey](https://www.nuget.org/packages/Trax.Api.Auth.ApiKey) | API keys in a header, hashed at startup |
| [Trax.Api.Auth.Jwt](https://www.nuget.org/packages/Trax.Api.Auth.Jwt) | JWT bearer tokens |
| [Trax.Api.Auth.Jwt.Cognito](https://www.nuget.org/packages/Trax.Api.Auth.Jwt.Cognito) | Amazon Cognito ID and access tokens (`UseCognito`) |
| [Trax.Api.Auth.Jwt.Cognito.Issuer](https://www.nuget.org/packages/Trax.Api.Auth.Jwt.Cognito.Issuer) | Mint Cognito-shaped tokens, with a refresh-token store |
| [Trax.Api.Auth.Jwt.Testing](https://www.nuget.org/packages/Trax.Api.Auth.Jwt.Testing) | A local JWKS server and token minters for tests |
| [Trax.Api.Auth.Oidc](https://www.nuget.org/packages/Trax.Api.Auth.Oidc) | OpenID Connect sign-in with PKCE and a session cookie |

## Where this fits

Trax is split into layers, one repo each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Api.**

| Repo | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| **[Trax.Api](https://github.com/TraxSharp/Trax.Api)** | **GraphQL generated from your trains, with authentication, audit and typed clients** |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Documentation

- [GraphQL API](https://traxsharp.net/docs/api): exposing trains and entities, run and queue modes ([reference](https://traxsharp.net/docs/sdk-reference/graphql-api))
- [API security](https://traxsharp.net/docs/api-security): every auth scheme, subscription auth, audit, hardening defaults ([reference](https://traxsharp.net/docs/sdk-reference/api-auth))
- [Authorization](https://traxsharp.net/docs/authorization): `[TraxAuthorize]` and `[TraxAllowAnonymous]` in detail
- [Persisted operations](https://traxsharp.net/docs/persisted-operations) and the [GraphQL client](https://traxsharp.net/docs/api-graphql-client)

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax.Api/blob/main/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax.Api/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
