using HotChocolate.Subscriptions;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Enums;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.GraphQL.Hooks;

/// <summary>
/// Handles cross-process train lifecycle events received via the broadcaster
/// and forwards them to HotChocolate's in-memory subscription transport.
/// This bridges the gap between worker processes (where trains execute)
/// and hub processes (where GraphQL subscriptions live).
/// </summary>
public class GraphQLTrainEventHandler : ITrainEventHandler
{
    private readonly ITopicEventSender _eventSender;
    private readonly ILogger<GraphQLTrainEventHandler>? _logger;

    public GraphQLTrainEventHandler(
        ITopicEventSender eventSender,
        ILogger<GraphQLTrainEventHandler>? logger = null
    )
    {
        _eventSender = eventSender;
        _logger = logger;
    }

    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        var topicName = message.EventType switch
        {
            "Started" => nameof(LifecycleSubscriptions.OnTrainStarted),
            "Completed" => nameof(LifecycleSubscriptions.OnTrainCompleted),
            "Failed" => nameof(LifecycleSubscriptions.OnTrainFailed),
            "Cancelled" => nameof(LifecycleSubscriptions.OnTrainCancelled),
            _ => null,
        };

        if (topicName is null)
        {
            _logger?.LogWarning(
                "Unknown event type {EventType} for train {TrainName}.",
                message.EventType,
                message.TrainName
            );
            return;
        }

        var lifecycleEvent = new TrainLifecycleEvent(
            MetadataId: message.MetadataId,
            ExternalId: message.ExternalId,
            TrainName: message.TrainName,
            TrainState: Enum.TryParse<TrainState>(message.TrainState, out var state)
                ? state
                : TrainState.Pending,
            Timestamp: message.Timestamp,
            FailureStep: message.FailureStep,
            FailureReason: message.FailureReason
        );

        await _eventSender.SendAsync(topicName, lifecycleEvent, ct);

        _logger?.LogDebug(
            "Forwarded remote {EventType} event for train {TrainName} ({ExternalId}) to GraphQL subscriptions.",
            message.EventType,
            message.TrainName,
            message.ExternalId
        );
    }
}
