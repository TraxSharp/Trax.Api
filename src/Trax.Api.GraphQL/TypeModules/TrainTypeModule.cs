using System.Text.Json;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Trax.Api.DTOs;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Api.GraphQL.TypeModules;

public class TrainTypeModule(ITrainDiscoveryService discoveryService) : TypeModule
{
    public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken
    )
    {
        var registrations = discoveryService.DiscoverTrains();
        var types = new List<ITypeSystemMember>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedInputTypes = new HashSet<Type>();
        var usedOutputTypes = new HashSet<Type>();
        var mutationFields = new List<(TrainRegistration Registration, string TrainName)>();
        var queryFields = new List<(TrainRegistration Registration, string TrainName)>();

        foreach (var reg in registrations)
        {
            if (!reg.IsQuery && !reg.IsMutation)
                continue;

            var trainName = reg.GraphQLName ?? DeriveTrainName(reg.ServiceTypeName);
            if (!usedNames.Add(trainName))
            {
                trainName = DeriveTrainName(reg.ServiceType.FullName ?? reg.ServiceTypeName);
                usedNames.Add(trainName);
            }

            if (usedInputTypes.Add(reg.InputType))
            {
                var inputObjectType = (ITypeSystemMember)
                    Activator.CreateInstance(
                        typeof(InputObjectType<>).MakeGenericType(reg.InputType)
                    )!;
                types.Add(inputObjectType);
            }

            if (HasTypedOutput(reg) && usedOutputTypes.Add(reg.OutputType))
            {
                var outputObjectType = (ITypeSystemMember)
                    Activator.CreateInstance(typeof(ObjectType<>).MakeGenericType(reg.OutputType))!;
                types.Add(outputObjectType);
            }

            if (reg.IsQuery)
            {
                queryFields.Add((reg, trainName));
            }
            else
            {
                // Create per-train response type eagerly for mutation trains with typed output
                if (HasTypedOutput(reg) && reg.GraphQLOperations.HasFlag(GraphQLOperation.Run))
                {
                    var responseTypeName = $"Run{trainName}Response";
                    var outputType = reg.OutputType;

                    types.Add(
                        new ObjectType(d =>
                        {
                            d.Name(responseTypeName);
                            d.Field("metadataId")
                                .Type<NonNullType<LongType>>()
                                .Resolve(ctx => ((RunTrainResult)ctx.Parent<object>()).MetadataId);
                            d.Field("output")
                                .Type(
                                    typeof(NonNullType<>).MakeGenericType(
                                        typeof(ObjectType<>).MakeGenericType(outputType)
                                    )
                                )
                                .Resolve(ctx => ((RunTrainResult)ctx.Parent<object>()).Output);
                        })
                    );
                }

                mutationFields.Add((reg, trainName));
            }
        }

        if (mutationFields.Count > 0)
        {
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DispatchMutations");
                    foreach (var (reg, name) in mutationFields)
                    {
                        if (reg.GraphQLOperations.HasFlag(GraphQLOperation.Run))
                            AddRunField(d, reg, name);

                        if (reg.GraphQLOperations.HasFlag(GraphQLOperation.Queue))
                            AddQueueField(d, reg, name);
                    }
                })
            );
        }

        if (queryFields.Count > 0)
        {
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DiscoverQueries");
                    foreach (var (reg, name) in queryFields)
                        AddQueryField(d, reg, name);
                })
            );
        }

        return new ValueTask<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    private static void AddQueryField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        var inputType = registration.InputType;
        var serviceTypeName = registration.ServiceTypeName;

        // Query fields use the derived name directly (no run/queue prefix)
        var fieldName = char.ToLowerInvariant(trainName[0]) + trainName[1..];

        var field = descriptor
            .Field(fieldName)
            .Argument(
                "input",
                a =>
                    a.Type(
                        typeof(NonNullType<>).MakeGenericType(
                            typeof(InputObjectType<>).MakeGenericType(inputType)
                        )
                    )
            );

        if (registration.GraphQLDescription is not null)
            field.Description(registration.GraphQLDescription);

        if (registration.GraphQLDeprecationReason is not null)
            field.Deprecated(registration.GraphQLDeprecationReason);

        if (HasTypedOutput(registration))
        {
            field
                .Type(
                    typeof(NonNullType<>).MakeGenericType(
                        typeof(ObjectType<>).MakeGenericType(registration.OutputType)
                    )
                )
                .Resolve(async ctx =>
                {
                    var input = ctx.ArgumentValue<object>("input");
                    var inputJson = JsonSerializer.Serialize(
                        input,
                        inputType,
                        TraxEffectConfiguration.StaticSystemJsonSerializerOptions
                    );

                    var executionService = ctx.Service<ITrainExecutionService>();
                    var ct = ctx.RequestAborted;
                    var result = await executionService.RunAsync(serviceTypeName, inputJson, ct);
                    return result.Output;
                });
        }
        else
        {
            field
                .Type<NonNullType<ObjectType<RunTrainResponse>>>()
                .Resolve(async ctx =>
                {
                    var input = ctx.ArgumentValue<object>("input");
                    var inputJson = JsonSerializer.Serialize(
                        input,
                        inputType,
                        TraxEffectConfiguration.StaticSystemJsonSerializerOptions
                    );

                    var executionService = ctx.Service<ITrainExecutionService>();
                    var ct = ctx.RequestAborted;
                    var result = await executionService.RunAsync(serviceTypeName, inputJson, ct);
                    return new RunTrainResponse(result.MetadataId);
                });
        }
    }

    private static void AddRunField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        var inputType = registration.InputType;
        var serviceTypeName = registration.ServiceTypeName;

        var field = descriptor
            .Field($"run{trainName}")
            .Argument(
                "input",
                a =>
                    a.Type(
                        typeof(NonNullType<>).MakeGenericType(
                            typeof(InputObjectType<>).MakeGenericType(inputType)
                        )
                    )
            );

        if (registration.GraphQLDescription is not null)
            field.Description($"Run: {registration.GraphQLDescription}");

        if (registration.GraphQLDeprecationReason is not null)
            field.Deprecated(registration.GraphQLDeprecationReason);

        if (HasTypedOutput(registration))
        {
            var responseTypeName = $"Run{trainName}Response";

            field
                .Type(new NamedTypeNode(responseTypeName))
                .Resolve(async ctx =>
                {
                    var input = ctx.ArgumentValue<object>("input");
                    var inputJson = JsonSerializer.Serialize(
                        input,
                        inputType,
                        TraxEffectConfiguration.StaticSystemJsonSerializerOptions
                    );

                    var executionService = ctx.Service<ITrainExecutionService>();
                    var ct = ctx.RequestAborted;
                    return await executionService.RunAsync(serviceTypeName, inputJson, ct);
                });
        }
        else
        {
            field
                .Type<NonNullType<ObjectType<RunTrainResponse>>>()
                .Resolve(async ctx =>
                {
                    var input = ctx.ArgumentValue<object>("input");
                    var inputJson = JsonSerializer.Serialize(
                        input,
                        inputType,
                        TraxEffectConfiguration.StaticSystemJsonSerializerOptions
                    );

                    var executionService = ctx.Service<ITrainExecutionService>();
                    var ct = ctx.RequestAborted;
                    var result = await executionService.RunAsync(serviceTypeName, inputJson, ct);
                    return new RunTrainResponse(result.MetadataId);
                });
        }
    }

    private static void AddQueueField(
        IObjectTypeDescriptor descriptor,
        TrainRegistration registration,
        string trainName
    )
    {
        var inputType = registration.InputType;
        var serviceTypeName = registration.ServiceTypeName;

        var field = descriptor
            .Field($"queue{trainName}")
            .Argument(
                "input",
                a =>
                    a.Type(
                        typeof(NonNullType<>).MakeGenericType(
                            typeof(InputObjectType<>).MakeGenericType(inputType)
                        )
                    )
            )
            .Argument("priority", a => a.Type<IntType>());

        if (registration.GraphQLDescription is not null)
            field.Description($"Queue: {registration.GraphQLDescription}");

        if (registration.GraphQLDeprecationReason is not null)
            field.Deprecated(registration.GraphQLDeprecationReason);

        field
            .Type<NonNullType<ObjectType<QueueTrainResponse>>>()
            .Resolve(async ctx =>
            {
                var input = ctx.ArgumentValue<object>("input");
                var inputJson = JsonSerializer.Serialize(
                    input,
                    inputType,
                    TraxEffectConfiguration.StaticSystemJsonSerializerOptions
                );

                var priority = ctx.ArgumentValue<int?>("priority") ?? 0;
                var executionService = ctx.Service<ITrainExecutionService>();
                var ct = ctx.RequestAborted;
                var result = await executionService.QueueAsync(
                    serviceTypeName,
                    inputJson,
                    priority,
                    ct
                );
                return new QueueTrainResponse(result.WorkQueueId, result.ExternalId);
            });
    }

    private static bool HasTypedOutput(TrainRegistration registration) =>
        registration.OutputType != typeof(LanguageExt.Unit)
        && registration.OutputType != typeof(object);

    private static string DeriveTrainName(string serviceTypeName)
    {
        var name = serviceTypeName;

        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name[1..];

        if (name.EndsWith("Train", StringComparison.Ordinal))
            name = name[..^5];

        return name;
    }
}
