using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Tests that verify the 04-harness-skill-activation.mdc skill file satisfies
/// all requirements for a generic-agent semantic planning-intent activation skill.
///
/// These tests will FAIL if:
/// - The skill uses lexical-only activation
/// - The skill does not mention generic agents
/// - The skill lacks positive/negative examples
/// - The skill lacks a bias-toward-activation rule
/// - The skill does not distinguish planning from execution mode
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class GenericAgentSkillActivationTests
{
    private const string SkillFileName = "04-harness-skill-activation.mdc";

    [Fact]
    public void ActivationSkill_FileExists_WithCanonicalName()
    {
        var path = GetSkillPath(SkillFileName);
        File.Exists(path).Should().BeTrue(
            $"canonical activation skill '{SkillFileName}' must exist at .cursor/rules/{SkillFileName}");
    }

    [Fact]
    public void ActivationSkill_ContainsGenericAgentWording()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must explicitly state it applies to generic agents
        content.ToLowerInvariant().Should().Contain("generic agent",
            "activation skill must state it applies to generic agents, not a specific product");
    }

    [Fact]
    public void ActivationSkill_ContainsSemanticPlanningIntentLanguage()
    {
        var content = ReadSkillOrFail(SkillFileName);
        content.Should().Contain("semantic",
            "activation must be based on semantic intent, not keyword matching");
        content.Should().Contain("planning intent",
            "activation skill must reference 'planning intent' as the activation signal");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyRejectsLexicalOnlyActivation()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must explicitly say lexical-only is insufficient
        content.ToLowerInvariant().Should().Contain("lexical",
            "skill must explicitly address that lexical-only matching is insufficient");
        content.ToLowerInvariant().Should().Contain("insufficient",
            "skill must state that lexical-only matching is insufficient");
    }

    [Fact]
    public void ActivationSkill_ContainsPositiveActivationExamples()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must list specific positive examples with "activate" signal
        content.Should().Contain("activate",
            "activation skill must contain positive examples that result in activation");
        // Must contain multiple scenarios
        content.Should().Contain("migration",
            "must include migration as a positive activation example");
        content.Should().Contain("approach",
            "must include approach request as a positive activation example");
        content.Should().Contain("design",
            "must include design request as a positive activation example");
    }

    [Fact]
    public void ActivationSkill_ContainsNonActivationExamples()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must describe what does NOT activate planning mode
        content.ToLowerInvariant().Should().Contain("trivial",
            "must describe trivial tasks as non-activation");
        content.ToLowerInvariant().Should().Contain("casual",
            "must describe casual questions as non-activation");
        content.Should().Contain("do NOT activate",
            "must use explicit 'do NOT activate' wording for non-activation cases");
    }

    [Fact]
    public void ActivationSkill_ContainsBiasTowardActivationRule()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must say to bias toward activation for non-trivial ambiguous tasks
        content.ToLowerInvariant().Should().Contain("bias toward activation",
            "skill must contain an explicit bias-toward-activation rule for ambiguous non-trivial tasks");
    }

    [Fact]
    public void ActivationSkill_ContainsDecisionHeuristic()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must contain a decision heuristic section for ambiguous cases
        content.ToLowerInvariant().Should().Contain("decision heuristic",
            "skill must contain a 'decision heuristic' section for ambiguous cases");
    }

    [Fact]
    public void ActivationSkill_DistinguishesPlanningModeFromExecutionMode()
    {
        var content = ReadSkillOrFail(SkillFileName);
        content.ToLowerInvariant().Should().Contain("planning mode",
            "skill must distinguish planning mode");
        content.ToLowerInvariant().Should().Contain("execution mode",
            "skill must distinguish execution mode");
    }

    [Fact]
    public void ActivationSkill_LinksToHarnessControlPlaneSkill()
    {
        var content = ReadSkillOrFail(SkillFileName);
        content.Should().Contain("00-harness-control-plane",
            "activation skill must explicitly link to 00-harness-control-plane.mdc");
    }

    [Fact]
    public void ActivationSkill_LinksToExecutionSkill()
    {
        var content = ReadSkillOrFail(SkillFileName);
        content.Should().Contain("02-harness-execution",
            "activation skill must link to 02-harness-execution.mdc for execution mode");
    }

    [Fact]
    public void ActivationSkill_ContainsActivationDecisionTable()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must contain a structured decision table
        content.ToLowerInvariant().Should().Contain("activation decision table",
            "skill must contain an activation decision table");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyStatesToActivateForNonTrivialTasks()
    {
        var content = ReadSkillOrFail(SkillFileName);
        content.ToLowerInvariant().Should().Contain("non-trivial",
            "skill must explicitly mention non-trivial tasks as an activation condition");
    }

    [Fact]
    public void ActivationSkill_StateBiasTowardActivationWhenUncertain()
    {
        var content = ReadSkillOrFail(SkillFileName);
        content.ToLowerInvariant().Should().Contain("uncertain",
            "skill must say to bias toward activation when uncertain about non-trivial tasks");
    }

    // ==========================================
    // Stronger specific-phrase tests
    // These go beyond "file contains the word X" to verify concrete examples
    // ==========================================

    [Fact]
    public void ActivationSkill_ContainsSpecificApproachWithoutPlanKeyword()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // The skill must include "How should we approach this refactor?" as a positive example
        // This is the canonical test case: planning intent without the word "plan"
        content.Should().Contain("approach",
            "activation skill must cover approach requests as planning intent");
        content.Should().Contain("refactor",
            "activation skill must show that 'How should we approach this refactor?' activates planning (no 'plan' keyword)");
    }

    [Fact]
    public void ActivationSkill_ContainsSpecificFalsePositiveRejection()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // The skill must explicitly address "That's the plan — let's do it" as NOT activating planning
        // This is the canonical false-positive: the word "plan" is present but intent is execution
        content.Should().Contain("let's do it",
            "activation skill must explicitly show that \"That's the plan — let's do it\" does NOT activate planning mode (lexical false-positive case)");
    }

    [Fact]
    public void ActivationSkill_ContainsMigrationPlanExample()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must contain a concrete migration plan example as a positive activation trigger
        content.Should().Contain("migration",
            "activation skill must include migration planning as a concrete positive example");
        content.Should().Contain("activate",
            "activation skill must use the word 'activate' alongside example descriptions");
    }

    [Fact]
    public void ActivationSkill_ContainsRolloutPlanExample()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must contain staged rollout / rollout plan as a positive activation example
        content.Should().Contain("rollout",
            "activation skill must include rollout planning as a concrete positive example");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyStatesLexicalCheckIsInsufficient()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // Must have a concrete statement that lexical-only is insufficient — not just mention the word
        content.Should().Contain("Detecting the word",
            "activation skill must explicitly call out that detecting a keyword alone is insufficient — not just mention 'lexical'");
        content.ToLowerInvariant().Should().Contain("insufficient",
            "activation skill must state that lexical-only detection is insufficient");
    }

    [Fact]
    public void ActivationSkill_ExplainsHighCostOfUnplannedExecution()
    {
        var content = ReadSkillOrFail(SkillFileName);
        // The bias-toward-activation rule must be grounded in a cost explanation
        content.ToLowerInvariant().Should().Contain("cost",
            "activation skill must explain the cost of unplanned execution as rationale for bias toward activation");
    }

    // --- Helpers ---

    private static string ReadSkillOrFail(string fileName)
    {
        var path = GetSkillPath(fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required skill file '{fileName}' not found at: {path}\n" +
                "Implementation is not complete until all skill files exist with correct content.");
        return File.ReadAllText(path);
    }

    private static string GetSkillPath(string fileName)
    {
        var root = FindHarnessRoot() ?? throw new DirectoryNotFoundException(
            "Could not locate harness repository root.");
        return Path.Combine(root, "agent-rules", fileName);
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
