using System.Text.Json;
using System.Text.Json.Serialization;
using HarnessMcp.ControlPlane.Support;
using HarnessMcp.ControlPlane.Validators;

namespace HarnessMcp.ControlPlane;

public class StepResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "1.0";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("nextAction")]
    public string NextAction { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("inputContract")]
    public InputContract? InputContract { get; set; }

    [JsonPropertyName("instructions")]
    public List<string> Instructions { get; set; } = new();

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; } = HarnessJson.CreateObject(_ => { });

    [JsonPropertyName("completionArtifacts")]
    public CompletionArtifacts? CompletionArtifacts { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("acceptedArtifacts")]
    public List<string> AcceptedArtifacts { get; set; } = new();
}

public class CompletionArtifacts
{
    [JsonPropertyName("executionPlan")]
    public JsonElement? ExecutionPlan { get; set; }

    [JsonPropertyName("workerExecutionPacket")]
    public JsonElement? WorkerExecutionPacket { get; set; }
}

public class InputContract
{
    [JsonPropertyName("artifactType")]
    public string ArtifactType { get; set; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";
}

public class StartSessionRequest
{
    [JsonPropertyName("rawTask")]
    public string RawTask { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

public class SubmitStepResultRequest
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("completedAction")]
    public string CompletedAction { get; set; } = string.Empty;

    [JsonPropertyName("artifact")]
    public Artifact? Artifact { get; set; }
}

public class Artifact
{
    [JsonPropertyName("artifactType")]
    public string ArtifactType { get; set; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }
}

public class HarnessStateMachine
{
    private readonly SessionStore _sessionStore;
    private readonly ValidationOptions _validationOptions;

    private readonly RequirementIntentValidator _requirementIntentValidator;
    private readonly RetrievalChunkSetValidator _retrievalChunkSetValidator;
    private readonly ChunkQualityReportValidator _chunkQualityReportValidator;
    private readonly RetrieveMemoryByChunksResponseValidator _retrieveMemoryByChunksResponseValidator;
    private readonly MergeRetrievalResultsResponseValidator _mergeRetrievalResultsResponseValidator;
    private readonly BuildMemoryContextPackResponseValidator _buildMemoryContextPackResponseValidator;
    private readonly ExecutionPlanValidator _executionPlanValidator;
    private readonly WorkerExecutionPacketValidator _workerExecutionPacketValidator;

    public HarnessStateMachine(SessionStore sessionStore, ValidationOptions validationOptions)
    {
        _sessionStore = sessionStore;
        _validationOptions = validationOptions;

        _requirementIntentValidator = new RequirementIntentValidator();
        _retrievalChunkSetValidator = new RetrievalChunkSetValidator(validationOptions);
        _chunkQualityReportValidator = new ChunkQualityReportValidator();
        _retrieveMemoryByChunksResponseValidator = new RetrieveMemoryByChunksResponseValidator();
        _mergeRetrievalResultsResponseValidator = new MergeRetrievalResultsResponseValidator();
        _buildMemoryContextPackResponseValidator = new BuildMemoryContextPackResponseValidator();
        _executionPlanValidator = new ExecutionPlanValidator(validationOptions);
        _workerExecutionPacketValidator = new WorkerExecutionPacketValidator();
    }

