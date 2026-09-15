---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A type-extension field on an anonymous parent declares its own posture

Trax refuses to boot when an ungated endpoint exposes a train or a `[TraxQueryModel]` entity
declaring neither `[TraxAuthorize]` nor `[TraxAllowAnonymous]`. That census cannot see an
`[ExtendObjectType]` resolver, because such a field is added to the type by HotChocolate and
never passes back through Trax, so a resolver bolted onto a `[TraxAllowAnonymous]` entity is
public without anyone having decided it should be. A field whose parent is anonymous, or whose
parent is a root type, must carry HotChocolate's `[Authorize]` or `[AllowAnonymous]`, and the
host fails at startup when it carries neither.

Inheritance is what makes this narrow enough to ship. A field on a gated parent is already
behind that parent's `@authorize`, so it needs no marker, and a field on a type carrying
neither marker reaches the schema only inside a train's output, where the train's posture
governs. Only two rows have to declare: an anonymous parent, which inherits nothing, and a
root type, which has no parent at all.

## Status

**Accepted.**

## Considered options

**Requiring a marker on every type-extension field.** Uniform, and wrong: it fails the host
for every consumer on upgrade, in exchange for repeating a gate the parent type already
applies. The three-way rule keeps the blast radius to fields that really do inherit nothing,
and it stays honest, because the day a parent gains `[TraxAllowAnonymous]` its fields move
into the must-declare row and the host fails.

It is still a breaking change, and the size of it is worth stating: every hand-written field
on a root type has to declare, and that is not rare. Landing the census in Trax's own test
suite required a marker on nine of them. Each was deliberately anonymous, each took
`[AllowAnonymous]`, and the diff is the argument for the rule: nothing about those fields
said so before.

**Widening `[TraxAuthorize]` to `AttributeTargets.Method` and emitting the directive from a
`TypeInterceptor`.** The obvious shape, and the one to reject first. The attribute lives in
`Trax.Effect` and the enforcement in `Trax.Api`, so the widening releases first and there is a
window in which a consumer can write `[TraxAuthorize]` on a resolver and have it do nothing:
today that is `CS0592`, and a compile error is better than a silent no-op. HotChocolate's own
`[Authorize]` already applies to a method and already produces the directive, so nothing is
gained by owning the attribute that is not already available. Revisit it as ergonomics once
the census exists, never as the thing that closes the gap.

**Reading the census off `GraphQLConfiguration.AdditionalTypeExtensions`.** Cheaper, and blind
in the one direction that matters. `ConfigureSchema` hands the consumer the whole
`IRequestExecutorBuilder`, so a type extension can reach the schema without passing through
`AddTypeExtensions`, and a census built from the registration list would not know it exists.
Resolving fields off the merged object type sees them however they were registered, which is
the same escape hatch [0001](./0001-a-misconfigured-host-fails-at-startup.md) closes with a
post-build validator.

## Consequences

**It fails closed.** A field whose parent posture cannot be resolved is treated as an anonymous
parent and must declare. The cost is a consumer occasionally writing a marker on a field that
did not need one, which is the right direction for the error to point.

**`ExposureAuthorizationRule` does not change, and is reused with one deliberate exception.**
It is already a pure function of `(hasAuthorize, hasAllowAnonymous, endpointGated)`, so the
marker decision is made by the same code for a train, an entity and a resolver. The exception
is `AnonymousUnderGate`: an entity's `[TraxAllowAnonymous]` under a gated endpoint is reported,
because the entity gate is all or nothing and the endpoint already answered, while a field's
`[AllowAnonymous]` is not, because on a role-gated parent it still means something, namely any
authenticated caller rather than only the role.

**A class-level attribute on a type extension is reported, not honoured.** HotChocolate applies
one to the type being extended rather than to the fields the extension adds. On a
`[TraxAllowAnonymous]` entity that re-locks the entity, which the query-model schema validator
already refuses; on a root type it would set the posture of every operation in the schema, which
nothing else catches, so the census names it and says to move the attribute to the resolver.

**Return type is not an exemption.** A field returning a gated entity still has to declare.
What the field returns is not what the field does, and a census that reasons about return
types has to keep re-deriving a second type's posture to answer a question about the first.

## Exemplars

- `TypeExtensionExposureRuleTests` pins the decision matrix, all 24 combinations of parent
  posture, the two attributes and the endpoint posture, plus the divergence from
  `ExposureAuthorizationRule` described above.
- `TypeExtensionExposureTests` drives it through a real host: which fields the census sees, what
  it resolves their parent to be, and that the host refuses to start. It includes the case that
  justifies reading the merged type, a type extension registered through `ConfigureSchema`,
  which never reaches `AdditionalTypeExtensions`.
- [Architecture Guards](/docs/reference/architecture-guards) is the rule this produces.

Not covered:

- A field built from a lambda in a schema callback has no member to carry an attribute, so the
  census skips it. Trax's own `discover` and `operations` entry fields are built this way and
  are gated by the code that builds them.
- A resolver reached through a route Trax does not own at all, such as a minimal-API endpoint
  over the same data, is outside the schema and outside this rule.
- Nothing checks that a marker is the *right* one. `[AllowAnonymous]` on a field that should
  have been gated satisfies the census, which asks only that somebody decided.

## Changelog

- **2026-09-15**: Accepted and implemented. Recorded the blast radius honestly (root-type fields
  are not rare), the `AnonymousUnderGate` divergence, and the class-level-attribute case.
- **2026-09-15**: Recorded.
