using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class AgentRuleContentTests
{
    private static readonly string RulesRoot = @"C:\Docs\工作笔记\Hackthon\2026\harness\.cursor\rules";

    private static string ReadRuleContent(string fileName) =>
        File.ReadAllText(Path.Combine(RulesRoot, fileName), System.Text.Encoding.UTF8);

    [Fact]
    public void planning_rule_exists()
    {
        var path = Path.Combine(RulesRoot, "00-harness-planning.mdc");
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void planning_rule_mandates_harness_first_behavior()
    {
        var path = Path.Combine(RulesRoot, "00-harness-planning.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));

        content.IndexOf("invoke", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("harness", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("non-trivial", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("before", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void planning_rule_prohibits_coding_before_harness()
    {
        var path = Path.Combine(RulesRoot, "00-harness-planning.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));

        var hasForbidden = content.IndexOf("must not", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           content.IndexOf("MUST NOT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           content.IndexOf("do not", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           content.IndexOf("MUST NOT", StringComparison.OrdinalIgnoreCase) >= 0;

        hasForbidden.Should().BeTrue("planning rule must contain explicit prohibitions");
    }

    [Fact]
    public void planning_rule_mentions_worker_packet()
    {
        var path = Path.Combine(RulesRoot, "00-harness-planning.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));
        content.IndexOf("worker", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void planning_rule_mentions_manifest()
    {
        var path = Path.Combine(RulesRoot, "00-harness-planning.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));
        content.IndexOf("manifest", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void failure_rule_exists()
    {
        var path = Path.Combine(RulesRoot, "01-harness-failure.mdc");
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void failure_rule_prohibits_fallback_manual_planning()
    {
        var path = Path.Combine(RulesRoot, "01-harness-failure.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));

        content.IndexOf("success", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("false", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("stop", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("error", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void failure_rule_requires_rerun()
    {
        var path = Path.Combine(RulesRoot, "01-harness-failure.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));
        content.IndexOf("rerun", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void execution_rule_exists()
    {
        var path = Path.Combine(RulesRoot, "02-harness-execution.mdc");
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void execution_rule_forbids_worker_side_memory_retrieval()
    {
        var path = Path.Combine(RulesRoot, "02-harness-execution.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));

        content.IndexOf("retrieve", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("memory", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("worker", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void execution_rule_forbids_scope_expansion()
    {
        var path = Path.Combine(RulesRoot, "02-harness-execution.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));

        content.IndexOf("expand", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("scope", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void execution_rule_forbids_replacement_plan()
    {
        var path = Path.Combine(RulesRoot, "02-harness-execution.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));

        content.IndexOf("replacement", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("plan", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void execution_rule_requires_three_output_sections()
    {
        var path = Path.Combine(RulesRoot, "02-harness-execution.mdc");
        var content = ReadRuleContent(Path.GetFileName(path));

        content.IndexOf("per-step", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("deliverable", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("validation", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void all_three_rule_files_are_present()
    {
        File.Exists(Path.Combine(RulesRoot, "00-harness-planning.mdc")).Should().BeTrue();
        File.Exists(Path.Combine(RulesRoot, "01-harness-failure.mdc")).Should().BeTrue();
        File.Exists(Path.Combine(RulesRoot, "02-harness-execution.mdc")).Should().BeTrue();
    }
}
