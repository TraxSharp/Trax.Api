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

A caller without the `Admin` role gets a `TRAX_AUTHORIZATION` error. An exposed train that declares neither
`[TraxAuthorize]` nor `[TraxAllowAnonymous]` stops the host from starting.

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
