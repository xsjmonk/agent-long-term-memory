using System.Text.Json;
using HarnessMcp.ControlPlane;
using HarnessMcp.ControlPlane.Validators;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Negative tests that verify validators reject non-canonical artifact shapes.
/// Each test proves that a specific invalid input is rejected with a meaningful error.
///
/// These tests will FAIL if validators are too lenient.
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class CanonicalContractRejectionTests
{
    // ==========================================
    // RequirementIntent Rejection Tests
    // ==========================================

    [Fact]
    public void RequirementIntent_Reject_MissingTaskId()
    {
        var v = new RequirementIntentValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_type"": ""ui-change"",
            ""goal"": ""add feature"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("task_id"));
    }

    [Fact]
    public void RequirementIntent_Reject_MissingTaskType()
    {
        var v = new RequirementIntentValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""goal"": ""add feature"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("task_type"));
    }

    [Fact]
    public void RequirementIntent_Reject_MissingGoal()
    {
        var v = new RequirementIntentValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task_type"": ""ui-change"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("goal"));
    }

    [Fact]
    public void RequirementIntent_Reject_MissingHardConstraints()
    {
        var v = new RequirementIntentValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""add feature"",
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("hard_constraints"));
    }

    [Fact]
    public void RequirementIntent_Reject_MissingRiskSignals()
    {
        var v = new RequirementIntentValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""add feature"",
            ""hard_constraints"": [],
            ""complexity"": ""low""
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("risk_signals"));
    }

    [Fact]
    public void RequirementIntent_Reject_InvalidComplexity()
    {
        var v = new RequirementIntentValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""add feature"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""extreme""
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("complexity"));
    }

    [Fact]
    public void RequirementIntent_Reject_UnknownField()
    {
        var v = new RequirementIntentValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""add feature"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low"",
            ""non_canonical_field"": ""value""
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("unknown") || e.Contains("non_canonical_field"));
    }

    // ==========================================
    // RetrievalChunkSet Rejection Tests
    // ==========================================

    [Fact]
    public void RetrievalChunkSet_Reject_MissingTaskId()
    {
        var v = new RetrievalChunkSetValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""complexity"": ""low"",
            ""chunks"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""test"" }]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("task_id"));
    }

    [Fact]
    public void RetrievalChunkSet_Reject_MissingComplexity()
    {
        var v = new RetrievalChunkSetValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""chunks"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""test"" }]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("complexity"));
    }

    [Fact]
    public void RetrievalChunkSet_Reject_MissingChunks()
    {
        var v = new RetrievalChunkSetValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""complexity"": ""low""
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chunks"));
    }

    [Fact]
    public void RetrievalChunkSet_Reject_UnknownChunkType()
    {
        var v = new RetrievalChunkSetValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""complexity"": ""low"",
            ""chunks"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""unknown_type"", ""text"": ""test"" }]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chunk_type") || e.Contains("unknown_type") || e.Contains("core_task"));
    }

    [Fact]
    public void RetrievalChunkSet_Reject_SimilarCaseWithoutTaskShape()
    {
        var v = new RetrievalChunkSetValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""core task text"" },
                { ""chunk_id"": ""c2"", ""chunk_type"": ""similar_case"" }
            ]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("task_shape") || e.Contains("similar_case"));
    }

    [Fact]
    public void RetrievalChunkSet_Reject_MissingCoreTaskChunk()
    {
        var v = new RetrievalChunkSetValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""constraint"", ""text"": ""must not break API"" }
            ]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("core_task"));
    }

    // ==========================================
    // MCP Response Rejection Tests
    // ==========================================

    [Fact]
    public void RetrieveMemoryByChunks_Reject_LegacyResultsAlias()
    {
        var v = new RetrieveMemoryByChunksResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""results"": []
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chunk_results") || e.Contains("results"));
    }

    [Fact]
    public void RetrieveMemoryByChunks_Reject_LegacyRetrievedAlias()
    {
        var v = new RetrieveMemoryByChunksResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""retrieved"": []
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chunk_results") || e.Contains("retrieved"));
    }

    [Fact]
    public void RetrieveMemoryByChunks_Reject_MissingChunkResults()
    {
        var v = new RetrieveMemoryByChunksResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""t1"" }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chunk_results"));
    }

    [Fact]
    public void RetrieveMemoryByChunks_Reject_MissingRequiredBucket()
    {
        var v = new RetrieveMemoryByChunksResponseValidator();
        // Only one bucket present instead of all required buckets
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""chunk_results"": [
                {
                    ""chunk_id"": ""c1"",
                    ""chunk_type"": ""core_task"",
                    ""results"": {
                        ""decisions"": []
                    }
                }
            ]
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("best_practices") || e.Contains("anti_patterns") || e.Contains("bucket"));
    }

    [Fact]
    public void RetrieveMemoryByChunks_Reject_UnknownBucket()
    {
        var v = new RetrieveMemoryByChunksResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""chunk_results"": [
                {
                    ""chunk_id"": ""c1"",
                    ""chunk_type"": ""core_task"",
                    ""results"": {
                        ""decisions"": [],
                        ""best_practices"": [],
                        ""anti_patterns"": [],
                        ""similar_cases"": [],
                        ""constraints"": [],
                        ""references"": [],
                        ""structures"": [],
                        ""unknown_bucket"": []
                    }
                }
            ]
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("unknown_bucket") || e.Contains("unknown bucket"));
    }

    [Fact]
    public void MergeRetrievalResults_Reject_LegacyMergedResultsAlias()
    {
        var v = new MergeRetrievalResultsResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""merged_results"": {}
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("merged_results") || e.Contains("merged"));
    }

    [Fact]
    public void MergeRetrievalResults_Reject_MissingMergedObject()
    {
        var v = new MergeRetrievalResultsResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""t1"" }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("merged"));
    }

    [Fact]
    public void MergeRetrievalResults_Reject_UnknownBucket()
    {
        var v = new MergeRetrievalResultsResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""merged"": {
                ""decisions"": [],
                ""constraints"": [],
                ""best_practices"": [],
                ""anti_patterns"": [],
                ""similar_cases"": [],
                ""references"": [],
                ""structures"": [],
                ""non_canonical_bucket"": []
            }
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("non_canonical_bucket") || e.Contains("unknown bucket"));
    }

    [Fact]
    public void BuildMemoryContextPack_Reject_ContextPackAlias()
    {
        var v = new BuildMemoryContextPackResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""context_pack"": {}
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("memory_context_pack") || e.Contains("context_pack"));
    }

    [Fact]
    public void BuildMemoryContextPack_Reject_MissingMemoryContextPack()
    {
        var v = new BuildMemoryContextPackResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""t1"" }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("memory_context_pack"));
    }

    [Fact]
    public void BuildMemoryContextPack_Reject_MissingRequiredSection()
    {
        var v = new BuildMemoryContextPackResponseValidator();
        // Missing avoid and similar_case_guidance sections
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""memory_context_pack"": {
                ""must_follow"": [],
                ""best_practices"": [],
                ""retrieval_support"": {
                    ""multi_supported_items"": [],
                    ""single_route_important_items"": []
                }
            }
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("avoid") || e.Contains("similar_case_guidance"));
    }

    [Fact]
    public void RetrieveMemoryByChunks_Reject_ChunkMissingResultsField()
    {
        var v = new RetrieveMemoryByChunksResponseValidator();
        // chunk exists but has no 'results' field
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""chunk_results"": [
                {
                    ""chunk_id"": ""c1"",
                    ""chunk_type"": ""core_task""
                }
            ]
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse("chunk missing 'results' object must be rejected");
        result.Errors.Should().Contain(e => e.Contains("results"));
    }

    [Fact]
    public void RetrieveMemoryByChunks_Reject_EmptyChunkResults()
    {
        var v = new RetrieveMemoryByChunksResponseValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""chunk_results"": []
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse("empty chunk_results must be rejected");
        result.Errors.Should().Contain(e => e.Contains("chunk_results"));
    }

    [Fact]
    public void MergeRetrievalResults_Reject_ItemMissingItemObject()
    {
        var v = new MergeRetrievalResultsResponseValidator();
        // merged bucket item missing the required 'item' wrapper object
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""merged"": {
                ""decisions"": [
                    {
                        ""knowledge_item_id"": ""k1"",
                        ""title"": ""t"",
                        ""summary"": ""s"",
                        ""supported_by_chunk_ids"": [""c1""],
                        ""supported_by_chunk_types"": [""core_task""],
                        ""merge_rationales"": [""relevant""]
                    }
                ],
                ""constraints"": [],
                ""best_practices"": [],
                ""anti_patterns"": [],
                ""similar_cases"": [],
                ""references"": [],
                ""structures"": []
            }
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse("merged item missing 'item' wrapper object must be rejected");
        result.Errors.Should().Contain(e => e.Contains("item"));
    }

    [Fact]
    public void MergeRetrievalResults_Reject_ItemMissingSupportedByChunkIds()
    {
        var v = new MergeRetrievalResultsResponseValidator();
        // merged bucket item has 'item' but missing 'supported_by_chunk_ids'
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""merged"": {
                ""decisions"": [
                    {
                        ""item"": { ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" },
                        ""supported_by_chunk_types"": [""core_task""],
                        ""merge_rationales"": [""relevant""]
                    }
                ],
                ""constraints"": [],
                ""best_practices"": [],
                ""anti_patterns"": [],
                ""similar_cases"": [],
                ""references"": [],
                ""structures"": []
            }
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse("merged item missing 'supported_by_chunk_ids' must be rejected");
        result.Errors.Should().Contain(e => e.Contains("supported_by_chunk_ids"));
    }

    [Fact]
    public void MergeRetrievalResults_Reject_MissingRequiredBucket()
    {
        var v = new MergeRetrievalResultsResponseValidator();
        // merged has only some buckets, missing others
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""merged"": {
                ""decisions"": [],
                ""best_practices"": []
            }
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse("merged missing required buckets must be rejected");
        result.Errors.Should().Contain(e => e.Contains("constraints") || e.Contains("anti_patterns") || e.Contains("bucket"));
    }

    [Fact]
    public void BuildMemoryContextPack_Reject_MissingRetrievalSupport()
    {
        var v = new BuildMemoryContextPackResponseValidator();
        // memory_context_pack present but missing retrieval_support
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""memory_context_pack"": {
                ""must_follow"": [],
                ""best_practices"": [],
                ""avoid"": [],
                ""similar_case_guidance"": []
            }
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse("memory_context_pack missing retrieval_support must be rejected");
        result.Errors.Should().Contain(e => e.Contains("retrieval_support"));
    }

    // ==========================================
    // ExecutionPlan Rejection Tests
    // ==========================================

    [Fact]
    public void ExecutionPlan_Reject_MissingTaskId()
    {
        var v = new ExecutionPlanValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""deliverables"": [""d""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("task_id"));
    }

    [Fact]
    public void ExecutionPlan_Reject_MissingTask()
    {
        var v = new ExecutionPlanValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""scope"": ""ui layer"",
            ""constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""deliverables"": [""d""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("task") && !e.Contains("task_id"));
    }

    [Fact]
    public void ExecutionPlan_Reject_MissingScope()
    {
        var v = new ExecutionPlanValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task"": ""implement feature"",
            ""constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""deliverables"": [""d""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("scope"));
    }

    [Fact]
    public void ExecutionPlan_Reject_EmptyConstraints()
    {
        var v = new ExecutionPlanValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""constraints"": [],
            ""forbidden_actions"": [""f1""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""deliverables"": [""d""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("empty constraints array must be rejected — at least one constraint is required");
        result.Errors.Should().Contain(e => e.Contains("constraint"));
    }

    [Fact]
    public void ExecutionPlan_Reject_EmptyForbiddenActions()
    {
        var v = new ExecutionPlanValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""constraints"": [""c1""],
            ""forbidden_actions"": [],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""deliverables"": [""d""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("empty forbidden_actions must be rejected — at least one forbidden action is required");
        result.Errors.Should().Contain(e => e.Contains("forbidden_actions"));
    }

    [Fact]
    public void ExecutionPlan_Reject_NonConsecutiveStepNumbers()
    {
        var v = new ExecutionPlanValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""steps"": [
                { ""step_number"": 1, ""title"": ""s1"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] },
                { ""step_number"": 3, ""title"": ""s3"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }
            ],
            ""deliverables"": [""d""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("non-consecutive step numbers must be rejected");
        result.Errors.Should().Contain(e => e.Contains("step") || e.Contains("consecutive") || e.Contains("missing"));
    }

    [Fact]
    public void ExecutionPlan_Reject_StepMissingAcceptanceChecks()
    {
        var v = new ExecutionPlanValidator(new ValidationOptions());
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""steps"": [
                { ""step_number"": 1, ""title"": ""s1"", ""actions"": [""a""], ""outputs"": [""o""] }
            ],
            ""deliverables"": [""d""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("step missing acceptance_checks must be rejected");
        result.Errors.Should().Contain(e => e.Contains("acceptance_check") || e.Contains("acceptance"));
    }

    // ==========================================
    // WorkerExecutionPacket Rejection Tests
    // ==========================================

    [Fact]
    public void WorkerExecutionPacket_Reject_MissingGoal()
    {
        var v = new WorkerExecutionPacketValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""scope"": ""ui layer"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [""Do NOT retrieve long-term memory. Do NOT replan.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("goal"));
    }

    [Fact]
    public void WorkerExecutionPacket_Reject_EmptyHardConstraints()
    {
        var v = new WorkerExecutionPacketValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""hard_constraints"": [],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [""Do NOT retrieve long-term memory. Do NOT replan.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("empty hard_constraints must be rejected");
        result.Errors.Should().Contain(e => e.Contains("hard_constraints"));
    }

    [Fact]
    public void WorkerExecutionPacket_Reject_EmptyExecutionRules()
    {
        var v = new WorkerExecutionPacketValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("empty execution_rules must be rejected");
        result.Errors.Should().Contain(e => e.Contains("execution_rules"));
    }

    [Fact]
    public void WorkerExecutionPacket_Reject_NoMemoryProhibitionInExecutionRules()
    {
        var v = new WorkerExecutionPacketValidator();
        // execution_rules does not mention memory prohibition
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [""follow the plan exactly""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("execution_rules without memory prohibition must be rejected");
        result.Errors.Should().Contain(e => e.Contains("memory"));
    }

    [Fact]
    public void WorkerExecutionPacket_Reject_ReplanInstructions()
    {
        var v = new WorkerExecutionPacketValidator();
        // steps contain replanning instructions
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [""Do NOT retrieve long-term memory. Do NOT replan.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""replan if blocked"", ""actions"": [""replan the approach""], ""outputs"": [""new plan""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("steps with replan instructions must be rejected");
        result.Errors.Should().Contain(e => e.Contains("replan") || e.Contains("plan"));
    }

    [Fact]
    public void WorkerExecutionPacket_Reject_MemoryRetrievalInSteps()
    {
        var v = new WorkerExecutionPacketValidator();
        // steps instruct independent memory retrieval
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [""Do NOT retrieve long-term memory. Do NOT replan.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""retrieve memory"", ""actions"": [""retrieve from long_term memory""], ""outputs"": [""context""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("steps with memory retrieval instructions must be rejected");
        result.Errors.Should().Contain(e => e.Contains("memory") || e.Contains("retrieval"));
    }

    [Fact]
    public void WorkerExecutionPacket_Reject_MissingScope()
    {
        var v = new WorkerExecutionPacketValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""implement feature"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [""Do NOT retrieve long-term memory. Do NOT replan.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("worker packet missing 'scope' must be rejected");
        result.Errors.Should().Contain(e => e.Contains("scope"));
    }

    [Fact]
    public void WorkerExecutionPacket_Reject_EmptyForbiddenActions()
    {
        var v = new WorkerExecutionPacketValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [],
            ""execution_rules"": [""Do NOT retrieve long-term memory. Do NOT replan.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");
        var result = v.Validate(input, null);
        result.IsValid.Should().BeFalse("empty forbidden_actions must be rejected");
        result.Errors.Should().Contain(e => e.Contains("forbidden_actions"));
    }

    [Fact]
    public void WorkerExecutionPacket_Reject_ForbiddenActionsNotPreservedFromExecutionPlan()
    {
        var v = new WorkerExecutionPacketValidator();

        var executionPlan = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""constraints"": [""must not change engine""],
            ""forbidden_actions"": [""modify engine files"", ""change database schema""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""deliverables"": [""d""]
        }");

        // Worker packet omits 'modify engine files' from forbidden_actions
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""implement feature"",
            ""scope"": ""ui layer"",
            ""hard_constraints"": [""must not change engine""],
            ""forbidden_actions"": [""change database schema""],
            ""execution_rules"": [""Do NOT retrieve long-term memory. Do NOT replan.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");

        var result = v.Validate(input, executionPlan);
        result.IsValid.Should().BeFalse(
            "forbidden_actions from execution plan must all be preserved in worker packet — 'modify engine files' was dropped");
        result.Errors.Should().Contain(e => e.Contains("forbidden") || e.Contains("preserve"));
    }

    [Fact]
    public void RequirementIntent_Reject_MissingComplexity()
    {
        var v = new RequirementIntentValidator();
        var input = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""add feature"",
            ""hard_constraints"": [],
            ""risk_signals"": []
        }");
        var result = v.Validate(input);
        result.IsValid.Should().BeFalse("missing complexity must be rejected");
        result.Errors.Should().Contain(e => e.Contains("complexity"));
    }
}
