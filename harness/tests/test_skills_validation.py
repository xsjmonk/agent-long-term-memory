"""
Test suite for harness planning skills.

Validates:
- Skill files exist and have required sections
- JSON schema examples in skills are valid
- Schema coverage rules are enforced
- Artifact specifications match protocol requirements
- Stage transitions are correct
"""

import json
import re
from pathlib import Path
import pytest


class TestSkillFilesExist:
    """Test that all required skill files exist."""

    REQUIRED_SKILLS = [
        "00-harness-control-plane.mdc",
        "01-harness-failure.mdc",
        "02-harness-execution.mdc",
        "03-harness-mcp-tool-calling.mdc",
        "04-harness-skill-activation.mdc",
        "05-artifact-schemas-detailed.mdc",
    ]

    @pytest.fixture
    def skills_dir(self):
        """Get the agent-rules directory."""
        base = Path(__file__).parent.parent
        skills_path = base / "agent-rules"
        assert skills_path.exists(), f"Skills directory not found at {skills_path}"
        return skills_path

    def test_all_skills_exist(self, skills_dir):
        """Verify all required skill files exist."""
        for skill in self.REQUIRED_SKILLS:
            skill_path = skills_dir / skill
            assert skill_path.exists(), f"Skill file not found: {skill}"

    def test_all_skills_not_empty(self, skills_dir):
        """Verify all skill files have content."""
        for skill in self.REQUIRED_SKILLS:
            skill_path = skills_dir / skill
            content = skill_path.read_text(encoding='utf-8')
            assert len(content) > 100, f"Skill file too small: {skill} ({len(content)} bytes)"


class TestSkillMetadata:
    """Test that skills have required metadata."""

    @pytest.fixture
    def skills_dir(self):
        base = Path(__file__).parent.parent
        return base / "agent-rules"

    def test_skill_has_metadata(self, skills_dir):
        """Each skill should start with YAML frontmatter."""
        for skill_file in skills_dir.glob("*.mdc"):
            content = skill_file.read_text(encoding='utf-8')
            assert content.startswith("---"), f"{skill_file.name} missing YAML frontmatter"

            # Check for required fields
            assert "description:" in content, f"{skill_file.name} missing description"
            assert "globs:" in content, f"{skill_file.name} missing globs field"

    def test_metadata_fields_populated(self, skills_dir):
        """Metadata fields should not be empty."""
        for skill_file in skills_dir.glob("*.mdc"):
            content = skill_file.read_text(encoding='utf-8')

            # Extract description
            desc_match = re.search(r'description:\s*(.+?)(?:\n|$)', content)
            assert desc_match, f"{skill_file.name} description malformed"
            desc = desc_match.group(1).strip()
            assert len(desc) > 10, f"{skill_file.name} description too short"


