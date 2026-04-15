using System;
using System.Buffers;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using HarnessMcp.Host.Aot;
using Microsoft.Extensions.Options;
using Xunit;

namespace HarnessMcp.Tests.Integration;

public sealed class MonitoringSignalRAotTests
{
    [Fact]
    public void SignalRJsonProtocol_WithSourceGeneratedPayloadResolver_SerializesStringArgument()
    {
        var hubProtocolOptions = new JsonHubProtocolOptions();

        hubProtocolOptions.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        hubProtocolOptions.PayloadSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        hubProtocolOptions.PayloadSerializerOptions.TypeInfoResolverChain.Clear();
        hubProtocolOptions.PayloadSerializerOptions.TypeInfoResolverChain.Add(SignalRJsonSerializerContext.Default);

        var protocol = new JsonHubProtocol(Options.Create(hubProtocolOptions));

        var invocation = new InvocationMessage("monitor", new object?[] { "{\"x\":1}" });

        var writer = new ArrayBufferWriter<byte>(1024);

        // Should not throw in AOT when reflection-based payload serialization is disabled.
        protocol.WriteMessage(invocation, writer);

        Assert.True(writer.WrittenCount > 0);
    }

    [Fact]
    public void SignalRJsonProtocol_WithoutResolver_ThrowsReflectionDisabledInvalidOperationException()
    {
        var hubProtocolOptions = new JsonHubProtocolOptions();
        hubProtocolOptions.PayloadSerializerOptions.TypeInfoResolverChain.Clear();

        var protocol = new JsonHubProtocol(Options.Create(hubProtocolOptions));

        var invocation = new InvocationMessage("monitor", new object?[] { "{\"x\":1}" });

        var writer = new ArrayBufferWriter<byte>(1024);

        var ex = Assert.ThrowsAny<Exception>(() => protocol.WriteMessage(invocation, writer));
        Assert.True(ex is InvalidOperationException || ex is NotSupportedException);

        // Different framework versions may throw either the AOT reflection-disabled exception
        // or a NotSupportedException about missing source-generated JsonTypeInfo.
        Assert.True(
            ex.Message.Contains("Reflection-based serialization", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("JsonTypeInfo metadata", StringComparison.OrdinalIgnoreCase),
            ex.Message);
    }
}

