---
authors: [Theauxm]
areas: [graphql, auth]
status: accepted
---

# A run's step feed follows one run, with its train's visibility

`onJunctionEvent(metadataId: Long!)` streams the steps of one run (junctions starting and
ending, questions asked, tracks taken) from a host that called `AddJunctionEvents()`. The run is a
required argument: there is no feed of every run's steps. Each step reaches a subscriber exactly
when that run's train events would: a train's steps are published by the same rule as its events
(`[TraxBroadcast]`, or every train when the operations surface is exposed), and each subscriber's
view is the one `LifecycleSubscriptionAccess` already decides for `onTrain*`, including the refusal
of a subscriber who could see nothing. The recorded timeline is `operations.junctionRuns`, under
the operations gate, beside `operations.execution`.

## Status

**Accepted.**

## Considered options

**An optional `metadataId`, streaming every run when it is left out.** Steps are several times
as many events as train state changes, and every one would be fanned out to every subscriber, which
is the load at which the lifecycle feed already drops events (0032). Nothing a client builds needs
every step of every run: a list of runs is the train feed, and a run's detail is one run. Making
the argument optional later is additive; making it required later would break clients.

**One topic per run.** It would hand each event only to that run's subscribers, but every topic
keeps its sequence state in `LifecycleEventPublisher` for the life of the node, so a topic per run
grows without bound. One topic, filtered by run as each subscriber reads it, keeps the numbering
and loss reporting of 0032 unchanged.

**A visibility of its own for steps.** Answers can be as sensitive as outputs, so steps can carry
no less authorization than the train's events, and a separate rule would be a second place for the
two to drift apart. They share the rule and the code.

**The timeline outside the operations namespace, for broadcast subscribers.** A late broadcast
subscriber would then read its run's steps so far. It needs a per-run check of the train's posture
on a read, which no query has today; the timeline answers to the gate that answers for
`operations.execution`, and a broadcast client follows from the moment it subscribes.

## Consequences

**Outside the operations view a step loses host detail**, as a train event does: the decider's
type name, and a failure's exception type unless it is a `TrainException`.

**Outside the operations view a step carries no answer unless the host opts in.** `answer` and
`confidence` are null for a broadcast subscriber until the host calls
`AllowJunctionAnswersForBroadcastSubscribers()`, which sets
`TrainLifecycleStreamOptions.IncludeJunctionAnswersForBroadcastSubscribers`. An answer can reveal as
much about a run as its output, and a broadcast subscriber is anyone the train's posture admits, so
a default that published it would be an open default; SignalR's junction payload makes the same
choice (`WithJunctionAnswers()`). The subscriber still sees that a question was asked, its key and
the step that followed. The operations view always sees answers, and an answer to a
`[TraxSensitive]` question is never present for anyone, live or recorded, opt-in or not.

**Names on a decision track are answers too.** Which junctions ran after a `Switch` tells which
track it took, so withholding the answer alone hides nothing. Without the opt-in a broadcast
subscriber sees a junction that has a `trackPosition` (one after any routing step) named
`(withheld)`, with `nameWithheld` set. Trax.Core reports where a track starts but not where it
rejoins the chain, so every junction after a route counts as on its track: fail-closed, at the cost
of hiding the names of junctions that in fact run on every track. A name the run itself withheld,
after a `[TraxSensitive]` route, stays withheld in every view, live and recorded, through
`JunctionStep.From`, which the dashboard's timeline uses as well.

**A run's id must be positive.** `onJunctionEvent` and `operations.junctionRuns` refuse
`metadataId <= 0` with `TRAX_INVALID_ARGUMENT`. Zero is what an unsaved run carries, so following
it would have been a combined feed of every unsaved run rather than one run.

**A loss is reported whichever run it came from.** The topic carries every run's steps, so a gap
in another run's steps shows as a skip in `sequence`, as a hidden train's lost event does in 0032.

**A step is published on the run's path, and never holds it up past a bound.** The handler numbers
the event and hands it to the in-memory topic, waiting at most 250ms in all for its turn and for
the send. A step that cannot be published in time is dropped and its number skipped, so every
subscriber sees the loss as a jump in `sequence`, exactly as it sees a loss its own buffer caused
(0032). Blocking the run instead would let one slow subscriber stall every junction on the host.

## Exemplars

- `JunctionEventSubscriptionTests` pins the rule: a gated caller and an anonymous one are refused,
  a broadcast subscriber receives none of a train it cannot see and no host detail of one it can,
  only the subscribed run's steps arrive, a withheld answer stays null, a train that is not
  broadcast publishes nothing, `metadataId` is required, and a broadcast subscriber sees answers
  only with the host's opt-in while the operations view always does, the names of junctions on a
  track are withheld from it by default, a name the run withheld stays withheld in every view, and
  an id that is not positive is refused.
- `JunctionEventPublishBoundTests` pins the bound: a blocked send returns within it, and the
  next step shows the dropped one as a gap.
- `JunctionEventsE2ETests` holds it end to end with a real run over a real socket: a secret in the
  train's input, output and failure message reaches no subscriber and no timeline read, the
  sensitive answer is absent everywhere, and `operations.junctionRuns` is denied exactly when
  `operations.execution` is.
- [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) is the rule this produces.

Not covered: nothing checks that a client reads the timeline after subscribing rather than
before; that is the client's half.

## Changelog

- **2026-10-02**: Track junction names withheld with answers; ids must be positive; publish bounded.
- **2026-10-02**: Answers withheld from broadcast subscribers unless the host opts in.
- **2026-10-02**: Recorded.
