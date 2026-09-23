using FluentAssertions;
using Trax.Api.DTOs;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Api.Tests;

/// <summary>
/// The API has to expose the fields that explain why something is not running or how it failed.
/// Without them a staged entry is indistinguishable from an ordinary queued one, and a conflict
/// is indistinguishable from a validation error — both of which the dashboard reads from here.
/// </summary>
[TestFixture]
public class DiagnosticFieldExposureTests
{
    [Test]
    public void A_work_queue_summary_says_whether_the_entry_is_dispatchable()
    {
        var staged = new WorkQueueSummary(
            1,
            "ext",
            "Train",
            WorkQueueStatus.Queued,
            DateTime.UtcNow,
            null,
            null,
            0,
            0,
            null,
            null,
            null,
            null,
            ConfirmedAt: null,
            SubjectKey: "customer-1"
        );

        staged
            .ConfirmedAt.Should()
            .BeNull(
                "a staged entry reads as Queued, so this is the only thing that distinguishes it"
            );
        staged.SubjectKey.Should().Be("customer-1");
    }

    [Test]
    public void A_work_queue_summary_defaults_the_new_fields()
    {
        var entry = new WorkQueueSummary(
            1,
            "ext",
            "Train",
            WorkQueueStatus.Queued,
            DateTime.UtcNow,
            null,
            null,
            0,
            0,
            null,
            null,
            null,
            null
        );

        entry.ConfirmedAt.Should().BeNull();
        entry.SubjectKey.Should().BeNull();
    }

    [Test]
    public void An_execution_detail_carries_the_failure_classification()
    {
        var detail = new ExecutionDetail(
            Id: 1,
            ExternalId: "ext",
            Name: "Train",
            TrainState: TrainState.Failed,
            StartTime: DateTime.UtcNow,
            EndTime: DateTime.UtcNow,
            FailureJunction: "SomeJunction",
            FailureReason: "it broke",
            FailureException: nameof(InvalidOperationException),
            StackTrace: null,
            Input: null,
            Output: null,
            ManifestId: null,
            CancellationRequested: false,
            CurrentlyRunningJunction: null,
            JunctionStartedAt: null,
            HostName: null,
            HostEnvironment: null,
            HostInstanceId: null,
            FailureClass: FailureClass.Conflict
        );

        detail
            .FailureClass.Should()
            .Be(FailureClass.Conflict, "triage needs the kind, not just the message");
    }

    [Test]
    public void An_execution_summary_carries_the_failure_classification()
    {
        var summary = new ExecutionSummary(
            Id: 1,
            ExternalId: "ext",
            Name: "Train",
            TrainState: TrainState.Failed,
            StartTime: DateTime.UtcNow,
            EndTime: null,
            FailureJunction: null,
            FailureReason: null,
            FailureClass: FailureClass.Transient,
            ManifestId: null,
            CancellationRequested: false,
            HostName: null,
            HostEnvironment: null,
            HostInstanceId: null
        );

        summary
            .FailureClass.Should()
            .Be(FailureClass.Transient, "so a list can be filtered by failure kind");
    }
}
