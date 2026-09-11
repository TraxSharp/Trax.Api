# Trax.Api

The GraphQL layer: schema generation over `[TraxQueryModel]` entities, authentication and
authorization, subscriptions, persisted operations, audit, and the cross-schema data
loaders. It sits above `Trax.Scheduler` and below `Trax.Dashboard` and `Trax.Samples`.

This file is the entry point. It routes; it does not restate the rules.

## Architecture decisions

`docs/adr/` records **why** things are the way they are. A documentation page says what the
rule is; an ADR says whether it is a deliberate constraint or an accident, so you can tell
which ones are safe to change. Read the relevant one before proposing to change a rule, and
if your work contradicts one, say so rather than silently overriding it.

| Working on | Read first |
| --- | --- |
| anything in an `AddTrax*` extension | [0002](./docs/adr/0002-reading-the-service-collection-is-order-dependent.md), before you read the `IServiceCollection` |
| a new host-configuration surface | [0001](./docs/adr/0001-a-misconfigured-host-fails-at-startup.md), it needs a startup validator |
| subscriptions or socket auth | both, in that order. This is where the silent failure happened |

Decisions binding more than one repo live in the central corpus at `Trax.Docs/adr/`, whose
index lists them by repo. Eight name `api`, including the canonical train name being the
interface FullName, exact version pinning, the dependency direction, and the three test
conventions. In a workspace checkout the index is at `../Trax.Docs/adr/README.md`; that path
does not resolve on GitHub, because it crosses a repository boundary.

## When your change makes a decision

Most changes do not. When one does (reversing it would cost something real, a future reader
would ask why it is like this, and there were genuine alternatives), it takes five steps and
the build enforces four. The `adr-guard` job runs on every pull request.

| | Step | Enforced |
| --- | --- | --- |
| 1 | Notice you made a decision, and write the ADR | no, this is the human step |
| 2 | Tag it `areas`, and add it to `docs/adr/README.md` | yes |
| 3 | Say where it stands in `## Status` and record it in `## Changelog` | yes |
| 4 | Give it `## Exemplars`: guards, `**Enforced elsewhere:**`, or `**Unenforced:**` with a reason | yes |
| 5 | Have each guard you named cite the ADR back, in its docstring and its failure message | yes |

Step 1 is the only one you have to remember, because no test can detect a decision you chose
not to record. The format is
[`.claude/skills/recording-decisions/ADR-FORMAT.md`](./.claude/skills/recording-decisions/ADR-FORMAT.md).

## Guards and validators

`tests/Trax.Api.Tests.Meta/` holds eleven convention guards. Ten are shared with other repos
and enforce workspace-wide rules; `NoSilentRegistrationOrderDependenceTests` is unique to
this repo and is the census behind [0002](./docs/adr/0002-reading-the-service-collection-is-order-dependent.md).

Six runtime validators fail the host at startup rather than at request time: five under
`src/Trax.Api.GraphQL/Startup/` and `TraxGraphQLAuthPolicyValidator` alongside the
authorization code. They are `IHostedService`s so they run after the container is complete.

The census (every guard credited to an ADR or explicitly opted out) is **not** switched on
here yet. Trax.Docs runs it over its own guards; this repo will once the shared copies carry
citations of the central ADRs they enforce.

## Running the tests

```bash
docker compose up -d          # Postgres and RabbitMQ for the integration suites
dotnet test
```
