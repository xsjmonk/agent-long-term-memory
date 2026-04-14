using HarnessMcp.AgentClient.Cli;

try
{
    var argsList = args ?? Array.Empty<string>();
    if (argsList.Length == 0)
    {
        Console.Error.WriteLine("Usage: dotnet run --project src/HarnessMcp.AgentClient -- plan-task --task-file <path> --output-dir <dir> --mcp-base-url <url> --model-base-url <url> --model-name <name>");
        return 1;
    }

    var command = argsList[0];
    if (string.Equals(command, "plan-task", StringComparison.OrdinalIgnoreCase))
        return await PlanTaskCommand.RunAsync(argsList.Skip(1).ToArray(), CancellationToken.None).ConfigureAwait(false);

    if (string.Equals(command, "describe-protocol", StringComparison.OrdinalIgnoreCase))
    {
        Console.Out.WriteLine(DescribeProtocolCommand.GetProtocolJson());
        return 0;
    }

    Console.Error.WriteLine($"Unknown command: {command}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Fatal error: " + ex);
    return 1;
}

