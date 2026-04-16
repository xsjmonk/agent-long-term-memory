using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

public class BuildScriptTests
{
    [Fact]
    public void BuildScript_TargetsControlPlaneOnly()
    {
        var harnessRoot = FindHarnessRoot();
        if (harnessRoot == null) return;

        var buildScriptPath = Path.Combine(harnessRoot, "Scripts", "build.ps1");
        if (!File.Exists(buildScriptPath)) return;

        var content = File.ReadAllText(buildScriptPath);
        
        content.Should().Contain("HarnessMcp.ControlPlane");
        content.Should().NotContain("HarnessMcp.AgentClient");
    }

    private string? FindHarnessRoot()
    {
        var current = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var testDir = Path.Combine(current, "tests", "HarnessMcp.ControlPlane.Tests");
            if (Directory.Exists(testDir))
                return current;

            var srcDir = Path.Combine(current, "src", "HarnessMcp.ControlPlane");
            if (Directory.Exists(srcDir))
                return current;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }
}