using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class SimilarCaseQueryTextBuilderTests
{
    [Fact]
    public void output_is_compact_deterministic_and_not_json()
    {
        var sig = new SimilarCaseSignature(
            TaskType: "ui-change",
            FeatureShape: "yearly-weighted-card",
            EngineChangeAllowed: false,
            LikelyLayers: new[] { "ui", "api" },
            RiskSignals: new[] { "placement-consistency", "async-refresh" },
            Complexity: "medium");

        var q1 = SimilarCaseQueryTextBuilder.Build(sig);
        var q2 = SimilarCaseQueryTextBuilder.Build(sig);

        q1.Should().Be(q2);
        q1.Should().NotContain("{");
        q1.Should().NotContain("}");
        q1.Should().NotContain("\"");
        q1.Should().Contain("ui-change");
        q1.Should().Contain("yearly-weighted-card");
        q1.Should().Contain("no-engine-change");
        q1.Should().Contain("ui");
        q1.Should().Contain("api");
        q1.Should().Contain("placement-consistency");
        q1.Should().Contain("async-refresh");
    }
}