class TestArtifactSchemas:
    """Test artifact schemas in detailed-schemas skill."""

    @pytest.fixture
    def schema_skill(self):
        base = Path(__file__).parent.parent
        skill_path = base / "agent-rules" / "05-artifact-schemas-detailed.mdc"
        return skill_path.read_text(encoding='utf-8')

    def test_requirement_intent_schema(self, schema_skill):
        """RequirementIntent schema must exist and be valid JSON."""
        # Find schema block
        match = re.search(
            r'### Stage 1: RequirementIntent.*?```json\s*(.*?)```',
            schema_skill,
            re.DOTALL
        )
        assert match, "RequirementIntent schema not found"

        schema_text = match.group(1).strip()
        # Parse to validate JSON
        schema = json.loads(schema_text)

        # Check required fields
        required = ["task_id", "task_type", "goal", "hard_constraints", "risk_signals", "complexity"]
        for field in required:
            assert field in schema, f"RequirementIntent missing {field}"

    def test_retrieval_chunk_set_schema(self, schema_skill):
        """RetrievalChunkSet schema must be valid."""
        match = re.search(
            r'### Stage 2: RetrievalChunkSet.*?```json\s*(.*?)```',
            schema_skill,
            re.DOTALL
        )
        assert match, "RetrievalChunkSet schema not found"

        schema_text = match.group(1).strip()
        schema = json.loads(schema_text)

        required = ["task_id", "complexity", "chunks"]
        for field in required:
            assert field in schema, f"RetrievalChunkSet missing {field}"

        # Verify chunks structure
        assert isinstance(schema["chunks"], list), "chunks must be array"
        assert len(schema["chunks"]) > 0, "chunks array must have examples"

    def test_chunk_quality_report_schema(self, schema_skill):
        """ChunkQualityReport schema must have all required fields."""
        match = re.search(
            r'### Stage 3: ChunkQualityReport.*?```json\s*(.*?)```',
            schema_skill,
            re.DOTALL
        )
        assert match, "ChunkQualityReport schema not found"

        schema_text = match.group(1).strip()
        schema = json.loads(schema_text)

        # All boolean flags must be present
        required_bools = [
            "has_core_task", "has_constraint", "has_risk",
            "has_pattern", "has_similar_case"
        ]
        for field in required_bools:
            assert field in schema, f"ChunkQualityReport missing {field}: {field}"
            assert isinstance(schema[field], bool), f"{field} must be boolean"

        # Arrays must exist
        for array_field in ["chunk_assessments", "gaps_identified", "errors", "warnings"]:
            assert array_field in schema, f"ChunkQualityReport missing {array_field}"
            assert isinstance(schema[array_field], list), f"{array_field} must be array"

    def test_execution_plan_schema(self, schema_skill):
        """ExecutionPlan schema must be valid."""
        match = re.search(
            r'### Stage 7: ExecutionPlan.*?```json\s*(.*?)```',
            schema_skill,
            re.DOTALL
        )
        assert match, "ExecutionPlan schema not found"

        schema_text = match.group(1).strip()
        schema = json.loads(schema_text)

        required = ["task_id", "task", "scope", "steps", "deliverables"]
        for field in required:
            assert field in schema, f"ExecutionPlan missing {field}"

    def test_worker_execution_packet_schema(self, schema_skill):
        """WorkerExecutionPacket schema must have execution_rules."""
        match = re.search(
            r'### Stage 8: WorkerExecutionPacket.*?```json\s*(.*?)```',
            schema_skill,
            re.DOTALL
        )
        assert match, "WorkerExecutionPacket schema not found"

        schema_text = match.group(1).strip()
        schema = json.loads(schema_text)

        # Check required fields
        required = ["worker_id", "goal", "scope", "hard_constraints",
                    "forbidden_actions", "execution_rules", "steps"]
        for field in required:
            assert field in schema, f"WorkerExecutionPacket missing {field}"

        # execution_rules MUST include memory prohibition
        exec_rules = schema.get("execution_rules", [])
        assert len(exec_rules) > 0, "execution_rules cannot be empty"

        has_memory_prohibition = any(
            "memory" in rule.lower() for rule in exec_rules
        )
        assert has_memory_prohibition, "execution_rules must forbid memory retrieval"


class TestSchemaCoverageRules:
    """Test that coverage rules are documented correctly."""

    @pytest.fixture
    def schema_skill(self):
        base = Path(__file__).parent.parent
        skill_path = base / "agent-rules" / "05-artifact-schemas-detailed.mdc"
        return skill_path.read_text(encoding='utf-8')

    def test_coverage_rules_documented(self, schema_skill):
        """Coverage rules section must exist."""
        assert "Coverage Rules" in schema_skill, "Coverage Rules section missing"
        assert "core_task chunk always required" in schema_skill
        assert "constraint chunk required IF" in schema_skill
        assert "risk chunk required IF" in schema_skill
        assert "similar_case chunk required IF" in schema_skill

    def test_similar_case_task_shape_documented(self, schema_skill):
        """task_shape requirement for similar_case must be documented."""
        assert "task_shape" in schema_skill, "task_shape not mentioned"
        assert "REQUIRED for `similar_case`" in schema_skill or \
               "similar_case chunks MUST include `task_shape`" in schema_skill, \
               "task_shape requirement not clearly stated"


class TestExamplesAreValidJSON:
    """Test that all JSON examples in skills are valid."""

    @pytest.fixture
    def skills_dir(self):
        base = Path(__file__).parent.parent
        return base / "agent-rules"

    def test_all_json_examples_valid(self, skills_dir):
        """All JSON code blocks should be valid JSON."""
        errors = []

        for skill_file in skills_dir.glob("*.mdc"):
            content = skill_file.read_text(encoding='utf-8')

            # Find all JSON code blocks
            json_blocks = re.findall(
                r'```json\s*(.*?)```',
                content,
                re.DOTALL
            )

            for i, block in enumerate(json_blocks):
                try:
                    json.loads(block.strip())
                except json.JSONDecodeError as e:
                    errors.append(f"{skill_file.name} JSON block {i}: {e}")

        assert not errors, "\n".join(errors)


