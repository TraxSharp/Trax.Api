using System.Security.Claims;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Authenticates a subscription JWT against every scheme registered with <c>AddTraxJwtAuth</c>,
/// named or default, when no <c>AddTraxJwtDispatcher</c> routes between them.
/// </summary>
/// <remarks>
/// Each scheme validates the token against its own <see cref="JwtBearerOptions"/> (signature,
/// issuer, audience, lifetime, JWKS), in registration order; the first scheme that validates it
/// resolves the principal with that scheme's resolver. A token no scheme validates is rejected.
/// Only validation is repeated per scheme; the resolver, which is the host's code and may hit a
/// database, runs once. See
/// <c>docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md</c>.
/// </remarks>
internal sealed class TraxJwtSchemesSocketInterceptor(
    JwtResolverRegistry registry,
    IOptionsMonitor<JwtBearerOptions> optionsMonitor,
    TraxApplicationServices applicationServices,
    ILogger<TraxJwtSchemesSocketInterceptor> logger
) : DefaultSocketSessionInterceptor
{
    public override async ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken = default
    )
    {
        var payload = ConnectionInitPayloadReader.TryRead<ConnectionInitPayload>(
            connectionInitMessage
        );
        var token = payload?.AuthToken ?? payload?.Bearer;
        if (string.IsNullOrWhiteSpace(token))
            return ConnectionStatus.Reject("Missing auth token in connection_init payload.");

        foreach (var scheme in registry.SchemeNames)
        {
            Microsoft.IdentityModel.Tokens.TokenValidationResult validation;
            try
            {
                validation = await JwtSocketTokenValidator.ValidateAsync(
                    token,
                    optionsMonitor.Get(scheme),
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                logger.LogDebug(
                    ex,
                    "Subscription JWT did not validate for scheme {Scheme}.",
                    scheme
                );
                continue;
            }

            if (!validation.IsValid || validation.ClaimsIdentity is null)
                continue;

            return await ResolveAsync(
                session,
                connectionInitMessage,
                scheme,
                new JwtTokenInput(
                    new ClaimsPrincipal(validation.ClaimsIdentity),
                    validation.SecurityToken
                ),
                cancellationToken
            );
        }

        return ConnectionStatus.Reject("Invalid JWT.");
    }

    private async ValueTask<ConnectionStatus> ResolveAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        string scheme,
        JwtTokenInput input,
        CancellationToken cancellationToken
    )
    {
        TraxPrincipal? principal;
        try
        {
            await using var scope = applicationServices.Services.CreateAsyncScope();
            var resolver = registry.TryResolve(scheme, scope.ServiceProvider);
            if (resolver is null)
                return ConnectionStatus.Reject(
                    $"No principal resolver is registered for scheme '{scheme}'."
                );
            principal = await resolver.ResolveAsync(input, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Trax subscription JWT resolver threw for scheme {Scheme}.",
                scheme
            );
            return ConnectionStatus.Reject("JWT resolver failed.");
        }

        if (principal is null)
            return ConnectionStatus.Reject("JWT did not map to a known Trax principal.");

        if (session.Connection.HttpContext is { } httpContext)
            httpContext.User = principal.ToClaimsPrincipal(scheme);

        return await base.OnConnectAsync(session, connectionInitMessage, cancellationToken);
    }

    internal sealed record ConnectionInitPayload(string? AuthToken, string? Bearer);
}
