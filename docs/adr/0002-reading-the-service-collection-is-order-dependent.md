---
authors: [Theauxm]
areas: [graphql, platform]
status: accepted
---

# Reading the service collection during registration is order-dependent

Inspecting the `IServiceCollection` inside a registration extension answers "is this
registered **yet**", not "will this be registered". Every existing site that does it is
enumerated with a count and a reason, and each is one of three safe kinds: an idempotency
guard, a precondition that throws, or a decision paired with a startup validator
([0001](./0001-a-misconfigured-host-fails-at-startup.md)).

Inspecting the collection is not banned. Being **silently** different because of order is.

## Status

**Accepted.**

## Why this is written down

Because it has shipped as a bug twice, from the same line of reasoning both times.

`AddTraxGraphQL()` picked the application services to bridge into HotChocolate's schema
container by reading the collection. A host calling `AddAuthentication()` afterwards got no
bridge, and the authentication interceptor then failed to activate on every request.

Worse, the same pattern chose the subscription interceptor. A scheme registered afterwards
was invisible, so none was wired, and HotChocolate's default accepted every
`connection_init`. **Subscriptions stopped authenticating while HTTP kept working**, because
`@authorize` lives on the schema and does not depend on registration order. Nothing warned.

## Consequences

**Prefer restructuring so the question disappears.** `ApplicationServiceBridge` registers a
forwarding factory that resolves on first use instead of asking whether a service exists
yet, which makes the ordering question moot rather than merely detected. That is better than
a validator, because there is nothing left to get wrong.

**Where order genuinely must matter, it fails at startup and the message names the call to
move.** An auth scheme must be registered before `AddTraxGraphQL()`, and the host refuses to
start otherwise.

**The census ratchets one way.** Adding a site means making it safe first and recording why.
Raising a count to silence the guard is itself the violation, and the guard says so.

## Exemplars

- `NoSilentRegistrationOrderDependenceTests` enumerates every introspection site in `src/`
  with the count it reads at, fails on a new file or a raised count, and separately fails
  when a reviewed entry names a file that no longer exists, so a cleaned-up site cannot leave
  a slot for a new one.
- [Registration Order](/docs/reference/registration-order) carries the same rule for users.

Not covered: the detection is a regex bound to a receiver named `services` or `Services`. A
collection held under any other name is invisible to the census, and so is a read performed
through a helper.

## Changelog

- **2026-09-11**: Recorded.
