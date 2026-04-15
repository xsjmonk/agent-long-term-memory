using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Tests that verify the canonical skill/rule files exist with the correct names,
/// contain the required operational rules, and fail loudly when files are missing or misnamed.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class RuleContentTests
{
    // Canonical rule file names — must match exactly
    private static readonly string[] CanonicalRuleFiles =
    {
        "00-harness-control-plane.mdc",
        "01-harness-failure.mdc",
        "02-harness-execution.mdc",
        "03-harness-mcp-tool-calling.mdc",
        "04-harness-skill-activation.mdc"
    };

    // Old (stale) file names that must NOT exist
    private static readonly string[] StaleRuleFiles =
    {
        "00-planning-mode-harness-control.mdc",
        "01-planning-mode-failure-handling.mdc",
        "02-execution-mode-worker-only.mdc",
        "03-planning-mode-mcp-stage.mdc"
    };

    [Fact]
    public void AllCanonicalRuleFiles_Exist()
    {
        var harnessRoot = GetHarnessRootOrFail();
        foreach (var fileName in CanonicalRuleFiles)
        {
            var path = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
            File.Exists(path).Should().BeTrue(
                $"canonical rule file '{fileName}' must exist at .cursor/rules/{fileName}");
        }
    }

    [Fact]
    public void StaleRuleFiles_DoNotExist()
    {
        var harnessRoot = GetHarnessRootOrFail();
        foreach (var fileName in StaleRuleFiles)
        {
            var path = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
            File.Exists(path).Should().BeFalse(
                $"stale rule file '{fileName}' must be removed — use canonical names only");
        }
    }

    [Fact]
    public void PlanningRule_RequiresHarnessFirstFlow()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain("start-session");
        content.Should().Contain("nextAction");
        content.Should().NotContain("HarnessRunManifest");
        content.Should().Contain("need_requirement_intent");
        content.Should().Contain("need_execution_plan");
    }

    [Fact]
    public void PlanningRule_RequiresExactWrapperPath()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain(@"Scripts\invoke-harness-control-plane.ps1");
        content.Should().Contain("submit-step-result");
        content.Should().NotContain("HarnessMcp.AgentClient");
    }

    [Fact]
    public void PlanningRule_RequiresSubmitAfterEveryStage()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain("submit-step-result");
        content.Should().Contain("submit it back");
    }

    [Fact]
    public void PlanningRule_RequiresStageOrder()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain("need_requirement_intent");
        content.Should().Contain("need_retrieval_chunk_set");
        content.Should().Contain("need_retrieval_chunk_validation");
        content.Should().Contain("need_execution_plan");
        content.Should().Contain("need_worker_execution_packet");
    }

    [Fact]
    public void PlanningRule_ContainsNeverSkipStages()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("NEVER skip stages");
    }

    [Fact]
    public void PlanningRule_ProhibitsMcpBeforeInstruction()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("MCP");
    }

    [Fact]
    public void FailureRule_IncludesHardStopOnError()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");

        content.Should().Contain("stop_with_error");
        content.Should().Contain("STOP IMMEDIATELY");
        content.Should().Contain("error");
    }

    [Fact]
    public void FailureRule_ForbidsFreeFormFallback()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");

        content.Should().Contain("stop_with_error");
        content.Should().Contain("NEVER");
    }

    [Fact]
    public void ExecutionRule_ForbidsWorkerSideRetrieval()
    {
        var content = ReadRuleOrFail("02-harness-execution.mdc");

        content.Should().Contain("long-term memory");
        content.Should().Contain("DO NOT Retrieve");
        content.Should().Contain("forbidden");
    }

    [Fact]
    public void ExecutionRule_ProhibitsIndependentMemoryRetrieval()
    {
        var content = ReadRuleOrFail("02-harness-execution.mdc");

        content.Should().Contain("long-term memory");
        content.Should().Contain("DO NOT Retrieve");
    }

    [Fact]
    public void McpToolRule_RequiresExactToolMapping()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");

        content.Should().Contain("retrieve_memory_by_chunks");
        content.Should().Contain("merge_retrieval_results");
        content.Should().Contain("build_memory_context_pack");
        content.Should().Contain("EXACTLY");
        content.Should().Contain("nextAction");
    }

    [Fact]
    public void McpToolRule_ProhibitsToolSubstitution()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");

        content.Should().Contain("NEVER");
        content.Should().Contain("retrieve_memory_by_chunks");
        content.Should().Contain("merge_retrieval_results");
        content.Should().Contain("build_memory_context_pack");
    }

    [Fact]
    public void ActivationSkill_DefinesSemanticActivation()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        content.Should().Contain("semantic");
        content.Should().Contain("planning intent");
        content.Should().Contain("activate");
    }

    [Fact]
    public void ActivationSkill_IncludesActivationExamples()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        // Must include activation scenario examples
        content.Should().Contain("design");
        content.Should().Contain("approach");
        content.Should().Contain("plan");
    }

    [Fact]
    public void ActivationSkill_IncludesNonActivationExamples()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        // Must describe what does NOT activate planning mode
        content.Should().Contain("trivial");
        content.Should().Contain("casual");
    }

    [Fact]
    public void ActivationSkill_ReferencesHarnessControlPlaneSKill()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        // Must tell agent to use the planning skill when activated
        content.Should().Contain("00-harness-control-plane");
    }

    [Fact]
    public void ActivationSkill_IsNotLexicalOnly()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        // Must explicitly say it is not just keyword matching
        content.Should().Contain("semantic");
        content.Should().Contain("not",
            "must state that activation is not keyword-only");
        content.Should().NotContain("if the user says the word 'plan'");
    }

    // ==========================================
    // Stronger Planning Skill (00) Tests
    // ==========================================

    [Fact]
    public void PlanningRule_ContainsAlwaysMustForbiddenImperatives()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("ALWAYS",
            "planning rule must contain ALWAYS imperatives — soft guidance is insufficient");
        content.Should().Contain("MUST",
            "planning rule must contain MUST requirements");
        content.Should().Contain("FORBIDDEN",
            "planning rule must contain FORBIDDEN prohibitions");
    }

    [Fact]
    public void PlanningRule_ContainsDoNotSkipSection()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");
        content.ToLowerInvariant().Should().Contain("do-not-skip",
            "planning rule must contain a do-not-skip/do-not-batch/do-not-bypass section");
    }

    [Fact]
    public void PlanningRule_ContainsResumeSection()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");
        content.ToLowerInvariant().Should().Contain("how to resume",
            "planning rule must contain a 'how to resume' section");
        content.Should().Contain("get-next-step",
            "resume section must reference get-next-step command");
        content.Should().Contain("get-session-status",
            "resume section must reference get-session-status command");
    }

    [Fact]
    public void PlanningRule_ContainsCompletionPresentationSection()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");
        content.ToLowerInvariant().Should().Contain("what to present",
            "planning rule must include a 'what to present at completion' section");
    }

    // ==========================================
    // Stronger Failure Skill (01) Tests
    // ==========================================

    [Fact]
    public void FailureRule_ContainsHardStop()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");
        content.Should().Contain("HARD STOP",
            "failure rule must contain 'HARD STOP' language — soft guidance is insufficient");
    }

    [Fact]
    public void FailureRule_DistinguishesThreeFailureTypes()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");
        content.Should().Contain("Harness Validation Failure",
            "failure rule must distinguish Harness Validation Failure");
        content.Should().Contain("MCP Tool Call Failure",
            "failure rule must distinguish MCP Tool Call Failure");
        content.Should().Contain("Wrapper",
            "failure rule must distinguish Wrapper/Executable Invocation Failure");
    }

    [Fact]
    public void FailureRule_ContainsRepairByGuessingProhibition()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");
        content.ToLowerInvariant().Should().Contain("repair by guessing",
            "failure rule must explicitly prohibit 'repair by guessing'");
    }

    [Fact]
    public void FailureRule_DistinguishesFourFailureTypes()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");
        content.Should().Contain("Harness Validation Failure",
            "failure rule must distinguish Harness Validation Failure (type 1)");
        content.Should().Contain("MCP Tool Call Failure",
            "failure rule must distinguish MCP Tool Call Failure (type 2)");
        content.Should().Contain("Wrapper",
            "failure rule must distinguish Wrapper/Executable Invocation Failure (type 3)");
        content.ToLowerInvariant().Should().Contain("mismatch",
            "failure rule must distinguish Session Resume/State Mismatch Failure (type 4)");
    }

    [Fact]
    public void FailureRule_ContainsSessionMismatchGuidance()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");
        content.ToLowerInvariant().Should().Contain("get-session-status",
            "failure rule must reference get-session-status for mismatch recovery");
    }

    [Fact]
    public void FailureRule_ContainsHardStopChecklist()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");
        content.ToLowerInvariant().Should().Contain("hard-stop checklist",
            "failure rule must contain a hard-stop checklist");
    }

    // ==========================================
    // Stronger MCP Skill (03) Tests
    // ==========================================

    [Fact]
    public void McpToolRule_ContainsGenericAgentNote()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");
        content.ToLowerInvariant().Should().Contain("generic agent",
            "MCP skill must state it applies to generic agents");
        content.ToLowerInvariant().Should().Contain("generic-agent note",
            "MCP skill must have an explicit 'Generic-Agent Note' section header");
    }

    [Fact]
    public void McpToolRule_ContainsNegativeExamples()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");
        content.ToLowerInvariant().Should().Contain("negative examples",
            "MCP skill must contain a 'Negative Examples' section showing invalid behaviors");
        content.Should().Contain("INVALID",
            "MCP skill negative examples must mark invalid behaviors with INVALID");
    }

    [Fact]
    public void McpToolRule_ContainsRawResponseRule()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");
        content.Should().Contain("RAW",
            "MCP skill must require submitting the RAW MCP response");
        content.ToLowerInvariant().Should().Contain("payload.request",
            "MCP skill must require using payload.request exactly");
    }

    [Fact]
    public void McpToolRule_ContainsPositiveExamples()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");
        content.ToLowerInvariant().Should().Contain("positive examples",
            "MCP skill must have a 'Positive Examples' section showing correct valid behavior");
        content.Should().Contain("CORRECT",
            "MCP skill positive examples must mark valid behaviors with CORRECT");
    }

    // ==========================================
    // Stronger Execution Skill (02) Tests
    // ==========================================

    [Fact]
    public void ExecutionRule_ContainsHandoffContract()
    {
        var content = ReadRuleOrFail("02-harness-execution.mdc");
        content.Should().Contain("Handoff Contract",
            "execution rule must contain a 'Handoff Contract' section defining the planning-to-execution transition");
    }

    [Fact]
    public void ExecutionRule_ContainsForbiddenByDesign()
    {
        var content = ReadRuleOrFail("02-harness-execution.mdc");
        content.ToLowerInvariant().Should().Contain("forbidden by design",
            "execution rule must say memory retrieval is 'forbidden by design'");
    }

    // ==========================================
    // Stronger Activation Skill (04) Tests
    // ==========================================

    [Fact]
    public void ActivationSkill_ContainsGenericAgentWording()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");
        content.ToLowerInvariant().Should().Contain("generic agent",
            "activation skill must state it applies to generic agents, not a specific product");
    }

    [Fact]
    public void ActivationSkill_ContainsDecisionHeuristic()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");
        content.ToLowerInvariant().Should().Contain("decision heuristic",
            "activation skill must contain a 'decision heuristic' section for ambiguous cases");
    }

    [Fact]
    public void ActivationSkill_ContainsActivationDecisionTable()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");
        content.ToLowerInvariant().Should().Contain("activation decision table",
            "activation skill must contain an activation decision table");
    }

    [Fact]
    public void ActivationSkill_ContainsBiasTowardActivation()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");
        content.ToLowerInvariant().Should().Contain("bias toward activation",
            "activation skill must state to bias toward activation for ambiguous non-trivial tasks");
    }

    // --- Private helpers ---

    private static string ReadRuleOrFail(string fileName)
    {
        var path = GetRulePathOrFail(fileName);
        return File.ReadAllText(path);
    }

    private static string GetRulePathOrFail(string fileName)
    {
        var harnessRoot = GetHarnessRootOrFail();
        var path = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required canonical rule file '{fileName}' not found at: {path}\n" +
                "Implementation is not complete until all canonical rule files exist.");
        return path;
    }

    private static string GetHarnessRootOrFail()
    {
        var root = FindHarnessRoot();
        if (root == null)
            throw new DirectoryNotFoundException(
                "Could not locate harness repository root. " +
                "Expected to find 'tests/HarnessMcp.ControlPlane.Tests' or 'src/HarnessMcp.ControlPlane' subdirectory.");
        return root;
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
