using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Tests for specific semantic activation scenarios defined in 04-harness-skill-activation.mdc.
///
/// These tests are STRONGER than "file contains the word 'semantic'":
/// they verify that the skill file explicitly handles concrete named scenarios —
/// the exact examples that a real agent would encounter.
///
/// Tests verify:
/// - Specific approach/design/strategy requests (no "plan" keyword) activate planning
/// - "That's the plan — let's do it" (has "plan" keyword) does NOT activate planning
/// - Rollout and migration planning examples are explicitly covered
/// - Ambiguous but non-trivial cases bias toward activation
/// - The cost reasoning for bias-toward-activation is present
/// - Skill explicitly distinguishes planning mode from execution mode
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class SemanticActivationScenarioTests
{
    private const string SkillFileName = "04-harness-skill-activation.mdc";

    // ==========================================
    // Positive activation scenario coverage
    // ==========================================

    [Fact]
    public void ActivationSkill_ExplicitlyCoversApproachRequest_WithoutPlanKeyword()
    {
        // "How should we approach this refactor?" is the canonical test case:
        // planning intent is clear but the word "plan" is not present.
        // The skill must explicitly demonstrate this case to prove it detects semantic intent.
        var content = ReadSkillOrFail();

        content.Should().Contain("approach",
            "skill must cover 'approach' as a positive activation signal");
        content.Should().Contain("refactor",
            "skill must explicitly name 'refactor' in at least one activation example — " +
            "this is the canonical no-keyword planning intent case");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyCoversStrategy_WithoutPlanKeyword()
    {
        var content = ReadSkillOrFail();

        content.Should().Contain("strategy",
            "skill must list 'strategy' as a positive semantic activation signal");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyCoversDesignRequest()
    {
        var content = ReadSkillOrFail();

        content.Should().Contain("design",
            "skill must list design requests as positive activation examples");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyCoversDecompositionRequest()
    {
        var content = ReadSkillOrFail();

        // Must list 'decompose' or 'outline' or similar structural-planning words
        var hasDecompose = content.Contains("decompose") || content.Contains("outline") || content.Contains("decomposition");
        hasDecompose.Should().BeTrue(
            "skill must include decompose/outline as semantic activation signals — " +
            "planning without the word 'plan' often uses these terms");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyCoversRolloutPlan()
    {
        var content = ReadSkillOrFail();

        content.Should().Contain("rollout",
            "skill must include rollout planning as a concrete activation example");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyCoversPreparingAnotherAgent()
    {
        var content = ReadSkillOrFail();

        // "Prepare another agent to implement the feature" is a planning request, not an execution request
        content.Should().Contain("another agent",
            "skill must cover 'prepare another agent' as a planning intent scenario — " +
            "this is a key use case for the harness planning loop");
    }

    // ==========================================
    // False-positive rejection (word "plan" present, not planning intent)
    // ==========================================

    [Fact]
    public void ActivationSkill_ExplicitlyCoversFalsePositive_PlanKeywordInExecutionContext()
    {
        var content = ReadSkillOrFail();

        // "That's the plan — let's do it" is the canonical false-positive:
        // the word "plan" is present but the intent is execution, not planning.
        // The skill must explicitly handle this case.
        content.Should().Contain("let's do it",
            "skill must explicitly show that \"That's the plan — let's do it\" does NOT activate planning — " +
            "this is the canonical lexical false-positive case that proves semantic > lexical matching");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyCoversFalsePositive_PlanLooksGoodProceed()
    {
        var content = ReadSkillOrFail();

        // "The plan looks good, proceed" is another execution directive that should NOT activate planning
        content.Should().Contain("looks good",
            "skill must handle 'The plan looks good, proceed' as a non-activation case — " +
            "this is an execution directive even though 'plan' appears");
    }

    // ==========================================
    // Non-activation scenario coverage
    // ==========================================

    [Fact]
    public void ActivationSkill_ExplicitlyCoversExplanationRequest_NonActivation()
    {
        var content = ReadSkillOrFail();

        // "What does this function do?" must not activate planning
        var hasExplanation = content.Contains("explanation") || content.Contains("explain") || content.Contains("What does");
        hasExplanation.Should().BeTrue(
            "skill must cover explanation/question requests as non-activation cases");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyCoversDirectExecution_NonActivation()
    {
        var content = ReadSkillOrFail();

        // Direct execution of an accepted WorkerExecutionPacket must NOT activate planning
        var hasDirectExecution = content.Contains("WorkerExecutionPacket") || content.Contains("Execute this accepted");
        hasDirectExecution.Should().BeTrue(
            "skill must explicitly state that direct execution of an accepted WorkerExecutionPacket " +
            "does not activate planning mode — it routes to 02-harness-execution.mdc instead");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyCoversRunTests_NonActivation()
    {
        var content = ReadSkillOrFail();

        content.Should().Contain("Run the tests",
            "skill must list 'Run the tests' as a non-activation example — direct commands don't need planning");
    }

    // ==========================================
    // Ambiguous case bias
    // ==========================================

    [Fact]
    public void ActivationSkill_BiasTowardActivation_IsExplicitlyStated()
    {
        var content = ReadSkillOrFail();

        content.ToLowerInvariant().Should().Contain("bias toward activation",
            "skill must explicitly state the bias-toward-activation rule for ambiguous non-trivial cases");
    }

    [Fact]
    public void ActivationSkill_CostRationale_IsPresent()
    {
        var content = ReadSkillOrFail();

        // The bias must be grounded in a cost-benefit argument
        content.ToLowerInvariant().Should().Contain("cost",
            "skill must explain the cost rationale behind bias-toward-activation — " +
            "the cost of unnecessary planning is low; cost of unplanned execution is high");
    }

    [Fact]
    public void ActivationSkill_AmbiguousNonTrivialCase_BiasTowardActivation()
    {
        var content = ReadSkillOrFail();

        content.ToLowerInvariant().Should().Contain("non-trivial",
            "skill must explicitly state that non-trivial ambiguous tasks should bias toward activation");
        content.ToLowerInvariant().Should().Contain("uncertain",
            "skill must use 'uncertain' to describe the ambiguous-case activation rule");
    }

    // ==========================================
    // Lexical-only rejection is explicit and concrete
    // ==========================================

    [Fact]
    public void ActivationSkill_LexicalOnlyRejection_IsConcreteNotAbstract()
    {
        var content = ReadSkillOrFail();

        // Must contain concrete wording about detecting a keyword being insufficient
        content.Should().Contain("Detecting the word",
            "skill must specifically call out 'Detecting the word' (a keyword) as insufficient — " +
            "this is stronger than just saying 'lexical matching is bad'");
    }

    [Fact]
    public void ActivationSkill_SemanticActivation_ExplainsWhatToInferInstead()
    {
        var content = ReadSkillOrFail();

        // After rejecting lexical matching, skill must say what TO use instead
        content.Should().Contain("meaning and context",
            "skill must instruct the agent to use 'meaning and context' instead of keyword detection");
    }

    // ==========================================
    // Mode distinction is explicit
    // ==========================================

    [Fact]
    public void ActivationSkill_PlanningModeAndExecutionMode_AreExplicitlyNamed()
    {
        var content = ReadSkillOrFail();

        content.ToLowerInvariant().Should().Contain("planning mode",
            "skill must explicitly name 'planning mode' as a distinct mode");
        content.ToLowerInvariant().Should().Contain("execution mode",
            "skill must explicitly name 'execution mode' as a distinct mode");
    }

    [Fact]
    public void ActivationSkill_ModesMustNotOverlap()
    {
        var content = ReadSkillOrFail();

        content.ToLowerInvariant().Should().Contain("must not run simultaneously",
            "skill must explicitly state that planning mode and execution mode must not run simultaneously");
    }

    // --- Helpers ---

    private static string ReadSkillOrFail()
    {
        var path = GetSkillPath(SkillFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required skill file '{SkillFileName}' not found at: {path}\n" +
                "Implementation is not complete until all skill files exist with correct content.");
        return File.ReadAllText(path);
    }

    private static string GetSkillPath(string fileName)
    {
        var root = FindHarnessRoot() ?? throw new DirectoryNotFoundException(
            "Could not locate harness repository root.");
        return Path.Combine(root, ".cursor", "rules", fileName);
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
