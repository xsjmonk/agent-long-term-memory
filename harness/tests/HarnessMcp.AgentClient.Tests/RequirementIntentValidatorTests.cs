using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class RequirementIntentValidatorTests
{
    [Fact]
    public void rejects_missing_goal()
    {
        var v = new RequirementIntentValidator();
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "core",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: new[] { "might be async" },
            Complexity: "low");

        var result = v.Validate(intent);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Goal"));
    }

    [Fact]
    public void rejects_invalid_complexity()
    {
        var v = new RequirementIntentValidator();
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "core",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "do thing",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "extreme");

        var result = v.Validate(intent);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Complexity"));
    }

    [Fact]
    public void preserves_ambiguities_explicitly()
    {
        var v = new RequirementIntentValidator();
        var ambiguities = new[] { "unclear UI target", "maybe server-side" };

        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "core",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "do thing",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: ambiguities,
            Complexity: "low");

        var result = v.Validate(intent);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Ambiguities.Should().Equal(ambiguities);
    }
}

