using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.Contracts;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class ExecutionPlanValidatorTests
{
    private static BuildMemoryContextPackResponse EmptyContextPack() =>
        new BuildMemoryContextPackResponse(
            SchemaVersion: "1.0",
            Kind: "build_memory_context_pack",
            RequestId: "r",
            TaskId: "t",
            ContextPack: new ContextPackSectionDto(
                Decisions: Array.Empty<MergedKnowledgeItemDto>(),
                Constraints: Array.Empty<MergedKnowledgeItemDto>(),
                BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
                AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
                SimilarCases: Array.Empty<MergedKnowledgeItemDto>(),
                References: Array.Empty<MergedKnowledgeItemDto>(),
                Structures: Array.Empty<MergedKnowledgeItemDto>()),
            Diagnostics: new ContextPackDiagnosticsDto(
                ChunksProcessed: 0,
                DistinctKnowledgeItems: 0,
                RetrievalElapsedMs: 0,
                MergeElapsedMs: 0,
                AssemblyElapsedMs: 0,
                Warnings: Array.Empty<string>()));

    [Fact]
    public void rejects_plan_missing_constraints()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "tt",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "goal",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: new[] { "hc1" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var plan = new ExecutionPlan(
            SessionId: "s",
            TaskId: "t",
            Objective: "obj",
            Assumptions: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            AntiPatternsToAvoid: Array.Empty<string>(),
            Steps: new[]
            {
                new ExecutionStep(
                    StepNumber: 1,
                    Title: "Step",
                    Purpose: "Purpose",
                    Inputs: Array.Empty<string>(),
                    Actions: new[] { "Do it" },
                    Outputs: new[] { "out" },
                    AcceptanceChecks: new[] { "ok" },
                    SupportingMemoryIds: Array.Empty<string>(),
                    Notes: Array.Empty<string>())
            },
            ValidationChecks: Array.Empty<string>(),
            Deliverables: Array.Empty<string>(),
            OpenQuestions: Array.Empty<string>());

        var result = new ExecutionPlanValidator().Validate(plan, intent, EmptyContextPack());
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("drops required hard constraint"));
    }

    [Fact]
    public void rejects_plan_missing_acceptance_checks()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "tt",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "goal",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var plan = new ExecutionPlan(
            SessionId: "s",
            TaskId: "t",
            Objective: "obj",
            Assumptions: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            AntiPatternsToAvoid: Array.Empty<string>(),
            Steps: new[]
            {
                new ExecutionStep(
                    StepNumber: 1,
                    Title: "Step",
                    Purpose: "Purpose",
                    Inputs: Array.Empty<string>(),
                    Actions: new[] { "Do it" },
                    Outputs: new[] { "out" },
                    AcceptanceChecks: Array.Empty<string>(),
                    SupportingMemoryIds: Array.Empty<string>(),
                    Notes: Array.Empty<string>())
            },
            ValidationChecks: Array.Empty<string>(),
            Deliverables: Array.Empty<string>(),
            OpenQuestions: Array.Empty<string>());

        var result = new ExecutionPlanValidator().Validate(plan, intent, EmptyContextPack());
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("AcceptanceChecks"));
    }

    [Fact]
    public void rejects_plan_that_suggests_worker_memory_retrieval()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "tt",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "goal",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var plan = new ExecutionPlan(
            SessionId: "s",
            TaskId: "t",
            Objective: "obj",
            Assumptions: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            AntiPatternsToAvoid: Array.Empty<string>(),
            Steps: new[]
            {
                new ExecutionStep(
                    StepNumber: 1,
                    Title: "Step",
                    Purpose: "Purpose",
                    Inputs: Array.Empty<string>(),
                    Actions: new[] { "Retrieve long-term memory for details" },
                    Outputs: new[] { "out" },
                    AcceptanceChecks: new[] { "ok" },
                    SupportingMemoryIds: Array.Empty<string>(),
                    Notes: Array.Empty<string>())
            },
            ValidationChecks: Array.Empty<string>(),
            Deliverables: Array.Empty<string>(),
            OpenQuestions: Array.Empty<string>());

        var result = new ExecutionPlanValidator().Validate(plan, intent, EmptyContextPack());
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("suggests worker-side memory retrieval"));
    }
}

