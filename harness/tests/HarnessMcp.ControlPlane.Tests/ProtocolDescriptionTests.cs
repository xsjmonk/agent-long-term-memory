using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

public class ProtocolDescriptionTests
{
    [Fact]
    public void DescribeProtocol_ListsAllCommands()
    {
        var description = new HarnessProtocolDescription();

        description.Commands.Should().Contain(c => c.Name == "start-session");
        description.Commands.Should().Contain(c => c.Name == "get-next-step");
        description.Commands.Should().Contain(c => c.Name == "submit-step-result");
        description.Commands.Should().Contain(c => c.Name == "get-session-status");
        description.Commands.Should().Contain(c => c.Name == "cancel-session");
        description.Commands.Should().Contain(c => c.Name == "describe-protocol");
    }

    [Fact]
    public void DescribeProtocol_ListsAllStages()
    {
        var description = new HarnessProtocolDescription();

        description.Stages.Should().Contain(s => s.Name == "need_requirement_intent");
        description.Stages.Should().Contain(s => s.Name == "need_retrieval_chunk_set");
        description.Stages.Should().Contain(s => s.Name == "need_retrieval_chunk_validation");
        description.Stages.Should().Contain(s => s.Name == "need_mcp_retrieve_memory_by_chunks");
        description.Stages.Should().Contain(s => s.Name == "need_mcp_merge_retrieval_results");
        description.Stages.Should().Contain(s => s.Name == "need_mcp_build_memory_context_pack");
        description.Stages.Should().Contain(s => s.Name == "need_execution_plan");
        description.Stages.Should().Contain(s => s.Name == "need_worker_execution_packet");
        description.Stages.Should().Contain(s => s.Name == "complete");
    }

    [Fact]
    public void DescribeProtocol_ContainsNoModelClientReference()
    {
        var description = new HarnessProtocolDescription();
        var json = JsonSerializer.Serialize(description);

        json.Should().NotContain("model");
        json.Should().NotContain("llm");
        json.Should().NotContain("openai");
        json.Should().NotContain("claude");
    }

    [Fact]
    public void StageNames_UseProtocolConvention()
    {
        Support.StageNameMapper.ToProtocolName(HarnessStage.NeedRequirementIntent).Should().Be("need_requirement_intent");
        Support.StageNameMapper.ToProtocolName(HarnessStage.NeedRetrievalChunkSet).Should().Be("need_retrieval_chunk_set");
        Support.StageNameMapper.ToProtocolName(HarnessStage.Complete).Should().Be("complete");
        Support.StageNameMapper.ToProtocolName(HarnessStage.Error).Should().Be("error");
    }
}