class TestPowerShellExamples:
    """Test PowerShell command examples are syntactically correct."""

    @pytest.fixture
    def control_plane_skill(self):
        base = Path(__file__).parent.parent
        skill_path = base / "agent-rules" / "00-harness-control-plane.mdc"
        return skill_path.read_text(encoding='utf-8')

    def test_start_session_example(self, control_plane_skill):
        """start-session example should have required parameters."""
        assert "start-session" in control_plane_skill
        assert "-RawTask" in control_plane_skill

    def test_submit_step_result_example(self, control_plane_skill):
        """submit-step-result example should have all required parameters."""
        assert "submit-step-result" in control_plane_skill
        assert "-SessionId" in control_plane_skill
        assert "-Action" in control_plane_skill
        assert "-ArtifactType" in control_plane_skill
        assert "-ArtifactFile" in control_plane_skill


class TestStageSequence:
    """Test that stage sequence is correct and documented."""

    @pytest.fixture
    def control_plane_skill(self):
        base = Path(__file__).parent.parent
        skill_path = base / "agent-rules" / "00-harness-control-plane.mdc"
        return skill_path.read_text(encoding='utf-8')

    def test_stage_table_exists(self, control_plane_skill):
        """Stage-to-action table must exist."""
        assert "Stage-to-Action Table" in control_plane_skill or \
               "stage sequence" in control_plane_skill.lower()

    def test_all_required_stages_documented(self, control_plane_skill):
        """All required planning stages must be documented."""
        required_stages = [
            "need_requirement_intent",
            "need_retrieval_chunk_set",
            "need_retrieval_chunk_validation",
            "need_mcp_retrieve_memory_by_chunks",
            "need_mcp_merge_retrieval_results",
            "need_mcp_build_memory_context_pack",
            "need_execution_plan",
            "need_worker_execution_packet",
        ]

        for stage in required_stages:
            assert stage in control_plane_skill, f"Stage {stage} not documented"


class TestErrorHandling:
    """Test error handling skill."""

    @pytest.fixture
    def failure_skill(self):
        base = Path(__file__).parent.parent
        skill_path = base / "agent-rules" / "01-harness-failure.mdc"
        return skill_path.read_text(encoding='utf-8')

    def test_hard_stop_principle_documented(self, failure_skill):
        """Hard stop principle must be clearly stated."""
        assert "HARD STOP" in failure_skill or "hard stop" in failure_skill.lower()
        assert "stop immediately" in failure_skill.lower()

    def test_failure_categories_documented(self, failure_skill):
        """All failure categories must be documented."""
        categories = [
            "Harness Validation Failure",
            "MCP Tool Call Failure",
            "Wrapper / Executable Invocation Failure",
            "Session Resume / State Mismatch Failure"
        ]

        for category in categories:
            assert category in failure_skill, f"Failure category missing: {category}"

    def test_recovery_procedure_documented(self, failure_skill):
        """Recovery procedure must be documented."""
        assert "Resubmit" in failure_skill or "resubmit" in failure_skill.lower()
        assert "errors array" in failure_skill.lower()


class TestMCPToolCalling:
    """Test MCP tool calling skill."""

    @pytest.fixture
    def mcp_skill(self):
        base = Path(__file__).parent.parent
        skill_path = base / "agent-rules" / "03-harness-mcp-tool-calling.mdc"
        return skill_path.read_text(encoding='utf-8')

    def test_exact_tool_mapping_documented(self, mcp_skill):
        """Exact tool mapping must be documented."""
        tools = [
            "retrieve_memory_by_chunks",
            "merge_retrieval_results",
            "build_memory_context_pack"
        ]

        for tool in tools:
            assert tool in mcp_skill, f"Tool {tool} not documented"

    def test_no_modification_rules_clear(self, mcp_skill):
        """Rules about not modifying responses must be clear."""
        assert "RAW" in mcp_skill, "Raw response requirement not documented"
        assert "Do NOT modify" in mcp_skill or "do not modify" in mcp_skill.lower()


class TestExecutionPhase:
    """Test execution phase skill."""

    @pytest.fixture
    def execution_skill(self):
        base = Path(__file__).parent.parent
        skill_path = base / "agent-rules" / "02-harness-execution.mdc"
        return skill_path.read_text(encoding='utf-8')

    def test_memory_prohibition_clear(self, execution_skill):
        """Memory retrieval prohibition must be very clear."""
        assert "memory" in execution_skill.lower()
        assert "DO NOT" in execution_skill or "FORBIDDEN" in execution_skill

    def test_no_replanning_rule(self, execution_skill):
        """No replanning rule must be documented."""
        assert "replan" in execution_skill.lower()

    def test_output_schema_documented(self, execution_skill):
        """Required output sections must be documented."""
        assert "per_step_results" in execution_skill
        assert "final_deliverables" in execution_skill
        assert "validation_summary" in execution_skill


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
