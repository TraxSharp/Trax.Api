---
authors: [Theauxm]
areas: [graphql, platform]
status: accepted
---

# A misconfigured host fails at startup, not at request time

When a host wires Trax's GraphQL surface wrongly, it throws while starting, with a message
naming the call to change. It does not build a schema that looks fine and then fails on the
request that happens to touch the broken part.

Six validators enforce this, five under `Trax.Api.GraphQL/Startup/` and one alongside the
authorization code. Each is an `IHostedService`, so it runs after the container is complete
and sees the truth regardless of the order the host registered anything in.

## Status

**Accepted.**

## Considered options

**Throwing from the registration extension itself.** Simpler, and wrong for most of these
checks: `AddTraxGraphQL()` runs partway through the host's startup code, so anything it
concludes from the `IServiceCollection` is a statement about where the caller happens to be,
not about the finished container. See
[0002](./0002-reading-the-service-collection-is-order-dependent.md).

**Letting it fail at request time**, which is what happens by default. The failure arrives
masked as an "Unexpected Execution Error" on one operation, pointing at a resolver rather
than at the missing registration three files away in `Program.cs`. For the subscription
case it did not fail at all: HotChocolate's default interceptor accepted every
`connection_init`, so subscriptions silently stopped authenticating while HTTP kept working.

## Consequences

**The message is part of the contract.** A validator that throws without naming the call to
add is only marginally better than the request-time failure it replaced. `TraxOperationsServiceValidator`
names `ExposeOperationQueries` and the services to register; the subscription one names the
ordering to fix.

**Startup cost is paid for real checks.** `QueryModelAuthorizationSchemaValidator`
materialises the whole schema at boot to re-assert that every `[TraxAuthorize]` entity still
carries `@authorize` after a consumer's `ConfigureSchema` callback has run. That callback has
full builder access by design, so it is the one place Trax cannot control, and the check is
worth the boot time.

## Exemplars

- `TraxOperationsServiceValidatorTests` pins the fail-fast behaviour for the operations
  surface, including the message.
- `QueryModelAuthorizeSchemaInvariantE2ETests` builds a real host and asserts the directives
  survive the full pipeline, which is the defence-in-depth half.
- [Registration Order](/docs/reference/registration-order) is the user-facing statement of
  what order matters and what happens when it is wrong.

Not covered: nothing checks that a new piece of host configuration *has* a validator. Adding
a surface that can be half-wired and forgetting the check is invisible to the build, and that
is the failure mode this ADR is about.

## Changelog

- **2026-09-11**: Recorded.
