using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Tests for the semantic planning-intent activation skill (04-harness-skill-activation.mdc).
/// Verifies that the activation skill defines semantic (not lexical-only) planning intent detection,
/// includes both activation and non-activation examples, and correctly relates to other skills.
///
/// These tests read the skill file content to verify it contains the required operational rules.
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class PlanningActivationTests
{
    private static string GetActivationSkillContent()
    {
        var path = GetRulePathOrFail("04-harness-skill-activation.mdc");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ActivationSkill_FileExists_WithCanonicalName()
    {
        var path = GetRulePathOrFail("04-harness-skill-activation.mdc");
        File.Exists(path).Should().BeTrue("canonical activation skill file must exist");
    }

    [Fact]
    public void ActivationSkill_DefinesSemanticActivation_NotLexicalOnly()
    {
        var content = GetActivationSkillContent();

        // Must explicitly define semantic (not keyword-only) activation
        content.Should().Contain("semantic",
            "activation must be semantic, not keyword-only");
        content.Should().Contain("planning intent",
            "must define the concept of planning intent");
    }

    [Fact]
    public void ActivationSkill_IncludesActivationExamples_DesignAndApproach()
    {
        var content = GetActivationSkillContent();

        // Must include examples of planning intent that trigger activation
        content.Should().Contain("design",
            "design requests must be listed as activation examples");
        content.Should().Contain("approach",
            "approach requests must be listed as activation examples");
    }

    [Fact]
    public void ActivationSkill_IncludesActivationExample_Migration()
    {
        var content = GetActivationSkillContent();
        content.Should().Contain("migrat",
            "migration planning must be an activation example");
    }

    [Fact]
    public void ActivationSkill_IncludesActivationExample_OutlineOrDecompose()
    {
        var content = GetActivationSkillContent();
        // Must mention outline, decompose, or step-by-step as activation triggers
        var hasOutline = content.Contains("outline") || content.Contains("decompose") || content.Contains("step-by-step");
        hasOutline.Should().BeTrue("outline/decompose/step-by-step requests must activate planning mode");
    }

    [Fact]
    public void ActivationSkill_IncludesNonActivationExample_TrivialTask()
    {
        var content = GetActivationSkillContent();
        content.Should().Contain("trivial",
            "trivial tasks must be listed as non-activation examples");
    }

    [Fact]
    public void ActivationSkill_IncludesNonActivationExample_CasualQuestion()
    {
        var content = GetActivationSkillContent();
        content.Should().Contain("casual",
            "casual questions must be listed as non-activation examples");
    }

    [Fact]
    public void ActivationSkill_IncludesNonActivationExample_ExplanationRequest()
    {
        var content = GetActivationSkillContent();
        // Must mention explanation or question as non-activation
        var hasExplanation = content.Contains("explanation") || content.Contains("question") || content.Contains("explain");
        hasExplanation.Should().BeTrue("explanation requests must not activate planning mode");
    }

    [Fact]
    public void ActivationSkill_IncludesNonActivationExample_DirectExecution()
    {
        var content = GetActivationSkillContent();
        // Must mention that executing an accepted WorkerExecutionPacket does not activate planning
        var hasExecutionMode = content.Contains("WorkerExecutionPacket") || content.Contains("execution mode") || content.Contains("accepted");
        hasExecutionMode.Should().BeTrue("direct execution of accepted packets must not activate planning mode");
    }

    [Fact]
    public void ActivationSkill_LinksToHarnessControlPlaneSkill()
    {
        var content = GetActivationSkillContent();
        content.Should().Contain("00-harness-control-plane",
            "when planning intent activates, must reference the harness control-plane skill");
    }

    [Fact]
    public void ActivationSkill_LinksToExecutionSkill()
    {
        var content = GetActivationSkillContent();
        content.Should().Contain("02-harness-execution",
            "when in execution mode, must reference the execution skill");
    }

    [Fact]
    public void ActivationSkill_ExplicitlyStatesToActivateForNonTrivialTasks()
    {
        var content = GetActivationSkillContent();
        content.Should().Contain("non-trivial",
            "must explicitly state that non-trivial tasks activate planning mode");
    }

    [Fact]
    public void ActivationSkill_StateBiasTowardActivationWhenUncertain()
    {
        var content = GetActivationSkillContent();
        // Must say to bias toward activation when uncertain about non-trivial tasks
        var hasBias = content.Contains("bias") || content.Contains("uncertain") || content.Contains("Bias");
        hasBias.Should().BeTrue("skill must say to bias toward activation when uncertain about non-trivial tasks");
    }

    [Fact]
    public void ActivationSkill_IsGenericAgent_NotProductSpecific()
    {
        var content = GetActivationSkillContent();
        // Must NOT be locked to a specific product name
        content.Should().NotContain("Cursor-only",
            "activation skill must be for generic agents, not product-specific");
        content.Should().NotContain("Claude-only",
            "activation skill must be for generic agents, not product-specific");
    }

    [Fact]
    public void ActivationSkill_DescribesRelationshipBetweenActivationAndPlanningSkill()
    {
        var content = GetActivationSkillContent();
        content.Should().Contain("activate",
            "must describe the activation decision and relationship to planning skill");
    }

    // --- Private helpers ---

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
                "Expected to find 'tests/HarnessMcp.ControlPlane.Tests' or 'src/HarnessMcp.ControlPlane'.");
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
