using System.Reflection;
using HotChocolate.Authorization;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.Subscriptions;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// The exposure census for fields a type extension adds to a type Trax owns. Finds them on the
/// merged object type, resolves what the parent gives them, and records every field that
/// inherits no gate and declares none.
/// </summary>
/// <remarks>
/// <para>
/// Reading the merged type rather than the registered <c>[ExtendObjectType]</c> classes is what
/// makes the census complete. <c>ConfigureSchema</c> hands the consumer the whole
/// <see cref="HotChocolate.Execution.Configuration.IRequestExecutorBuilder"/>, so a type
/// extension can reach the schema without passing through
/// <c>GraphQLConfiguration.AdditionalTypeExtensions</c>, and a census built from that list would
/// not know it exists. By <c>OnBeforeCompleteType</c> every extension has been merged onto its
/// target, however it was registered.
/// </para>
/// <para>
/// <see cref="QueryModelProjectionRequirementInterceptor"/> reads the same hook for the same
/// reason and tells extension fields apart the same way.
/// </para>
/// </remarks>
internal sealed class TypeExtensionExposureInterceptor : TypeInterceptor
{
    private readonly GraphQLConfiguration _configuration;
    private readonly TypeExtensionExposureReport _report;
    private readonly Dictionary<Type, TypeExtensionParentPosture> _postureByEntityType;

    /// <summary>
    /// The schema root types Trax registers. A field grafted onto one of these has no parent to
    /// inherit from, which is why a subscription added by
    /// <c>[ExtendObjectType(nameof(LifecycleSubscriptions))]</c> is covered without a special
    /// case.
    /// </summary>
    private static readonly HashSet<Type> RootTypes =
    [
        typeof(RootQuery),
        typeof(RootMutation),
        typeof(LifecycleSubscriptions),
    ];

    public TypeExtensionExposureInterceptor(
        GraphQLConfiguration configuration,
        TypeExtensionExposureReport report
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(report);

        _configuration = configuration;
        _report = report;

        // Last registration wins, matching how the rest of the pipeline resolves a duplicate
        // entity registration, rather than throwing here for a conflict the census did not cause.
        _postureByEntityType = new Dictionary<Type, TypeExtensionParentPosture>();
        foreach (var reg in configuration.ModelRegistrations)
        {
            _postureByEntityType[reg.EntityType] =
                reg.AuthorizeAttributes.Count > 0 ? TypeExtensionParentPosture.Gated
                : reg.AllowAnonymous ? TypeExtensionParentPosture.Anonymous
                : TypeExtensionParentPosture.NotExposed;
        }
    }

    /// <summary>
    /// Runs after type extensions are merged, so a consumer-supplied <c>[ExtendObjectType]</c>
    /// field is present on the object type by this point.
    /// </summary>
    public override void OnBeforeCompleteType(
        ITypeCompletionContext completionContext,
        TypeSystemConfiguration configuration
    )
    {
        if (configuration is not ObjectTypeConfiguration objectType)
            return;

        var parent = ResolveParentPosture(objectType.RuntimeType);

        foreach (var field in objectType.Fields)
        {
            // Introspection fields resolve off the type system, not off a parent instance.
            if (field.Name.StartsWith("__", StringComparison.Ordinal))
                continue;

            if (ExtensionMember(objectType.RuntimeType, field) is not { } member)
                continue;

            var fieldPath = $"{objectType.Name}.{field.Name}";

            // A class-level attribute on an [ExtendObjectType] lands on the type being extended.
            // On a root type that is the whole schema's posture, set by someone adding one field.
            if (RootTypes.Contains(objectType.RuntimeType) && DeclaresPosture(member.DeclaringType))
            {
                _report.Add(
                    new TypeExtensionExposureViolation(
                        fieldPath,
                        TypeExtensionExposureRule.BuildClassLevelMessage(
                            fieldPath,
                            member.DeclaringType!.FullName ?? member.DeclaringType.Name,
                            DescribeParent(objectType.RuntimeType, parent)
                        )
                    )
                );
                continue;
            }

            var violation = TypeExtensionExposureRule.Evaluate(
                parent,
                HasAuthorize(member, field),
                member.IsDefined(typeof(AllowAnonymousAttribute), inherit: true),
                _configuration.AuthorizationRequired
            );

            if (violation is ExposureViolation.None)
                continue;

            _report.Add(
                new TypeExtensionExposureViolation(
                    fieldPath,
                    TypeExtensionExposureRule.BuildMessage(
                        fieldPath,
                        $"{member.DeclaringType?.FullName}.{member.Name}",
                        DescribeParent(objectType.RuntimeType, parent),
                        violation
                    )
                )
            );
        }
    }

    /// <summary>
    /// The CLR member behind a field that a type extension contributed, or <c>null</c> when the
    /// field is not one.
    /// </summary>
    /// <remarks>
    /// A member declared by the object's own runtime type, or by a base or interface of it, is a
    /// natural member of the type and carries the type's own posture. A member declared anywhere
    /// else was bolted on. A field with no member at all is a resolver built inline (Trax's own
    /// <c>discover</c> and <c>operations</c> entry fields are built this way), which has no
    /// declaration site for an attribute and is gated by the code that wrote it.
    /// </remarks>
    private static MemberInfo? ExtensionMember(Type runtimeType, ObjectFieldConfiguration field)
    {
        var member = field.ResolverMember ?? field.Member;

        return member?.DeclaringType is { } declaring && !declaring.IsAssignableFrom(runtimeType)
            ? member
            : null;
    }

    /// <summary>
    /// Whether the field is gated. The attribute is the normal answer; the directive covers a
    /// field gated from a <c>ConfigureSchema</c> callback with <c>descriptor.Authorize()</c>,
    /// which leaves no attribute to read.
    /// </summary>
    private static bool HasAuthorize(MemberInfo member, ObjectFieldConfiguration field) =>
        member.IsDefined(typeof(AuthorizeAttribute), inherit: true)
        || (
            field.HasDirectives
            && field.Directives.Any(d =>
                d.Value is AuthorizeDirective
                || (
                    d.Type?.ToString()?.Contains("authorize", StringComparison.OrdinalIgnoreCase)
                    ?? false
                )
            )
        );

    private TypeExtensionParentPosture ResolveParentPosture(Type runtimeType)
    {
        // A root type has nothing above it, so a field on one inherits nothing.
        if (RootTypes.Contains(runtimeType))
            return TypeExtensionParentPosture.Anonymous;

        return _postureByEntityType.TryGetValue(runtimeType, out var posture)
            ? posture
            : TypeExtensionParentPosture.NotExposed;
    }

    /// <summary>
    /// Whether a type declares a posture on itself, which HotChocolate applies to the type being
    /// extended rather than to the extension's own fields.
    /// </summary>
    private static bool DeclaresPosture(Type? type) =>
        type is not null
        && (
            type.IsDefined(typeof(AuthorizeAttribute), inherit: true)
            || type.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
        );

    private static string DescribeParent(Type runtimeType, TypeExtensionParentPosture parent) =>
        parent is TypeExtensionParentPosture.Anonymous && RootTypes.Contains(runtimeType)
            ? $"the schema root type '{runtimeType.Name}'"
            : $"'{runtimeType.Name}', which is [TraxAllowAnonymous]";
}
