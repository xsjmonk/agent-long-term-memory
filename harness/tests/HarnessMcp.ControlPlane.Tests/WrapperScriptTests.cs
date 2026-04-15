using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

public class WrapperScriptTests
{
    [Fact]
    public void OldWrapper_IsDeprecated()
    {
        var harnessRoot = FindHarnessRoot();
        if (harnessRoot == null) return;

        var oldWrapperPath = Path.Combine(harnessRoot, "Scripts", "invoke-harness-plan.ps1");
        if (!File.Exists(oldWrapperPath))
        {
            return;
        }

        var content = File.ReadAllText(oldWrapperPath);
        content.Should().Contain("DEPRECATED");
    }

    [Fact]
    public void ControlPlaneWrapper_ReferencesControlPlaneExecutable()
    {
        var harnessRoot = FindHarnessRoot();
        if (harnessRoot == null) return;

        var wrapperPath = Path.Combine(harnessRoot, "Scripts", "invoke-harness-control-plane.ps1");
        if (!File.Exists(wrapperPath))
        {
            return;
        }

        var content = File.ReadAllText(wrapperPath);
        
        content.Should().Contain("HarnessMcp.ControlPlane");
        content.Should().NotContain("HarnessMcp.AgentClient");
    }

    [Fact]
    public void Wrapper_DoesNotHardcodeSingleDebugPath()
    {
        var harnessRoot = FindHarnessRoot();
        if (harnessRoot == null) return;

        var wrapperPath = Path.Combine(harnessRoot, "Scripts", "invoke-harness-control-plane.ps1");
        if (!File.Exists(wrapperPath))
        {
            return;
        }

        var content = File.ReadAllText(wrapperPath);
        
        content.Should().Contain("Release");
        content.Should().Contain("Debug");
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