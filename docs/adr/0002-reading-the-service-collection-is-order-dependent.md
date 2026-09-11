---
authors: [Theauxm]
areas: [graphql, platform]
status: accepted
---

# Reading the service collection during registration is order-dependent

Inspecting the `IServiceCollection` inside a registration extension answers "is this
registered **yet**", not "will this be registered". Every existing site that does it is
enumerated with a count and a reason. Three kinds are safe: an idempotency guard, a
precondition that throws, and a decision paired with a startup validator
([0001](./0001-a-misconfigured-host-fails-at-startup.md)).

Inspecting the collection is not banned. Being **silently** different because of order is.

**One site is none of the three, and is knowingly accepted.** `AddTraxGraphQL()` snapshots
train discovery to decide whether `RootQuery` and `RootMutation` get any fields. A
`[TraxQuery]` train registered afterwards is absent from the schema, with no validator and
no error. The code says so where it happens. It is tolerated because the established shape
is `AddTrax(...)` then `AddTraxGraphQL(...)`, with train registration finished inside or
before `AddTrax`, but it is a real exception to the rule above rather than a fourth safe
kind.

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
move.** The token-based schemes (`AddTraxJwtAuth`, `AddTraxApiKeyAuth`, `AddTraxJwtDispatcher`)
must be registered before `AddTraxGraphQL()` or the host refuses to start. Cookie-based OIDC
is exempt by design: it needs no socket interceptor, so registering it afterwards is fine.

**The census ratchets one way.** Adding a site means making it safe first and recording why.
Raising a count to silence the guard is itself the violation, and the guard says so.

## Exemplars

- `NoSilentRegistrationOrderDependenceTests` enumerates every introspection site in `src/`
  with the count it reads at, fails on a new file or a raised count, and separately fails
  when a reviewed entry names a file that no longer exists.
- [Registration Order](/docs/reference/registration-order) carries the same rule for users.

Not covered, and the holes are wider than the census suggests:

- **The verb list is closed.** It matches `Any`, `All`, `Count`, `Where`, `Select`, and
  `First`, `Last` and `Single` each with and without their `OrDefault` form. A `foreach` over
  the collection, `Contains`, `IndexOf`, `OfType<T>()` or an indexer is invisible, and
  `TrainDiscoveryService` uses exactly the `foreach` form.
- **The receiver must be named `services` or `Services`.** A field-backed `_services` or any
  other name is not seen. A helper taking a parameter named `services` *is* counted, which is
  how the train-discovery resolver got into the list.
- **It scans this repo's `src/` only.** The largest collection read that `AddTraxGraphQL()`
  triggers executes in `Trax.Mediator`, where no census exists.
- **`Trax.Dashboard` ships `AddTraxDashboard()` and reads the collection too**, and has no
  census at all. The policy is stated workspace-wide; the guard is not.

## Changelog

- **2026-09-11**: Corrected the verb list: eleven verbs, not eight, and the OrDefault forms are what catch two of the reviewed sites.
- **2026-09-11**: Corrected four claims an audit falsified: the train-discovery snapshot is a
  known exception rather than one of the three safe kinds, the stale check covers deleted files
  only, cookie-based OIDC is exempt from the ordering rule, and the census has more blind spots
  than the receiver name.
- **2026-09-11**: Recorded.
