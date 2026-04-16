using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Tests that skills are strong enough to enforce operational discipline.
/// These tests will FAIL if any skill is too weak — i.e., if it is written
/// as loose guidance rather than as a production-grade operational runbook.
///
/// Assertions check for:
/// - Imperative wording (ALWAYS, NEVER, MUST, FORBIDDEN) in the planning skill
/// - Hard-stop language in the failure skill
/// - Exact tool mapping and raw-response submission in the MCP skill
/// - No-retrieval and no-replanning rules in the execution skill
/// - Semantic activation and generic-agent wording in the activation skill
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class SkillOperationalStrengthTests
{
    // ==========================================
    // Planning Skill (00) Strength Tests
    // ==========================================

    [Fact]
    public void PlanningSkill_ContainsAlwaysWording()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("ALWAYS",
            "planning skill must contain ALWAYS imperatives — guidance without imperatives is too weak");
    }

    [Fact]
    public void PlanningSkill_ContainsNeverWording()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("NEVER",
            "planning skill must contain NEVER prohibitions — guidance without prohibitions is too weak");
    }

    [Fact]
    public void PlanningSkill_ContainsMustWording()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("MUST",
            "planning skill must contain MUST requirements — guidance without MUST is too weak");
    }

    [Fact]
    public void PlanningSkill_ContainsForbiddenWording()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("FORBIDDEN",
            "planning skill must contain FORBIDDEN prohibitions — guidance without FORBIDDEN is too weak");
    }

    [Fact]
    public void PlanningSkill_ContainsStageTable()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        // Must contain a stage table mapping stages to allowed actions
        content.Should().Contain("need_requirement_intent",
            "planning skill must contain the canonical stage table");
        content.Should().Contain("need_execution_plan",
            "planning skill stage table must include need_execution_plan");
        content.Should().Contain("need_worker_execution_packet",
            "planning skill stage table must include need_worker_execution_packet");
        // Verify it's formatted as a table (has | separators)
        content.Should().Contain("| `need_requirement_intent`",
            "stage table must use markdown table format");
    }

    [Fact]
    public void PlanningSkill_ContainsCompletionPresentationSection()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        content.ToLowerInvariant().Should().Contain("what to present",
            "planning skill must include a 'what to present at completion' section");
    }

    [Fact]
    public void PlanningSkill_ContainsResumeInstructions()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        content.ToLowerInvariant().Should().Contain("how to resume",
            "planning skill must include a 'how to resume' section");
        content.Should().Contain("get-next-step",
            "resume section must reference get-next-step command");
        content.Should().Contain("get-session-status",
            "resume section must reference get-session-status command");
    }

    [Fact]
    public void PlanningSkill_ContainsDoNotSkipBatchBypassSection()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        content.ToLowerInvariant().Should().Contain("do-not-skip",
            "planning skill must contain a do-not-skip / do-not-batch / do-not-bypass section");
    }

    [Fact]
    public void PlanningSkill_ContainsPlanningAndImplementationScenario()
    {
        var content = ReadSkillOrFail("00-harness-control-plane.mdc");
        // Must address the scenario where user asks for both planning and implementation
        content.ToLowerInvariant().Should().Contain("implementation in the same message",
            "planning skill must address what to do when user asks for planning AND implementation together");
    }

    // ==========================================
    // Failure Skill (01) Strength Tests
    // ==========================================

    [Fact]
    public void FailureSkill_ContainsHardStopLanguage()
    {
        var content = ReadSkillOrFail("01-harness-failure.mdc");
        content.Should().Contain("HARD STOP",
            "failure skill must contain 'HARD STOP' language — soft guidance is insufficient");
    }

    [Fact]
    public void FailureSkill_ContainsRepairByGuessingProhibition()
    {
        var content = ReadSkillOrFail("01-harness-failure.mdc");
        content.ToLowerInvariant().Should().Contain("repair by guessing",
            "failure skill must explicitly prohibit 'repair by guessing'");
    }

    [Fact]
    public void FailureSkill_DistinguishesThreeFailureTypes()
    {
        var content = ReadSkillOrFail("01-harness-failure.mdc");
        // Must describe all three failure types
        content.Should().Contain("Harness Validation Failure",
            "failure skill must distinguish Harness Validation Failure");
        content.Should().Contain("MCP Tool Call Failure",
            "failure skill must distinguish MCP Tool Call Failure");
        content.Should().Contain("Wrapper",
            "failure skill must distinguish Wrapper/Executable Invocation Failure");
    }

    [Fact]
    public void FailureSkill_ContainsHardStopChecklist()
    {
        var content = ReadSkillOrFail("01-harness-failure.mdc");
        content.ToLowerInvariant().Should().Contain("hard-stop checklist",
            "failure skill must contain a hard-stop checklist");
    }

    [Fact]
    public void FailureSkill_ContainsStopImmediately()
    {
        var content = ReadSkillOrFail("01-harness-failure.mdc");
        content.Should().Contain("STOP IMMEDIATELY",
            "failure skill must say STOP IMMEDIATELY on harness error");
    }

    [Fact]
    public void FailureSkill_ForbidsFreeFormFallback()
    {
        var content = ReadSkillOrFail("01-harness-failure.mdc");
        content.ToLowerInvariant().Should().Contain("free-form",
            "failure skill must forbid free-form planning fallback");
        content.Should().Contain("NEVER",
            "failure skill must use NEVER to prohibit invalid behaviors");
    }

    [Fact]
    public void FailureSkill_DistinguishesFourFailureTypes()
    {
        var content = ReadSkillOrFail("01-harness-failure.mdc");
        // Must address all 4 failure types: validation, MCP, wrapper, and session mismatch
        content.Should().Contain("Harness Validation Failure",
            "failure skill must distinguish Harness Validation Failure");
        content.Should().Contain("MCP Tool Call Failure",
            "failure skill must distinguish MCP Tool Call Failure");
        content.Should().Contain("Wrapper",
            "failure skill must distinguish Wrapper/Executable Invocation Failure");
        content.ToLowerInvariant().Should().Contain("mismatch",
            "failure skill must distinguish Session Resume/State Mismatch Failure as the 4th category");
    }

    [Fact]
    public void FailureSkill_ContainsSessionResumeMisMatchGuidance()
    {
        var content = ReadSkillOrFail("01-harness-failure.mdc");
        content.ToLowerInvariant().Should().Contain("get-session-status",
            "failure skill must instruct agent to call get-session-status when session state is uncertain");
        content.ToLowerInvariant().Should().Contain("get-next-step",
            "failure skill must reference get-next-step for re-syncing after mismatch");
    }

    // ==========================================
    // MCP Skill (03) Strength Tests
    // ==========================================

    [Fact]
    public void McpSkill_ContainsGenericAgentNote()
    {
        var content = ReadSkillOrFail("03-harness-mcp-tool-calling.mdc");
        content.ToLowerInvariant().Should().Contain("generic agent",
            "MCP skill must contain a generic-agent note stating it applies to any agent");
        content.ToLowerInvariant().Should().Contain("generic-agent note",
            "MCP skill must have an explicit 'Generic-Agent Note' section header");
    }

    [Fact]
    public void McpSkill_ContainsExactToolMapping()
    {
        var content = ReadSkillOrFail("03-harness-mcp-tool-calling.mdc");
        content.Should().Contain("retrieve_memory_by_chunks",
            "MCP skill must contain exact tool name retrieve_memory_by_chunks");
        content.Should().Contain("merge_retrieval_results",
            "MCP skill must contain exact tool name merge_retrieval_results");
        content.Should().Contain("build_memory_context_pack",
            "MCP skill must contain exact tool name build_memory_context_pack");
        content.Should().Contain("EXACTLY",
            "MCP skill must say to use the tool mapping EXACTLY");
    }

    [Fact]
    public void McpSkill_ContainsRawResponseSubmissionRule()
    {
        var content = ReadSkillOrFail("03-harness-mcp-tool-calling.mdc");
        content.Should().Contain("RAW",
            "MCP skill must require submitting the RAW MCP response");
        content.ToLowerInvariant().Should().Contain("payload.request",
            "MCP skill must require using payload.request exactly");
    }

    [Fact]
    public void McpSkill_ContainsNegativeExamples()
    {
        var content = ReadSkillOrFail("03-harness-mcp-tool-calling.mdc");
        content.ToLowerInvariant().Should().Contain("negative examples",
            "MCP skill must contain a 'Negative Examples' section showing invalid behaviors");
        content.Should().Contain("INVALID",
            "MCP skill negative examples must mark invalid behaviors with INVALID");
    }

    [Fact]
    public void McpSkill_AppliesRegardlessOfAgentType()
    {
        var content = ReadSkillOrFail("03-harness-mcp-tool-calling.mdc");
        // Must say it applies whether the agent is Claude, Cursor, or any other
        content.ToLowerInvariant().Should().Contain("claude",
            "MCP skill must mention Claude as an example agent type");
        content.ToLowerInvariant().Should().Contain("cursor",
            "MCP skill must mention Cursor as an example agent type");
    }

    [Fact]
    public void McpSkill_ContainsPositiveExamples()
    {
        var content = ReadSkillOrFail("03-harness-mcp-tool-calling.mdc");
        // Must have a positive examples section showing VALID behavior
        content.ToLowerInvariant().Should().Contain("positive examples",
            "MCP skill must contain a 'Positive Examples' section showing correct valid behavior");
        content.Should().Contain("CORRECT",
            "MCP skill positive examples must mark correct behaviors with CORRECT");
    }

    [Fact]
    public void McpSkill_PositiveExampleShowsExactToolNameUsage()
    {
        var content = ReadSkillOrFail("03-harness-mcp-tool-calling.mdc");
        // Positive examples must demonstrate using exact tool name
        content.ToLowerInvariant().Should().Contain("correct exact tool",
            "MCP positive examples must show correct exact tool name usage");
    }

    // ==========================================
    // Execution Skill (02) Strength Tests
    // ==========================================

    [Fact]
    public void ExecutionSkill_ContainsHandoffContract()
    {
        var content = ReadSkillOrFail("02-harness-execution.mdc");
        content.Should().Contain("Handoff Contract",
            "execution skill must contain a 'Handoff Contract' section defining the planning-to-execution transition");
    }

    [Fact]
    public void ExecutionSkill_ContainsNoRetrievalRule()
    {
        var content = ReadSkillOrFail("02-harness-execution.mdc");
        content.ToLowerInvariant().Should().Contain("long-term memory",
            "execution skill must prohibit long-term memory retrieval");
        content.Should().Contain("DO NOT Retrieve",
            "execution skill must explicitly say DO NOT Retrieve long-term memory");
        content.ToLowerInvariant().Should().Contain("forbidden by design",
            "execution skill must say memory retrieval is forbidden by design");
    }

    [Fact]
    public void ExecutionSkill_ContainsNoReplanningRule()
    {
        var content = ReadSkillOrFail("02-harness-execution.mdc");
        content.ToLowerInvariant().Should().Contain("do not replan",
            "execution skill must say DO NOT replan");
        content.ToLowerInvariant().Should().Contain("explicitly asks",
            "execution skill must state that re-planning is only allowed if user explicitly asks");
    }

    [Fact]
    public void ExecutionSkill_ContainsReportingRequirements()
    {
        var content = ReadSkillOrFail("02-harness-execution.mdc");
        content.ToLowerInvariant().Should().Contain("report assumptions",
            "execution skill must require reporting assumptions");
        content.ToLowerInvariant().Should().Contain("unresolved issues",
            "execution skill must require reporting unresolved issues");
    }

    [Fact]
    public void ExecutionSkill_RequiresOutputSections()
    {
        var content = ReadSkillOrFail("02-harness-execution.mdc");
        content.Should().Contain("per_step_results",
            "execution skill must require per_step_results output section");
        content.Should().Contain("final_deliverables",
            "execution skill must require final_deliverables output section");
        content.Should().Contain("validation_summary",
            "execution skill must require validation_summary output section");
    }

    // ==========================================
    // Activation Skill (04) Strength Tests
    // ==========================================

    [Fact]
    public void ActivationSkill_ContainsGenericAgentWording()
    {
        var content = ReadSkillOrFail("04-harness-skill-activation.mdc");
        content.ToLowerInvariant().Should().Contain("generic agent",
            "activation skill must state it applies to generic agents");
    }

    [Fact]
    public void ActivationSkill_ContainsSemanticActivation()
    {
        var content = ReadSkillOrFail("04-harness-skill-activation.mdc");
        content.Should().Contain("semantic",
            "activation skill must base activation on semantic intent");
        content.Should().Contain("planning intent",
            "activation skill must reference planning intent as the activation signal");
    }

    [Fact]
    public void ActivationSkill_ContainsDecisionHeuristic()
    {
        var content = ReadSkillOrFail("04-harness-skill-activation.mdc");
        content.ToLowerInvariant().Should().Contain("decision heuristic",
            "activation skill must contain a decision heuristic for ambiguous cases");
    }

    [Fact]
    public void ActivationSkill_ContainsActivationDecisionTable()
    {
        var content = ReadSkillOrFail("04-harness-skill-activation.mdc");
        content.ToLowerInvariant().Should().Contain("activation decision table",
            "activation skill must contain an activation decision table");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyRejectsLexicalOnlyMatching()
    {
        var content = ReadSkillOrFail("04-harness-skill-activation.mdc");
        content.ToLowerInvariant().Should().Contain("lexical",
            "activation skill must explicitly address lexical-only matching");
        content.ToLowerInvariant().Should().Contain("insufficient",
            "activation skill must say lexical-only matching is insufficient");
    }

    // --- Helpers ---

    private static string ReadSkillOrFail(string fileName)
    {
        var root = FindHarnessRoot() ?? throw new DirectoryNotFoundException(
            "Could not locate harness repository root.");
        var path = Path.Combine(root, "agent-rules", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required skill file '{fileName}' not found at: {path}\n" +
                "Implementation is not complete until all skill files exist.");
        return File.ReadAllText(path);
    }

    private static string? FindHarnessRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "tests", "HarnessMcp.ControlPlane.Tests")))
                return current;
            if (Directory.Exists(Path.Combine(current, "src", "HarnessMcp.ControlPlane")))
                return current;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }
}
