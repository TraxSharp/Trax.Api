namespace Trax.Api.DTOs;

/// <summary>
/// A delay to apply to a manifest trigger. Not accepted by any current Trax API surface; kept for
/// compatibility and not intended for new code.
/// </summary>
/// <param name="Delay">How long to wait before the manifest runs.</param>
public record TriggerDelayedRequest(TimeSpan Delay);
