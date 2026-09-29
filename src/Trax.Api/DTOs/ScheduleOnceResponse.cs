namespace Trax.Api.DTOs;

/// <summary>
/// The manifest created by a one-time delayed schedule. Not returned by any current Trax API
/// surface; kept for compatibility and not intended for new code.
/// </summary>
/// <param name="ManifestId">The id of the manifest created.</param>
/// <param name="ExternalId">The manifest's external id.</param>
public record ScheduleOnceResponse(long ManifestId, string ExternalId);
