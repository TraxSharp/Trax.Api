using HotChocolate.Subscriptions;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.GraphQL.Hooks;

/// <summary>
/// Forwards junction events, the steps of a run, to the <c>onJunctionEvent</c> subscription. Called
/// on the run's path for a run on this host, and by the broadcaster's receiver for a run on another.
/// </summary>
/// <remarks>
/// A step is forwarded exactly when the train's own events are (<see cref="LifecycleStreamRule"/>):
/// for a <c>[TraxBroadcast]</c> train, or for every train on a host exposing the operations surface.
/// Which subscribers receive it is decided per subscriber when it is read, by the same visibility as
/// the train's events. It publishes to the in-memory topic and returns, so it adds nothing slow to
/// the run's path.
/// </remarks>
internal sealed class GraphQLJunctionEventHandler(
    ITopicEventSender eventSender,
    LifecycleStreamRule rule,
    ILogger<GraphQLJunctionEventHandler>? logger = null
) : IJunctionEventHandler
{
    private readonly LifecycleEventPublisher _publisher = LifecycleEventPublisher.For(eventSender);

    /// <inheritdoc />
    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (!TrainLifecycleEventMessage.IsJunctionEvent(message.EventType))
            return;

        if (!rule.Publishes(message.TrainName))
            return;

        if (JunctionEvent.From(message) is not { } junctionEvent)
        {
            logger?.LogWarning(
                "Dropping {EventType} for train {TrainName}: not a junction event this API knows.",
                message.EventType,
                message.TrainName
            );
            return;
        }

        await _publisher
            .PublishAsync(nameof(LifecycleSubscriptions.OnJunctionEvent), junctionEvent, ct)
            .ConfigureAwait(false);
    }
}
