using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HarnessMcp.Infrastructure.Postgres;

internal sealed record BuilderApiTaskShapeDto(
    [property: JsonPropertyName("task_type")] string TaskType,
    [property: JsonPropertyName("feature_shape")] string FeatureShape,
    [property: JsonPropertyName("engine_change_allowed")] bool EngineChangeAllowed,
    [property: JsonPropertyName("likely_layers")] IReadOnlyList<string> LikelyLayers,
    [property: JsonPropertyName("risk_signals")] IReadOnlyList<string> RiskSignals,
    [property: JsonPropertyName("complexity")] string? Complexity);