using FluentAssertions;
using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Enums;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.Tests;

[TestFixture]
public class GraphQLTrainEventHandlerTests
{
    private ITopicEventSender _eventSender = null!;
    private GraphQLTrainEventHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _eventSender = Substitute.For<ITopicEventSender>();
        _handler = new GraphQLTrainEventHandler(_eventSender);
    }

    private static TrainLifecycleEventMessage CreateMessage(
        string eventType = "Completed",
        string trainState = "Completed",
        string trainName = "TestTrain",
        string? failureStep = null,
        string? failureReason = null
    ) =>
        new(
            MetadataId: 42,
            ExternalId: "ext-123",
            TrainName: trainName,
            TrainState: trainState,
            Timestamp: new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc),
            FailureStep: failureStep,
            FailureReason: failureReason,
            EventType: eventType,
            Executor: "RemoteWorker"
        );

    [Test]
    public async Task HandleAsync_Started_SendsToCorrectTopic()
    {
        var message = CreateMessage("Started", "InProgress");

        await _handler.HandleAsync(message, CancellationToken.None);

        await _eventSender
            .Received(1)
            .SendAsync(
                nameof(LifecycleSubscriptions.OnTrainStarted),
                Arg.Any<TrainLifecycleEvent>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task HandleAsync_Completed_SendsToCorrectTopic()
    {
        var message = CreateMessage("Completed", "Completed");

        await _handler.HandleAsync(message, CancellationToken.None);

        await _eventSender
            .Received(1)
            .SendAsync(
                nameof(LifecycleSubscriptions.OnTrainCompleted),
                Arg.Any<TrainLifecycleEvent>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task HandleAsync_Failed_SendsToCorrectTopic()
    {
        var message = CreateMessage(
            "Failed",
            "Failed",
            failureStep: "StepA",
            failureReason: "boom"
        );

        await _handler.HandleAsync(message, CancellationToken.None);

        await _eventSender
            .Received(1)
            .SendAsync(
                nameof(LifecycleSubscriptions.OnTrainFailed),
                Arg.Any<TrainLifecycleEvent>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task HandleAsync_Cancelled_SendsToCorrectTopic()
    {
        var message = CreateMessage("Cancelled", "Cancelled");

        await _handler.HandleAsync(message, CancellationToken.None);

        await _eventSender
            .Received(1)
            .SendAsync(
                nameof(LifecycleSubscriptions.OnTrainCancelled),
                Arg.Any<TrainLifecycleEvent>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task HandleAsync_UnknownEventType_DoesNotSend()
    {
        var message = CreateMessage("Unknown", "Completed");

        await _handler.HandleAsync(message, CancellationToken.None);

        await _eventSender
            .DidNotReceive()
            .SendAsync(
                Arg.Any<string>(),
                Arg.Any<TrainLifecycleEvent>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task HandleAsync_MapsFieldsCorrectly()
    {
        var message = CreateMessage("Completed", "Completed", "MySpecialTrain");
        TrainLifecycleEvent? capturedEvent = null;

        await _eventSender.SendAsync(
            Arg.Any<string>(),
            Arg.Do<TrainLifecycleEvent>(e => capturedEvent = e),
            Arg.Any<CancellationToken>()
        );

        await _handler.HandleAsync(message, CancellationToken.None);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.MetadataId.Should().Be(42);
        capturedEvent.ExternalId.Should().Be("ext-123");
        capturedEvent.TrainName.Should().Be("MySpecialTrain");
        capturedEvent.TrainState.Should().Be(TrainState.Completed);
        capturedEvent.Timestamp.Should().Be(new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task HandleAsync_MapsFailureDetailsCorrectly()
    {
        var message = CreateMessage(
            "Failed",
            "Failed",
            failureStep: "ProcessData",
            failureReason: "NullReferenceException"
        );
        TrainLifecycleEvent? capturedEvent = null;

        await _eventSender.SendAsync(
            Arg.Any<string>(),
            Arg.Do<TrainLifecycleEvent>(e => capturedEvent = e),
            Arg.Any<CancellationToken>()
        );

        await _handler.HandleAsync(message, CancellationToken.None);

        capturedEvent!.FailureStep.Should().Be("ProcessData");
        capturedEvent.FailureReason.Should().Be("NullReferenceException");
    }

    [Test]
    public async Task HandleAsync_InvalidTrainState_DefaultsToPending()
    {
        var message = CreateMessage("Completed", "InvalidState");
        TrainLifecycleEvent? capturedEvent = null;

        await _eventSender.SendAsync(
            Arg.Any<string>(),
            Arg.Do<TrainLifecycleEvent>(e => capturedEvent = e),
            Arg.Any<CancellationToken>()
        );

        await _handler.HandleAsync(message, CancellationToken.None);

        capturedEvent!.TrainState.Should().Be(TrainState.Pending);
    }

    [Test]
    public async Task HandleAsync_PassesCancellationToken()
    {
        var message = CreateMessage();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await _handler.HandleAsync(message, token);

        await _eventSender
            .Received(1)
            .SendAsync(Arg.Any<string>(), Arg.Any<TrainLifecycleEvent>(), token);
    }
}
