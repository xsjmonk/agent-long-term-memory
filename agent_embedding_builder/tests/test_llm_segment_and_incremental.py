from __future__ import annotations

import os
import sys
import types
from pathlib import Path

import pytest

from app.category_ontology import load_category_ontology
from app.class_resolution import resolve_retrieval_class
from app.config_loader import load_config
from app.hash_state import should_rebuild
from app.local_llm import LocalLLM, detect_llama_gpu_layers, extract_json_object, validate_local_gguf_model
from app.models import CandidateSegment, DownloadSourceConfig, EmbeddingConfig, LLMConfig, LabelEvidence, SelectedSegment, SourceMetadata
from app.retrieval_profile_builder import build_retrieval_profiles
from app.segmenter import select_segments_with_duplicate_control


def _write_valid_gguf(path: Path, size_bytes: int = 1024 * 1024) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"G" * size_bytes)


def _llm_config(tmp_path: Path, download_if_missing: bool = True) -> LLMConfig:
    return LLMConfig(
        provider="llama_cpp",
        local_model_path=str(tmp_path / "configured" / "missing.gguf"),
        download_if_missing=download_if_missing,
        download_source=DownloadSourceConfig(type="huggingface", repo_id="repo", filename="*Q8_0.gguf", local_dir=str(tmp_path / "staging")),
        context_length=1024,
        max_tokens=32,
        temperature=0.1,
        gpu_layers=0,
        seed=1,
    )


def test_llm_uses_existing_valid_configured_model_path(tmp_path: Path) -> None:
    configured = tmp_path / "configured" / "ready.gguf"
    _write_valid_gguf(configured)
    config = _llm_config(tmp_path, download_if_missing=False)
    config.local_model_path = str(configured)
    llm = LocalLLM(config)
    assert llm.model_path == configured


