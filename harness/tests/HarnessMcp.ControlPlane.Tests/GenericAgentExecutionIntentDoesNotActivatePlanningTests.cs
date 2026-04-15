using System.IO;
using System.Text.Json;
using FluentAssertions;
using HarnessMcp.ControlPlane;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Proves that execution intent and trivial requests do NOT activate the harness planning loop.
///
/// The planning activation gate lives in the skill (04-harness-skill-activation.mdc), not the harness.
/// The harness accepts ANY StartSession call — it is the skill's responsibility to prevent
/// planning activation for trivial or execution-intent requests.
///
/// Two-layer proof:
///   1. Skill-content: activation skill explicitly lists these as non-activation cases.
///   2. Harness contract: harness does NOT have an "activation check" — it starts any session —
///      so the skill is the sole gate, and the skill must be explicit about non-activation.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class GenericAgentExecutionIntentDoesNotActivatePlanningTests : IDisposable
{
    private readonly string _sessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _sm;
    private const string ActivationSkillFile = "04-harness-skill-activation.mdc";

    public GenericAgentExecutionIntentDoesNotActivatePlanningTests()
    {
        _sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-exec-intent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsRoot);
        _store = new SessionStore(_sessionsRoot);
        _sm = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // ==========================================
    // Layer 1: Skill explicitly covers non-activation scenarios
    // ==========================================

    [Fact]
    public void Skill_ExplicitlyCoversTrivialRename_AsNonActivation()
    {
        var content = ReadSkillOrFail();
        // "Rename this variable" is trivial — skill must say it does NOT activate planning
        content.Should().Contain("Rename this variable",
            "activation skill must explicitly list 'Rename this variable' as a non-activation case");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_PlanKeyword_InExecutionContext_AsNonActivation()
    {
        var content = ReadSkillOrFail();
        // "That's the plan — let's do it" has the word 'plan' but is execution intent
        content.Should().Contain("let's do it",
            "activation skill must explicitly show that \"That's the plan — let's do it\" does NOT activate planning — lexical false-positive case");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_PlanLooksGoodProceed_AsNonActivation()
    {
        var content = ReadSkillOrFail();
        // "The plan looks good, proceed" is an execution directive
        content.Should().Contain("looks good",
            "activation skill must cover 'The plan looks good, proceed' as a non-activation case — execution directive");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_ImplementItNow_AsNonActivation()
    {
        var content = ReadSkillOrFail();
        // "That's the plan — implement it now" → do NOT activate
        content.Should().Contain("implement it now",
            "activation skill must explicitly list 'implement it now' phrasing as a non-activation case");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_RunTests_AsNonActivation()
    {
        var content = ReadSkillOrFail();
        // "Run the tests" → do NOT activate (direct command)
        content.Should().Contain("Run the tests",
            "activation skill must list 'Run the tests' as a non-activation case — direct commands don't need planning");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_ExplanationRequests_AsNonActivation()
    {
        var content = ReadSkillOrFail();
        // "What does this function do?" → do NOT activate
        var hasExplanation = content.Contains("explanation") || content.Contains("explain") || content.Contains("What does");
        hasExplanation.Should().BeTrue(
            "activation skill must cover explanation/describe requests as non-activation cases");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_DirectExecutionOfAcceptedPacket_AsNonActivation()
    {
        var content = ReadSkillOrFail();
        // "Execute this accepted worker packet" → use 02-harness-execution.mdc, not planning mode
        var hasDirectExecution = content.Contains("WorkerExecutionPacket") || content.Contains("Execute this accepted");
        hasDirectExecution.Should().BeTrue(
            "activation skill must explicitly say that executing an accepted WorkerExecutionPacket routes to execution mode, not planning mode");
    }

    [Fact]
    public void Skill_UsesExplicit_DoNotActivate_Wording()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("do NOT activate",
            "activation skill must use explicit 'do NOT activate' wording to make non-activation cases unambiguous");
    }

    [Fact]
    public void Skill_ExplicitlyStates_LexicalKeyword_AloneIsInsufficient()
    {
        var content = ReadSkillOrFail();
        // Must warn against checking for the word "plan" alone
        content.Should().Contain("Detecting the word",
            "skill must specifically call out that detecting a keyword alone is insufficient — prevents lexical-only agents");
        content.ToLowerInvariant().Should().Contain("insufficient",
            "skill must say lexical-only detection is insufficient");
    }

    // ==========================================
    // Layer 2: Harness is not the activation gate
    // The skill is. These tests verify the harness contract that makes the skill
    // the sole gate: harness accepts any session, so the skill MUST be explicit.
    // ==========================================

    [Fact]
    public void Harness_IsNotTheActivationGate_ItAcceptsAnySession()
    {
        // The harness does NOT know about trivial vs. non-trivial tasks —
        // it accepts any StartSession call. This is BY DESIGN.
        // The skill is the sole gate that decides whether to start a session.
        var r = _sm.StartSession(new StartSessionRequest { RawTask = "Rename this variable" });

        // Harness WILL start it — but the skill would have prevented this call
        r.Success.Should().BeTrue(
            "harness accepts any task — the activation skill is the gate that prevents trivial tasks from reaching the harness");
        r.Stage.Should().Be("need_requirement_intent",
            "if a session IS started (skill gate bypassed), harness correctly begins at need_requirement_intent");
    }

    [Fact]
    public void Harness_RequiresSkill_AsGate_Because_ItHasNoActivationCheck()
    {
        // This test documents the architectural contract: harness has no activation check.
        // The harness ALWAYS starts at need_requirement_intent.
        // Therefore the activation skill MUST be the gate.
        var r1 = _sm.StartSession(new StartSessionRequest { RawTask = "Fix the typo in line 42" });
        var r2 = _sm.StartSession(new StartSessionRequest { RawTask = "Design the migration architecture" });

        // Both return the same starting stage — harness does not differentiate
        r1.Stage.Should().Be("need_requirement_intent");
        r2.Stage.Should().Be("need_requirement_intent");

        // The difference is: the skill says r1's intent should NOT start a session at all,
        // while r2's intent SHOULD. The harness enforces discipline AFTER activation.
        r1.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);
        r2.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);
    }

    [Fact]
    public void Skill_DistinguishesExecutionMode_ForAcceptedPackets()
    {
        var content = ReadSkillOrFail();
        // After planning completes, a "proceed" request routes to execution mode, not planning mode again
        content.ToLowerInvariant().Should().Contain("execution mode",
            "skill must distinguish execution mode from planning mode");
        content.Should().Contain("02-harness-execution",
            "skill must route accepted-packet execution to 02-harness-execution.mdc");
    }

    [Fact]
    public void Skill_PlanningAndExecutionModes_MustNotRunSimultaneously()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("must not run simultaneously",
            "skill must explicitly state that planning mode and execution mode must not overlap");
    }

    // --- Helpers ---

    private string ReadSkillOrFail()
    {
        var root = FindHarnessRoot() ?? throw new DirectoryNotFoundException("Could not locate harness root.");
        var path = Path.Combine(root, ".cursor", "rules", ActivationSkillFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Activation skill not found at: {path}");
        return File.ReadAllText(path);
    }

    private static string? FindHarnessRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "tests", "HarnessMcp.ControlPlane.Tests"))) return current;
            if (Directory.Exists(Path.Combine(current, "src", "HarnessMcp.ControlPlane"))) return current;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }

    public void Dispose()
    {
        if (Directory.Exists(_sessionsRoot)) Directory.Delete(_sessionsRoot, true);
    }
}
