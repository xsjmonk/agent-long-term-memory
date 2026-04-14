using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.Contracts;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class WorkerPacketBuilderTests
{
    [Fact]
    public void includes_forbidden_actions_and_required_output_sections_and_key_memory()
    {
        var requirementIntent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "tt",
            Domain: "ui",
            Module: "cards",
            Feature: "ajax",
            Goal: "goal",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: new[] { "hc" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: new[] { "ui" },
            RetrievalFocuses: new[] { "placement" },
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var step = new ExecutionStep(
            StepNumber: 1,
            Title: "Do",
            Purpose: "Purpose",
            Inputs: Array.Empty<string>(),
            Actions: new[] { "action" },
            Outputs: new[] { "out" },
            AcceptanceChecks: new[] { "ok" },
            SupportingMemoryIds: Array.Empty<string>(),
            Notes: Array.Empty<string>());

        var executionPlan = new ExecutionPlan(
            SessionId: "s",
            TaskId: "t",
            Objective: "obj",
            Assumptions: Array.Empty<string>(),
            HardConstraints: new[] { "hc" },
            AntiPatternsToAvoid: Array.Empty<string>(),
            Steps: new[] { step },
            ValidationChecks: Array.Empty<string>(),
            Deliverables: new[] { "d1" },
            OpenQuestions: Array.Empty<string>());

        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var hydrated = new[]
        {
            new GetKnowledgeItemResponse(
                SchemaVersion: "1.0",
                Kind: "get_knowledge_item",
                RequestId: "r",
                Item: new KnowledgeCandidateDto(
                    KnowledgeItemId: id,
                    RetrievalClass: RetrievalClass.Decision,
                    Title: "Decision title",
                    Summary: "s",
                    Details: null,
                    SemanticScore: 0,
                    LexicalScore: 0,
                    ScopeScore: 0,
                    AuthorityScore: 0,
                    CaseShapeScore: 0,
                    FinalScore: 9,
                    Authority: AuthorityLevel.Reviewed,
                    Status: KnowledgeStatus.Active,
                    Scopes: new ScopeFilterDto(
                        Domains: Array.Empty<string>(),
                        Modules: Array.Empty<string>(),
                        Features: Array.Empty<string>(),
                        Layers: Array.Empty<string>(),
                        Concerns: Array.Empty<string>(),
                        Repos: Array.Empty<string>(),
                        Services: Array.Empty<string>(),
                        Symbols: Array.Empty<string>()),
                    Labels: Array.Empty<string>(),
                    Tags: Array.Empty<string>(),
                    Evidence: Array.Empty<EvidenceDto>(),
                    SupportedByChunks: Array.Empty<string>(),
                    SupportedByQueryKinds: Array.Empty<string>()),
                Segments: Array.Empty<KnowledgeSegmentDto>(),
                Relations: Array.Empty<RelatedKnowledgeDto>())
        };

        var packet = new WorkerPacketBuilder().Build(requirementIntent, executionPlan, hydrated);

        packet.ForbiddenActions.Should().Contain("do not retrieve long-term memory independently");
        packet.ForbiddenActions.Should().Contain("do not expand scope beyond the listed steps");
        packet.RequiredOutputSections.Should().NotBeEmpty();
        packet.KeyMemory.Should().NotBeEmpty();
        packet.KeyMemory.Should().Contain(k => k.Contains(id.ToString("D")) && k.Contains("Decision title"));
    }
}

