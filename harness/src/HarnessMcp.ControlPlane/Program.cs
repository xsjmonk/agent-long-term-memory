using System.Text.Json;
using HarnessMcp.ControlPlane;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: harness <command> [options]");
    Console.Error.WriteLine("Commands: start-session, get-next-step, submit-step-result, get-session-status, cancel-session, describe-protocol");
    return 1;
}

var command = args[0].ToLowerInvariant();
var options = RuntimeOptions.Load();

try
{
    return command switch
    {
        "start-session" => StartSession(args, options),
        "get-next-step" => GetNextStep(args, options),
        "submit-step-result" => SubmitStepResult(args, options),
        "get-session-status" => GetSessionStatus(args, options),
        "cancel-session" => CancelSession(args, options),
        "describe-protocol" => DescribeProtocol(),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static int StartSession(string[] args, RuntimeOptions options)
{
    var rawTask = GetOptionValue(args, "--raw-task") ?? GetOptionValue(args, "-t");
    if (string.IsNullOrEmpty(rawTask))
    {
        Console.Error.WriteLine("--raw-task is required");
        return 1;
    }

    var sessionId = GetOptionValue(args, "--session-id") ?? GetOptionValue(args, "-s");
    var project = GetOptionValue(args, "--project");

    var request = new StartSessionRequest
    {
        RawTask = rawTask,
        SessionId = sessionId,
        Metadata = project != null ? new() { ["project"] = project } : null
    };

    var store = new SessionStore(options.SessionsRoot);
    var stateMachine = new HarnessStateMachine(store, options.Validation);
    var response = stateMachine.StartSession(request);

    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
    return response.Success ? 0 : 1;
}

static int GetNextStep(string[] args, RuntimeOptions options)
{
    var sessionId = GetRequiredOption(args, "--session-id", "-s");
    var store = new SessionStore(options.SessionsRoot);
    var stateMachine = new HarnessStateMachine(store, options.Validation);
    var response = stateMachine.GetNextStep(sessionId);

    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
    return response.Success ? 0 : 1;
}

static int SubmitStepResult(string[] args, RuntimeOptions options)
{
    var sessionId = GetRequiredOption(args, "--session-id", "-s");
    var action = GetRequiredOption(args, "--action", "-a");
    var artifactType = GetRequiredOption(args, "--artifact-type", "-t");
    var artifactFile = GetRequiredOption(args, "--artifact-file", "-f");

    if (!File.Exists(artifactFile))
    {
        Console.Error.WriteLine($"Artifact file not found: {artifactFile}");
        return 1;
    }

    var artifactJson = File.ReadAllText(artifactFile);
    object? artifactValue;
    try
    {
        artifactValue = JsonSerializer.Deserialize<object>(artifactJson);
    }
    catch
    {
        artifactValue = artifactJson;
    }

    var request = new SubmitStepResultRequest
    {
        SessionId = sessionId,
        CompletedAction = action,
        Artifact = new Artifact
        {
            ArtifactType = artifactType,
            Value = artifactValue
        }
    };

    var store = new SessionStore(options.SessionsRoot);
    var stateMachine = new HarnessStateMachine(store, options.Validation);
    var response = stateMachine.SubmitStepResult(request);

    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
    return response.Success ? 0 : 1;
}

static int GetSessionStatus(string[] args, RuntimeOptions options)
{
    var sessionId = GetRequiredOption(args, "--session-id", "-s");
    var store = new SessionStore(options.SessionsRoot);
    var stateMachine = new HarnessStateMachine(store, options.Validation);
    var response = stateMachine.GetSessionStatus(sessionId);

    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
    return response.Success ? 0 : 1;
}

static int CancelSession(string[] args, RuntimeOptions options)
{
    var sessionId = GetRequiredOption(args, "--session-id", "-s");
    var store = new SessionStore(options.SessionsRoot);
    var stateMachine = new HarnessStateMachine(store, options.Validation);
    var response = stateMachine.CancelSession(sessionId);

    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
    return response.Success ? 0 : 1;
}

static int DescribeProtocol()
{
    var description = new HarnessProtocolDescription();
    Console.WriteLine(JsonSerializer.Serialize(description, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    return 1;
}

static string? GetOptionValue(string[] args, string name)
{
    for (int i = 1; i < args.Length - 1; i++)
    {
        if (args[i] == name)
            return args[i + 1];
    }
    return null;
}

static string GetRequiredOption(string[] args, string name, string? shortName = null)
{
    var value = GetOptionValue(args, name) ?? (shortName != null ? GetOptionValue(args, shortName) : null);
    if (string.IsNullOrEmpty(value))
    {
        throw new ArgumentException($"{name} is required");
    }
    return value;
}