def test_llm_download_if_missing_copies_to_exact_configured_path(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    staging_path = tmp_path / "staging" / "sub" / "abc-Q8_0.gguf"
    _write_valid_gguf(staging_path)
    fake_hf = types.SimpleNamespace(
        snapshot_download=lambda **kwargs: str(tmp_path / "staging"),
        hf_hub_download=lambda **kwargs: str(staging_path),
    )
    monkeypatch.setitem(sys.modules, "huggingface_hub", fake_hf)
    llm = LocalLLM(_llm_config(tmp_path))
    configured = Path(_llm_config(tmp_path).local_model_path)
    assert llm.model_path == configured
    assert configured.exists()
    assert configured.read_bytes() == staging_path.read_bytes()


def test_llm_tiny_placeholder_file_is_rejected(tmp_path: Path) -> None:
    tiny = tmp_path / "configured" / "tiny.gguf"
    tiny.parent.mkdir(parents=True, exist_ok=True)
    tiny.write_bytes(b"x" * 1024)
    config = _llm_config(tmp_path, download_if_missing=False)
    config.local_model_path = str(tiny)
    with pytest.raises(ValueError, match="too small"):
        LocalLLM(config)


def test_llm_tiny_placeholder_file_redownloads_when_allowed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    tiny = tmp_path / "configured" / "tiny.gguf"
    tiny.parent.mkdir(parents=True, exist_ok=True)
    tiny.write_bytes(b"x" * 1024)
    staging_path = tmp_path / "staging" / "replacement.gguf"
    _write_valid_gguf(staging_path)
    config = _llm_config(tmp_path, download_if_missing=True)
    config.local_model_path = str(tiny)
    fake_hf = types.SimpleNamespace(
        snapshot_download=lambda **kwargs: str(tmp_path / "staging"),
        hf_hub_download=lambda **kwargs: str(staging_path),
    )
    monkeypatch.setitem(sys.modules, "huggingface_hub", fake_hf)
    llm = LocalLLM(config)
    assert llm.model_path == tiny
    assert tiny.read_bytes() == staging_path.read_bytes()


def test_validate_local_gguf_model_rejects_bad_shapes(tmp_path: Path) -> None:
    wrong_suffix = tmp_path / "model.bin"
    wrong_suffix.write_bytes(b"x" * (1024 * 1024))
    with pytest.raises(ValueError, match=r"\.gguf"):
        validate_local_gguf_model(wrong_suffix)
    with pytest.raises(FileNotFoundError):
        validate_local_gguf_model(tmp_path / "missing.gguf")


def test_llm_wildcard_download_selection_is_deterministic(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    stage = tmp_path / "stage"
    _write_valid_gguf(stage / "zeta-Q8_0.gguf")
    _write_valid_gguf(stage / "alpha-Q8_0.gguf")
    _write_valid_gguf(stage / "beta-other.gguf")
    calls: dict[str, str] = {}

    def _snapshot_download(**kwargs):
        calls["allow_patterns"] = kwargs["allow_patterns"][0]
        return str(stage)

    fake_hf = types.SimpleNamespace(
        snapshot_download=_snapshot_download,
        hf_hub_download=lambda **kwargs: str(stage / "unused.gguf"),
    )
    monkeypatch.setitem(sys.modules, "huggingface_hub", fake_hf)
    llm = LocalLLM(_llm_config(tmp_path))
    assert calls["allow_patterns"] == "*Q8_0.gguf"
    assert llm.model_path == Path(_llm_config(tmp_path).local_model_path)
    assert llm.model_path.read_bytes() == (stage / "alpha-Q8_0.gguf").read_bytes()


def test_llm_non_wildcard_download_respects_final_configured_path(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    staging_path = tmp_path / "staging" / "repo-model.gguf"
    _write_valid_gguf(staging_path)
    config = _llm_config(tmp_path)
    config.download_source.filename = "repo-model.gguf"
    fake_hf = types.SimpleNamespace(
        snapshot_download=lambda **kwargs: str(tmp_path / "staging"),
        hf_hub_download=lambda **kwargs: str(staging_path),
    )
    monkeypatch.setitem(sys.modules, "huggingface_hub", fake_hf)
    llm = LocalLLM(config)
    assert llm.model_path == Path(config.local_model_path)
    assert llm.model_path != staging_path


def test_llm_json_parse_retry_behavior(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    existing = tmp_path / "ok.gguf"
    _write_valid_gguf(existing)
    config = _llm_config(tmp_path, download_if_missing=False)
    config.local_model_path = str(existing)
    llm = LocalLLM(config)

    class FakeRuntime:
        def __init__(self):
            self.calls = 0

        def create_completion(self, **kwargs):
            self.calls += 1
            text = "bad-json" if self.calls == 1 else '{"labels": [], "normalized_retrieval_text": "ok"}'
            return {"choices": [{"text": text}]}

    fake = FakeRuntime()
    monkeypatch.setattr(llm, "_load_runtime", lambda: fake)
    result = llm.infer("x", retries=1)
    assert result.normalized_retrieval_text == "ok"


def test_extract_json_object_handles_wrapped_output() -> None:
    wrapped = 'Here is the result:\n```json\n{"labels": [], "normalized_retrieval_text": "ok"}\n```\nDone.'
    assert extract_json_object(wrapped) == '{"labels": [], "normalized_retrieval_text": "ok"}'


def test_detect_llama_gpu_layers_prefers_gpu_when_available(monkeypatch: pytest.MonkeyPatch) -> None:
    fake_torch = types.SimpleNamespace(
        cuda=types.SimpleNamespace(is_available=lambda: True),
        backends=types.SimpleNamespace(mps=types.SimpleNamespace(is_available=lambda: False)),
    )
    monkeypatch.setitem(sys.modules, "torch", fake_torch)
    assert detect_llama_gpu_layers(0) == -1
    assert detect_llama_gpu_layers(12) == 12


def test_llama_runtime_falls_back_to_cpu_when_gpu_offload_fails(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    existing = tmp_path / "ok.gguf"
    _write_valid_gguf(existing)
    config = _llm_config(tmp_path, download_if_missing=False)
    config.local_model_path = str(existing)

    fake_torch = types.SimpleNamespace(
        cuda=types.SimpleNamespace(is_available=lambda: True),
        backends=types.SimpleNamespace(mps=types.SimpleNamespace(is_available=lambda: False)),
    )
    monkeypatch.setitem(sys.modules, "torch", fake_torch)

    calls: list[int] = []

    class FakeLlama:
        def __init__(self, **kwargs):
            calls.append(kwargs["n_gpu_layers"])
            if kwargs["n_gpu_layers"] == -1:
                raise RuntimeError("gpu failed")

    monkeypatch.setitem(sys.modules, "llama_cpp", types.SimpleNamespace(Llama=FakeLlama))
    llm = LocalLLM(config)
    llm._load_runtime()
    assert calls == [-1, 0]


def test_retrieval_class_resolution() -> None:
    root = Path(__file__).resolve().parents[1]
    ontology = load_category_ontology(root / "config" / "category_ontology.json", root / "config" / "category_ontology.schema.json")
    labels = [LabelEvidence(label="anti_pattern", label_role="primary", confidence=0.9, source_method="rule_based")]
    assert resolve_retrieval_class(labels, ontology) == "antipattern"
    assert resolve_retrieval_class([LabelEvidence(label="project_history", label_role="primary", confidence=0.7, source_method="rule_based")], ontology) == "historical_note"
    assert resolve_retrieval_class([LabelEvidence(label="constraint", label_role="primary", confidence=0.7, source_method="rule_based"), LabelEvidence(label="incident", label_role="secondary", confidence=0.8, source_method="rule_based")], ontology) == "constraint"
    assert resolve_retrieval_class([LabelEvidence(label="structure_fact", label_role="primary", confidence=0.7, source_method="rule_based")], ontology) == "structure"
    assert resolve_retrieval_class([], ontology) == "reference"


def test_duplicate_control_parent_child_logic() -> None:
    parent = CandidateSegment(Path("a.md"), "markdown", "section", "broad text", ["Doc", "Section"], 1, 20, "Doc", "p")
    child = CandidateSegment(Path("a.md"), "markdown", "paragraph_group", "broad text", ["Doc", "Section", "g1"], 2, 4, "Doc", "c")
    kept = select_segments_with_duplicate_control([parent, child], {"p": 0.7, "c": 0.6})
    assert kept == [parent]


def test_incremental_rebuild_decision_logic() -> None:
    signature = {"builder_version": "1", "classifier_version": "1", "embedding_model_version": "m1", "normalization_version": "1", "ontology_version": "o1", "schema_compatibility_version": "s1"}
    last_state = {"artifact_hash": "abc", "build_status": "succeeded", **signature}
    assert not should_rebuild(last_state, "abc", signature, enabled=True)
    assert should_rebuild(last_state, "def", signature, enabled=True)
    assert should_rebuild(last_state, "abc", {**signature, "ontology_version": "o2"}, enabled=True)


def test_retrieval_profiles_rules() -> None:
    item = SelectedSegment(
        candidate=CandidateSegment(Path("a.md"), "markdown", "section", "text", ["Doc"], 1, 1, metadata=SourceMetadata()),
        labels=[LabelEvidence(label="best_practice", label_role="primary", confidence=0.9, source_method="rule_based")],
        retrieval_class="best_practice",
        normalized_retrieval_text="normalized",
        summary="summary",
        scopes=[],
        tags=[],
        retrieval_profiles=[],
        confidence=0.9,
        explanation_payload={},
    )
    profiles = build_retrieval_profiles(item, load_config(Path(__file__).resolve().parents[1]).retrieval_profiles)
    profile_types = [profile[0] for profile in profiles]
    assert "default" in profile_types
    assert "details" not in profile_types
    assert "pattern" in profile_types


def test_embedding_provider_reuses_existing_model_directory(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    local_dir = tmp_path / "emb"
    local_dir.mkdir(parents=True)

    class FakeSentenceTransformer:
        def __init__(self, *args, **kwargs):
            raise AssertionError("download/load should not be triggered for existing embedding model dir")

    monkeypatch.setitem(sys.modules, "sentence_transformers", types.SimpleNamespace(SentenceTransformer=FakeSentenceTransformer))

    from app.embedding_provider import EmbeddingProvider

    provider = EmbeddingProvider(
        EmbeddingConfig(
            provider="sentence_transformers",
            model_name="emb",
            local_model_path=str(local_dir),
            download_if_missing=True,
            batch_size=8,
            normalize_embeddings=True,
        )
    )
    assert provider.model_path == str(local_dir)


def test_embedding_provider_missing_model_downloads_when_allowed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    local_dir = tmp_path / "emb"
    calls: list[tuple[str, str | None]] = []

    class FakeSentenceTransformer:
        def __init__(self, model_name_or_path, device=None):
            calls.append((model_name_or_path, device))

        def save(self, path):
            Path(path).mkdir(parents=True, exist_ok=True)

    monkeypatch.setitem(sys.modules, "sentence_transformers", types.SimpleNamespace(SentenceTransformer=FakeSentenceTransformer))

    from app.embedding_provider import EmbeddingProvider

    provider = EmbeddingProvider(
        EmbeddingConfig(
            provider="sentence_transformers",
            model_name="emb",
            local_model_path=str(local_dir),
            download_if_missing=True,
            batch_size=8,
            normalize_embeddings=True,
        )
    )
    assert provider.model_path == str(local_dir)
    assert local_dir.exists()
    assert len(calls) == 1


def test_embedding_provider_missing_model_uses_hashing_when_download_disabled(tmp_path: Path) -> None:
    from app.embedding_provider import EmbeddingProvider

    provider = EmbeddingProvider(
        EmbeddingConfig(
            provider="sentence_transformers",
            model_name="emb",
            local_model_path=str(tmp_path / "missing-emb"),
            download_if_missing=False,
            batch_size=8,
            normalize_embeddings=True,
        )
    )
    assert provider.model_path == "__hashing_fallback__"