    public StepResponse StartSession(StartSessionRequest request)
    {
        var sessionId = request.SessionId ?? Ids.NewSessionId();
        var taskId = Ids.NewTaskId();
        var session = new Session
        {
            SessionId = sessionId,
            TaskId = taskId,
            RawTask = request.RawTask,
            CurrentStage = HarnessStage.NeedRequirementIntent,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        if (request.Metadata != null)
            session.Metadata = request.Metadata;

        _sessionStore.Save(session);

        return BuildNextStepResponse(session);
    }

    public StepResponse GetNextStep(string sessionId)
    {
        var session = _sessionStore.Load(sessionId);
        if (session == null)
            return CreateErrorResponse(sessionId, "Session not found");

        if (session.CurrentStage == HarnessStage.Error)
            return new StepResponse
            {
                Success = true,
                SessionId = sessionId,
                TaskId = session.TaskId,
                Stage = StageNameMapper.ToProtocolName(session.CurrentStage),
                NextAction = HarnessActionName.StopWithError,
                Errors = session.Errors
            };

        return BuildNextStepResponse(session);
    }

    public StepResponse SubmitStepResult(SubmitStepResultRequest request)
    {
        var session = _sessionStore.Load(request.SessionId);
        if (session == null)
            return CreateErrorResponse(request.SessionId, "Session not found");

        if (session.CurrentStage == HarnessStage.Error)
            return CreateErrorResponse(session.SessionId, "Session is in error state");

        var expectedAction = GetExpectedAction(session.CurrentStage);
        if (request.CompletedAction != expectedAction)
        {
            MarkError(session, $"Expected action '{expectedAction}', got '{request.CompletedAction}'");
            return CreateErrorResponse(session.SessionId, $"Expected action '{expectedAction}', got '{request.CompletedAction}'");
        }

        var validationResult = ValidateArtifact(session, request);
        if (!validationResult.IsValid)
        {
            session.Errors.AddRange(validationResult.Errors);
            session.Warnings.AddRange(validationResult.Warnings);
            session.CurrentStage = HarnessStage.Error;
            _sessionStore.Save(session);
            return new StepResponse
            {
                Success = false,
                SessionId = session.SessionId,
                TaskId = session.TaskId,
                Stage = "error",
                NextAction = HarnessActionName.StopWithError,
                Errors = validationResult.Errors,
                Warnings = validationResult.Warnings
            };
        }

        StoreArtifact(session, request);
        session.Warnings.AddRange(validationResult.Warnings);
        AdvanceStage(session);
        _sessionStore.Save(session);

        if (session.CurrentStage == HarnessStage.Complete)
            return BuildCompletionResponse(session);

        return BuildNextStepResponse(session);
    }

    public StepResponse GetSessionStatus(string sessionId)
    {
        var session = _sessionStore.Load(sessionId);
        if (session == null)
            return CreateErrorResponse(sessionId, "Session not found");

        var acceptedArtifacts = GetAcceptedArtifactList(session);

        return new StepResponse
        {
            Success = true,
            SessionId = session.SessionId,
            TaskId = session.TaskId,
            Stage = StageNameMapper.ToProtocolName(session.CurrentStage),
            NextAction = GetExpectedAction(session.CurrentStage),
            Errors = session.Errors,
            Warnings = session.Warnings,
            AcceptedArtifacts = acceptedArtifacts,
                Payload = HarnessJson.CreateObject(w =>
                {
                    w.WriteString("rawTask", session.RawTask);
                })
        };
    }

    public StepResponse CancelSession(string sessionId)
    {
        var session = _sessionStore.Load(sessionId);
        if (session == null)
            return CreateErrorResponse(sessionId, "Session not found");

        session.Errors.Add("Session cancelled by user");
        session.CurrentStage = HarnessStage.Error;
        _sessionStore.Save(session);

        return new StepResponse
        {
            Success = true,
            SessionId = sessionId,
            Stage = "error",
            NextAction = HarnessActionName.StopWithError
        };
    }

    private ValidationResult ValidateArtifact(Session session, SubmitStepResultRequest request)
    {
        if (request.Artifact?.Value == null && IsMcpStage(session.CurrentStage))
        {
            return new ValidationResult { IsValid = false, Errors = new() { "Artifact is required for MCP stage" } };
        }

        object? requirementIntent = session.AcceptedRequirementIntent;
        object? retrievalChunkSet = session.AcceptedRetrievalChunkSet;

        var artifactType = request.Artifact.ArtifactType;
        var value = request.Artifact.Value;

        return session.CurrentStage switch
        {
            HarnessStage.NeedRequirementIntent => _requirementIntentValidator.Validate(value),
            HarnessStage.NeedRetrievalChunkSet => _retrievalChunkSetValidator.Validate(value, requirementIntent),
            HarnessStage.NeedRetrievalChunkValidation => _chunkQualityReportValidator.Validate(value),
            HarnessStage.NeedMcpRetrieveMemoryByChunks => _retrieveMemoryByChunksResponseValidator.Validate(value),
            HarnessStage.NeedMcpMergeRetrievalResults => _mergeRetrievalResultsResponseValidator.Validate(value),
            HarnessStage.NeedMcpBuildMemoryContextPack => _buildMemoryContextPackResponseValidator.Validate(value),
            HarnessStage.NeedExecutionPlan => _executionPlanValidator.Validate(value, requirementIntent),
            HarnessStage.NeedWorkerExecutionPacket => _workerExecutionPacketValidator.Validate(value, session.AcceptedExecutionPlan),
            _ => new ValidationResult { IsValid = true }
        };
    }

    private StepResponse BuildNextStepResponse(Session session)
    {
        var (nextAction, toolName, instructions, inputContract, payload) = GetStageInstructions(session.CurrentStage, session);

        return new StepResponse
        {
            Success = true,
            ProtocolVersion = "1.0",
            SessionId = session.SessionId,
            TaskId = session.TaskId,
            Stage = StageNameMapper.ToProtocolName(session.CurrentStage),
            NextAction = nextAction,
            ToolName = toolName,
            Instructions = instructions,
            InputContract = inputContract,
            Payload = payload,
            Warnings = session.Warnings
        };
    }

    private (string nextAction, string? toolName, List<string> instructions, InputContract? inputContract, JsonElement payload) GetStageInstructions(HarnessStage stage, Session session)
    {
        static void WriteJsonElement(Utf8JsonWriter w, JsonElement? element)
        {
            if (element is null)
            {
                w.WriteNullValue();
            }
            else
            {
                element.Value.WriteTo(w);
            }
        }

        return stage switch
        {
            HarnessStage.NeedRequirementIntent => (
                HarnessActionName.AgentGenerateRequirementIntent,
                null,
                new List<string> { "Convert the raw task into RequirementIntent JSON.", "Do not query MCP yet.", "Do not generate the plan yet." },
                new InputContract { ArtifactType = "RequirementIntent", SchemaVersion = "1.0" },
                HarnessJson.CreateObject(w =>
                {
                    w.WriteString("rawTask", session.RawTask);
                })
            ),
            HarnessStage.NeedRetrievalChunkSet => (
                HarnessActionName.AgentGenerateRetrievalChunkSet,
                null,
                new List<string> { "Generate compact purpose-specific retrieval chunks.", "Do not mix constraint, risk, pattern, or similar-case semantics in one chunk." },
                new InputContract { ArtifactType = "RetrievalChunkSet", SchemaVersion = "1.0" },
                HarnessJson.CreateObject(w =>
                {
                    w.WritePropertyName("requirementIntent");
                    WriteJsonElement(w, session.AcceptedRequirementIntent);
                })
            ),
            HarnessStage.NeedRetrievalChunkValidation => (
                HarnessActionName.AgentValidateChunkQuality,
                null,
                new List<string> { "Validate chunk quality, coverage, and purity." },
                new InputContract { ArtifactType = "ChunkQualityReport", SchemaVersion = "1.0" },
                HarnessJson.CreateObject(w =>
                {
                    w.WritePropertyName("retrievalChunkSet");
                    WriteJsonElement(w, session.AcceptedRetrievalChunkSet);
                })
            ),
            HarnessStage.NeedMcpRetrieveMemoryByChunks => (
                HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
                "retrieve_memory_by_chunks",
                new List<string> { "Call MCP tool retrieve_memory_by_chunks with the exact request provided." },
                new InputContract { ArtifactType = "RetrieveMemoryByChunksResponse", SchemaVersion = "1.0" },
                HarnessJson.CreateObject(w =>
                {
                    w.WritePropertyName("request");
                    w.WriteStartObject();
                    w.WriteString("schemaVersion", "1.0");
                    w.WriteString("requestId", session.SessionId + "-retrieve");
                    w.WriteString("taskId", session.TaskId);
                    w.WritePropertyName("requirementIntent");
                    WriteJsonElement(w, session.AcceptedRequirementIntent);
                    w.WritePropertyName("retrievalChunks");
                    WriteJsonElement(w, session.AcceptedRetrievalChunkSet);

                    w.WritePropertyName("search_profile");
                    w.WriteStartObject();
                    w.WriteBoolean("active_only", true);
                    w.WriteString("minimum_authority", "reviewed");
                    w.WriteNumber("max_items_per_chunk", 5);
                    w.WriteBoolean("require_type_separation", true);
                    w.WriteEndObject();

                    w.WriteEndObject();
                })
            ),
            HarnessStage.NeedMcpMergeRetrievalResults => (
                HarnessActionName.AgentCallMcpMergeRetrievalResults,
                "merge_retrieval_results",
                new List<string> { "Call MCP tool merge_retrieval_results with the exact request provided." },
                new InputContract { ArtifactType = "MergeRetrievalResultsResponse", SchemaVersion = "1.0" },
                HarnessJson.CreateObject(w =>
                {
                    w.WritePropertyName("request");
                    w.WriteStartObject();
                    w.WriteString("schemaVersion", "1.0");
                    w.WriteString("requestId", session.SessionId + "-merge");
                    w.WriteString("taskId", session.TaskId);
                    w.WritePropertyName("retrieved");
                    WriteJsonElement(w, session.AcceptedRetrieveMemoryByChunksResponse);
                    w.WriteEndObject();
                })
            ),
            HarnessStage.NeedMcpBuildMemoryContextPack => (
                HarnessActionName.AgentCallMcpBuildMemoryContextPack,
                "build_memory_context_pack",
                new List<string> { "Call MCP tool build_memory_context_pack with the exact request provided." },
                new InputContract { ArtifactType = "BuildMemoryContextPackResponse", SchemaVersion = "1.0" },
                HarnessJson.CreateObject(w =>
                {
                    w.WritePropertyName("request");
                    w.WriteStartObject();
                    w.WriteString("schemaVersion", "1.0");
                    w.WriteString("requestId", session.SessionId + "-contextpack");
                    w.WriteString("taskId", session.TaskId);
                    w.WritePropertyName("requirementIntent");
                    WriteJsonElement(w, session.AcceptedRequirementIntent);
                    w.WritePropertyName("retrieved");
                    WriteJsonElement(w, session.AcceptedRetrieveMemoryByChunksResponse);
                    w.WritePropertyName("merged");
                    WriteJsonElement(w, session.AcceptedMergeRetrievalResultsResponse);
                    w.WriteEndObject();
                })
            ),
            HarnessStage.NeedExecutionPlan => (
                HarnessActionName.AgentGenerateExecutionPlan,
                null,
                new List<string> { "Generate the execution plan from RequirementIntent, RetrievalChunkSet, and memory context pack.", "Include all constraints and acceptance criteria." },
                new InputContract { ArtifactType = "ExecutionPlan", SchemaVersion = "1.0" },
                HarnessJson.CreateObject(w =>
                {
                    w.WritePropertyName("requirementIntent");
                    WriteJsonElement(w, session.AcceptedRequirementIntent);
                    w.WritePropertyName("retrievalChunkSet");
                    WriteJsonElement(w, session.AcceptedRetrievalChunkSet);
                    w.WritePropertyName("memoryContextPack");
                    WriteJsonElement(w, session.AcceptedBuildMemoryContextPackResponse);
                })
            ),
            HarnessStage.NeedWorkerExecutionPacket => (
                HarnessActionName.AgentGenerateWorkerExecutionPacket,
                null,
                new List<string> { "Generate the worker execution packet from the accepted execution plan.", "Preserve all hard constraints and forbidden actions." },
                new InputContract { ArtifactType = "WorkerExecutionPacket", SchemaVersion = "1.0" },
                HarnessJson.CreateObject(w =>
                {
                    w.WritePropertyName("executionPlan");
                    WriteJsonElement(w, session.AcceptedExecutionPlan);
                })
            ),
            _ => (
                HarnessActionName.Complete,
                null,
                new List<string>(),
                null,
                HarnessJson.CreateObject(_ => { })
            )
        };
    }

    private string GetExpectedAction(HarnessStage stage)
    {
        return stage switch
        {
            HarnessStage.NeedRequirementIntent => HarnessActionName.AgentGenerateRequirementIntent,
            HarnessStage.NeedRetrievalChunkSet => HarnessActionName.AgentGenerateRetrievalChunkSet,
            HarnessStage.NeedRetrievalChunkValidation => HarnessActionName.AgentValidateChunkQuality,
            HarnessStage.NeedMcpRetrieveMemoryByChunks => HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            HarnessStage.NeedMcpMergeRetrievalResults => HarnessActionName.AgentCallMcpMergeRetrievalResults,
            HarnessStage.NeedMcpBuildMemoryContextPack => HarnessActionName.AgentCallMcpBuildMemoryContextPack,
            HarnessStage.NeedExecutionPlan => HarnessActionName.AgentGenerateExecutionPlan,
            HarnessStage.NeedWorkerExecutionPacket => HarnessActionName.AgentGenerateWorkerExecutionPacket,
            _ => ""
        };
    }

    private bool IsMcpStage(HarnessStage stage)
    {
        return stage is HarnessStage.NeedMcpRetrieveMemoryByChunks
            or HarnessStage.NeedMcpMergeRetrievalResults
            or HarnessStage.NeedMcpBuildMemoryContextPack;
    }

    private void StoreArtifact(Session session, SubmitStepResultRequest request)
    {
        session.LastAcceptedAction = request.CompletedAction;

        if (request.Artifact?.Value == null) return;

        var artifactType = request.Artifact.ArtifactType;
        switch (artifactType)
        {
            case "RequirementIntent":
                session.AcceptedRequirementIntent = request.Artifact.Value;
                break;
            case "RetrievalChunkSet":
                session.AcceptedRetrievalChunkSet = request.Artifact.Value;
                break;
            case "ChunkQualityReport":
                session.AcceptedChunkQualityReport = request.Artifact.Value;
                break;
            case "RetrieveMemoryByChunksResponse":
                session.AcceptedRetrieveMemoryByChunksResponse = request.Artifact.Value;
                break;
            case "MergeRetrievalResultsResponse":
                session.AcceptedMergeRetrievalResultsResponse = request.Artifact.Value;
                break;
            case "BuildMemoryContextPackResponse":
                session.AcceptedBuildMemoryContextPackResponse = request.Artifact.Value;
                break;
            case "ExecutionPlan":
                session.AcceptedExecutionPlan = request.Artifact.Value;
                break;
            case "WorkerExecutionPacket":
                session.AcceptedWorkerExecutionPacket = request.Artifact.Value;
                break;
        }
    }

    private void AdvanceStage(Session session)
    {
        session.CurrentStage = session.CurrentStage switch
        {
            HarnessStage.NeedRequirementIntent => HarnessStage.NeedRetrievalChunkSet,
            HarnessStage.NeedRetrievalChunkSet => HarnessStage.NeedRetrievalChunkValidation,
            HarnessStage.NeedRetrievalChunkValidation => HarnessStage.NeedMcpRetrieveMemoryByChunks,
            HarnessStage.NeedMcpRetrieveMemoryByChunks => HarnessStage.NeedMcpMergeRetrievalResults,
            HarnessStage.NeedMcpMergeRetrievalResults => HarnessStage.NeedMcpBuildMemoryContextPack,
            HarnessStage.NeedMcpBuildMemoryContextPack => HarnessStage.NeedExecutionPlan,
            HarnessStage.NeedExecutionPlan => HarnessStage.NeedWorkerExecutionPacket,
            HarnessStage.NeedWorkerExecutionPacket => HarnessStage.Complete,
            _ => session.CurrentStage
        };
    }

    private void MarkError(Session session, string error)
    {
        session.CurrentStage = HarnessStage.Error;
        session.Errors.Add(error);
        _sessionStore.Save(session);
    }

    private List<string> GetAcceptedArtifactList(Session session)
    {
        var list = new List<string>();
        if (session.AcceptedRequirementIntent != null) list.Add("RequirementIntent");
        if (session.AcceptedRetrievalChunkSet != null) list.Add("RetrievalChunkSet");
        if (session.AcceptedChunkQualityReport != null) list.Add("ChunkQualityReport");
        if (session.AcceptedRetrieveMemoryByChunksResponse != null) list.Add("RetrieveMemoryByChunksResponse");
        if (session.AcceptedMergeRetrievalResultsResponse != null) list.Add("MergeRetrievalResultsResponse");
        if (session.AcceptedBuildMemoryContextPackResponse != null) list.Add("BuildMemoryContextPackResponse");
        if (session.AcceptedExecutionPlan != null) list.Add("ExecutionPlan");
        if (session.AcceptedWorkerExecutionPacket != null) list.Add("WorkerExecutionPacket");
        return list;
    }

    private StepResponse BuildCompletionResponse(Session session)
    {
        return new StepResponse
        {
            Success = true,
            ProtocolVersion = "1.0",
            SessionId = session.SessionId,
            TaskId = session.TaskId,
            Stage = "complete",
            NextAction = HarnessActionName.Complete,
            CompletionArtifacts = new CompletionArtifacts
            {
                ExecutionPlan = session.AcceptedExecutionPlan,
                WorkerExecutionPacket = session.AcceptedWorkerExecutionPacket
            },
            AcceptedArtifacts = GetAcceptedArtifactList(session)
        };
    }

    private StepResponse CreateErrorResponse(string sessionId, string error)
    {
        return new StepResponse
        {
            Success = false,
            SessionId = sessionId,
            Stage = "error",
            NextAction = HarnessActionName.StopWithError,
            Errors = new List<string> { error }
        };
    }
}