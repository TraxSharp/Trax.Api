# Decisions

Why a thing in `Trax.Api` is the way it is, which alternatives were weighed, and what each
cost. A documentation page tells you what the rule *is*; an ADR tells you whether it is a
deliberate constraint or an accident, so you can tell which ones are safe to change.

Read the relevant one before proposing to change a rule. If your work contradicts one, say
so rather than silently overriding it.

## Scope

**These bind `Trax.Api` only.** A decision binding more than one Trax repo lives in the
central corpus, at `Trax.Docs/adr/`, and declares which repos must obey it. These omit that
key, because the path already says it.

Numbering is per directory, so `0001` exists in several repos. Cite one of these as
`api/0001`.

## How they are checked

The `adr-guard` job in `.github/workflows/pull_request.yml` runs the guard published by
Trax.Docs against this directory on every pull request. The job needs nothing else cloned.
The local command does, because it builds the guard out of a workspace checkout:

```bash
dotnet run --project ../Trax.Docs/tools/Trax.Adr.Guard -- \
  --repo . \
  --known-areas graphql,auth,platform,testing \
  --census-root tests/Trax.Api.Tests.Meta
```

Without `--census-root` the census is never added to the run, so nothing named
`census/classified` is printed and the local command is weaker than the job above.

The format is `.claude/skills/recording-decisions/ADR-FORMAT.md`.

## By area

| Area | ADRs |
| --- | --- |
| `graphql` | [0001](./0001-a-misconfigured-host-fails-at-startup.md), [0002](./0002-reading-the-service-collection-is-order-dependent.md), [0003](./0003-a-type-extension-field-declares-its-own-posture.md), [0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md), [0006](./0006-one-socket-interceptor-composes-every-token-scheme.md), [0008](./0008-a-token-scheme-requires-a-credential-on-every-socket.md), [0009](./0009-the-endpoint-policy-applies-to-every-transport.md), [0010](./0010-a-scheme-policy-requires-its-scheme.md), [0011](./0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md), [0015](./0015-a-socket-runs-a-bounded-number-of-operations.md), [0022](./0022-a-socket-authenticates-through-the-schemes-handler.md) |
| `platform` | [0001](./0001-a-misconfigured-host-fails-at-startup.md), [0002](./0002-reading-the-service-collection-is-order-dependent.md) |
| `auth` | [0003](./0003-a-type-extension-field-declares-its-own-posture.md), [0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md), [0006](./0006-one-socket-interceptor-composes-every-token-scheme.md), [0008](./0008-a-token-scheme-requires-a-credential-on-every-socket.md), [0009](./0009-the-endpoint-policy-applies-to-every-transport.md), [0010](./0010-a-scheme-policy-requires-its-scheme.md), [0011](./0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md), [0022](./0022-a-socket-authenticates-through-the-schemes-handler.md), [0023](./0023-a-principal-id-is-qualified-by-its-scheme.md) |

## All of them

| # | Decision | Areas |
| --- | --- | --- |
| [0001](./0001-a-misconfigured-host-fails-at-startup.md) | A misconfigured host fails at startup, not at request time | graphql, platform |
| [0002](./0002-reading-the-service-collection-is-order-dependent.md) | Reading the service collection during registration is order-dependent | graphql, platform |
| [0003](./0003-a-type-extension-field-declares-its-own-posture.md) | A type-extension field on an anonymous parent declares its own posture | graphql, auth |
| [0004](./0004-the-operations-namespace-gates-independently-of-the-endpoint.md) | The operations namespace gates independently of the endpoint | graphql, auth |
| [0006](./0006-one-socket-interceptor-composes-every-token-scheme.md) | One socket interceptor composes every token scheme | graphql, auth |
| [0008](./0008-a-token-scheme-requires-a-credential-on-every-socket.md) | A token scheme requires a credential on every socket | graphql, auth |
| [0009](./0009-the-endpoint-policy-applies-to-every-transport.md) | The endpoint policy applies to every transport | graphql, auth |
| [0010](./0010-a-scheme-policy-requires-its-scheme.md) | A scheme policy requires its scheme | graphql, auth |
| [0011](./0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md) | Subscriptions carry the authorization of the data they stream | graphql, auth |
| [0015](./0015-a-socket-runs-a-bounded-number-of-operations.md) | A socket runs a bounded number of operations | graphql |
| [0022](./0022-a-socket-authenticates-through-the-schemes-handler.md) | A socket authenticates through the scheme's handler | graphql, auth |
| [0023](./0023-a-principal-id-is-qualified-by-its-scheme.md) | A principal id is qualified by its scheme | auth |
