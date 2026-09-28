using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// The socket session interceptor <c>AddTraxGraphQL()</c> registers for every host. It
/// authenticates a subscription's <c>connection_init</c> with whichever token schemes the host
/// registered, API key, JWT, or both, by delegating each connection to
/// <see cref="TraxApiKeySocketInterceptor"/>, <see cref="TraxJwtDispatcherSocketInterceptor"/> or
/// <see cref="TraxJwtSocketInterceptor"/>.
/// </summary>
/// <remarks>
/// HotChocolate runs one <see cref="ISocketSessionInterceptor"/> per schema, so a host with both
/// schemes needs one interceptor that can serve either credential.
/// <para>
/// <b>Which schemes are active</b> is read from the completed application container on the first
/// connection, not from the <c>IServiceCollection</c> while <c>AddTraxGraphQL()</c> runs. A scheme
/// registered after <c>AddTraxGraphQL()</c> is therefore enforced like one registered before it.
/// </para>
/// <para>
/// <b>Routing.</b> With one scheme registered, every connection goes to it, exactly as the
/// scheme's own interceptor would handle it. With both, the payload decides: an <c>authToken</c>
/// that is a well-formed JWT goes to JWT validation and any other <c>authToken</c> to the
/// API-key resolver; with no <c>authToken</c>, an <c>apiKey</c> goes to the API-key resolver and
/// a <c>bearer</c> to JWT validation. A connection with none of the three is rejected. A
/// credential is validated by one strategy only; a JWT that fails validation is not retried as
/// an API key.
/// </para>
/// <para>
/// <b>No token scheme registered:</b> the connection is accepted, as HotChocolate's default
/// interceptor accepts it. A host with public subscriptions, or with cookie authentication
/// (which authenticates the WebSocket upgrade itself), is unaffected.
/// </para>
/// <para>
/// A host that supplies its own interceptor through
/// <c>ConfigureSchema(b =&gt; b.AddSocketSessionInterceptor&lt;T&gt;())</c> replaces this one.
/// </para>
/// </remarks>
public sealed class TraxCompositeSocketInterceptor : DefaultSocketSessionInterceptor
{
    private readonly TraxApplicationServices _applicationServices;
    private readonly Lazy<Strategies> _strategies;

    /// <summary>
    /// Creates the interceptor over the application container, which it reads for the registered
    /// schemes on first use.
    /// </summary>
    public TraxCompositeSocketInterceptor(TraxApplicationServices applicationServices)
    {
        ArgumentNullException.ThrowIfNull(applicationServices);
        _applicationServices = applicationServices;
        _strategies = new Lazy<Strategies>(
            DiscoverStrategies,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    /// <inheritdoc />
    public override ValueTask<ConnectionStatus> OnConnectAsync(
        ISocketSession session,
        IOperationMessagePayload connectionInitMessage,
        CancellationToken cancellationToken = default
    )
    {
        var strategies = _strategies.Value;

        if (strategies.ApiKey is null && strategies.Jwt is null)
            return base.OnConnectAsync(session, connectionInitMessage, cancellationToken);

        var target = SelectStrategy(strategies, connectionInitMessage);
        if (target is null)
            return new ValueTask<ConnectionStatus>(
                ConnectionStatus.Reject("Missing auth token in connection_init payload.")
            );

        return target.OnConnectAsync(session, connectionInitMessage, cancellationToken);
    }

    private static DefaultSocketSessionInterceptor? SelectStrategy(
        Strategies strategies,
        IOperationMessagePayload connectionInitMessage
    )
    {
        // One scheme: it reads the payload itself, and rejects a missing credential in its own
        // words, so the composite changes nothing for a single-scheme host.
        if (strategies.Jwt is null)
            return strategies.ApiKey;
        if (strategies.ApiKey is null)
            return strategies.Jwt;

        var payload = ConnectionInitPayloadReader.TryRead<ConnectionInitPayload>(
            connectionInitMessage
        );

        // The precedence matches each strategy's own read (authToken first), so the credential
        // routed on is the credential the strategy then validates.
        if (!string.IsNullOrWhiteSpace(payload?.AuthToken))
            return LooksLikeJwt(payload.AuthToken) ? strategies.Jwt : strategies.ApiKey;
        if (!string.IsNullOrWhiteSpace(payload?.ApiKey))
            return strategies.ApiKey;
        if (!string.IsNullOrWhiteSpace(payload?.Bearer))
            return strategies.Jwt;

        return null;
    }

    /// <summary>
    /// True when <paramref name="token"/> parses as a compact JWT, header included. Says nothing
    /// about validity: the JWT strategy validates it in full. Parsing the header, rather than
    /// matching the three-segment shape, keeps an API key that happens to contain two dots on
    /// the API-key path.
    /// </summary>
    internal static bool LooksLikeJwt(string token)
    {
        try
        {
            _ = new JsonWebToken(token);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException)
        {
            return false;
        }
    }

    private Strategies DiscoverStrategies()
    {
        var services = _applicationServices.Services;
        var isService = services.GetRequiredService<IServiceProviderIsService>();
        var loggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

        DefaultSocketSessionInterceptor? apiKey = null;
        if (isService.IsService(typeof(ITraxPrincipalResolver<string>)))
            apiKey = new TraxApiKeySocketInterceptor(
                _applicationServices,
                loggerFactory.CreateLogger<TraxApiKeySocketInterceptor>()
            );

        // A dispatcher routes every mapped JWT scheme by issuer, so it supersedes the
        // single-scheme interceptor when both are registered.
        DefaultSocketSessionInterceptor? jwt = null;
        if (isService.IsService(typeof(JwtDispatcherRuntime)))
            jwt = new TraxJwtDispatcherSocketInterceptor(
                services.GetRequiredService<JwtDispatcherRuntime>(),
                services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(),
                _applicationServices,
                loggerFactory.CreateLogger<TraxJwtDispatcherSocketInterceptor>()
            );
        else if (isService.IsService(typeof(ITraxPrincipalResolver<JwtTokenInput>)))
            jwt = new TraxJwtSocketInterceptor(
                services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>(),
                _applicationServices,
                loggerFactory.CreateLogger<TraxJwtSocketInterceptor>()
            );

        return new Strategies(apiKey, jwt);
    }

    private sealed record Strategies(
        DefaultSocketSessionInterceptor? ApiKey,
        DefaultSocketSessionInterceptor? Jwt
    );

    internal sealed record ConnectionInitPayload(string? AuthToken, string? ApiKey, string? Bearer);
}
