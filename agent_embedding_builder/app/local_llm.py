from __future__ import annotations

import fnmatch
import json
import logging
import shutil
from pathlib import Path
from typing import Any

from pydantic import ValidationError

from app.models import LLMConfig, LLMInferenceResult

logger = logging.getLogger(__name__)


def extract_json_object(text: str) -> str:
    stripped = text.strip()
    if stripped.startswith("```"):
        lines = stripped.splitlines()
        if lines and lines[0].startswith("```"):
            lines = lines[1:]
        if lines and lines[-1].strip() == "```":
            lines = lines[:-1]
        stripped = "\n".join(lines).strip()

    start = stripped.find("{")
    if start == -1:
        raise ValueError("No JSON object start found in local LLM output.")

    depth = 0
    in_string = False
    escape = False
    for index in range(start, len(stripped)):
        ch = stripped[index]
        if in_string:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
            continue
        if ch == "{":
            depth += 1
            continue
        if ch == "}":
            depth -= 1
            if depth == 0:
                return stripped[start : index + 1]

    raise ValueError("No complete JSON object found in local LLM output.")


def detect_llama_gpu_layers(configured_gpu_layers: int) -> int:
    if configured_gpu_layers > 0:
        return configured_gpu_layers
    try:
        import torch

        if torch.cuda.is_available():
            return -1
        mps_backend = getattr(torch.backends, "mps", None)
        if mps_backend is not None and mps_backend.is_available():
            return -1
    except Exception:
        pass
    return configured_gpu_layers


def validate_local_gguf_model(path: Path) -> None:
    logger.debug("Validating local GGUF model at %s", path)
    if not path.exists():
        raise FileNotFoundError(f"Configured LLM model path does not exist: {path}")
    if not path.is_file():
        raise ValueError(f"Configured LLM model path must be a file: {path}")
    if path.suffix.lower() != ".gguf":
        raise ValueError(f"Configured LLM model path must end with .gguf: {path}")
    if path.stat().st_size < 1024 * 1024:
        raise ValueError(f"Configured LLM model file is too small to be a valid GGUF model: {path}")
    logger.debug("Validated local GGUF model at %s (%s bytes)", path, path.stat().st_size)


class LocalLLM:
    def __init__(self, config: LLMConfig) -> None:
        self.config = config
        self.model_path = self.ensure_model()
        self._llm = None
        self.runtime_gpu_layers = detect_llama_gpu_layers(config.gpu_layers)

    def ensure_model(self) -> Path:
        configured = Path(self.config.local_model_path)
        if configured.exists():
            try:
                validate_local_gguf_model(configured)
                logger.debug("Using local LLM model from %s", configured)
                return configured
            except (ValueError, FileNotFoundError):
                if not self.config.download_if_missing:
                    raise
                logger.warning("Configured LLM path exists but is not valid; re-downloading model to %s", configured)
        if not self.config.download_if_missing:
            raise FileNotFoundError(f"Configured LLM model path does not exist: {configured}")
        from huggingface_hub import hf_hub_download, snapshot_download

        configured.parent.mkdir(parents=True, exist_ok=True)
        local_dir = Path(self.config.download_source.local_dir)
        local_dir.mkdir(parents=True, exist_ok=True)
        filename = self.config.download_source.filename
        logger.debug(
            "Preparing local LLM model. final_path=%s staging_dir=%s requested_filename=%s",
            configured,
            local_dir,
            filename,
        )
        if "*" in filename:
            preferred_filename = (
                configured.name
                if configured.suffix.lower() == ".gguf" and "*" not in configured.name and fnmatch.fnmatch(configured.name, filename)
                else None
            )
            if preferred_filename:
                try:
                    downloaded_path = Path(
                        hf_hub_download(
                            repo_id=self.config.download_source.repo_id,
                            filename=preferred_filename,
                            local_dir=str(local_dir),
                        )
                    )
                    logger.info(
                        "Downloaded LLM model directly from configured final filename to staging path: %s",
                        downloaded_path,
                    )
                except Exception:
                    downloaded_path = None
            else:
                downloaded_path = None
            if downloaded_path is None:
                snapshot_dir = Path(
                    snapshot_download(
                        repo_id=self.config.download_source.repo_id,
                        local_dir=str(local_dir),
                        allow_patterns=[filename],
                    )
                )
                matches = sorted(path for path in snapshot_dir.rglob("*") if path.is_file() and fnmatch.fnmatch(path.name, filename))
                if not matches:
                    matches = sorted(path for path in snapshot_dir.rglob("*.gguf") if path.is_file())
                if not matches:
                    raise FileNotFoundError("Downloaded snapshot did not contain a matching GGUF file.")
                downloaded_path = matches[0]
        else:
            downloaded_path = Path(
                hf_hub_download(
                    repo_id=self.config.download_source.repo_id,
                    filename=filename,
                    local_dir=str(local_dir),
                )
            )
            logger.debug("Downloaded LLM model to staging path: %s", downloaded_path)
        final_path = configured
        if downloaded_path.resolve(strict=False) != final_path.resolve(strict=False):
            shutil.copy2(downloaded_path, final_path)
            logger.debug("Copied LLM model into canonical final path: %s", final_path)
        else:
            logger.debug("LLM model already matches canonical final path: %s", final_path)
        validate_local_gguf_model(final_path)
        logger.info("Local LLM model is ready at %s", final_path)
        return final_path

    def _load_runtime(self) -> Any:
        if self._llm is None:
            logger.debug("Importing llama_cpp Python bindings...")
            from llama_cpp import Llama

            logger.debug(
                "Creating llama.cpp runtime. model=%s context_length=%s requested_gpu_layers=%s runtime_gpu_layers=%s seed=%s",
                self.model_path,
                self.config.context_length,
                self.config.gpu_layers,
                self.runtime_gpu_layers,
                self.config.seed,
            )
            try:
                self._llm = Llama(
                    model_path=str(self.model_path),
                    n_ctx=self.config.context_length,
                    seed=self.config.seed,
                    n_gpu_layers=self.runtime_gpu_layers,
                    verbose=False,
                )
            except Exception:
                if self.runtime_gpu_layers != 0:
                    logger.warning(
                        "llama.cpp runtime creation failed with GPU offload=%s; retrying on CPU-only mode.",
                        self.runtime_gpu_layers,
                    )
                    self.runtime_gpu_layers = 0
                    self._llm = Llama(
                        model_path=str(self.model_path),
                        n_ctx=self.config.context_length,
                        seed=self.config.seed,
                        n_gpu_layers=0,
                        verbose=False,
                    )
                else:
                    raise
            logger.info("llama.cpp runtime is ready for %s", self.model_path)
        return self._llm

    def infer(self, prompt: str, retries: int = 2) -> LLMInferenceResult:
        if not prompt.strip():
            return LLMInferenceResult()
        logger.debug("Starting local LLM inference. prompt_chars=%s retries=%s", len(prompt), retries)
        runtime = self._load_runtime()
        for attempt in range(retries + 1):
            logger.debug("Running local LLM completion attempt %s/%s", attempt + 1, retries + 1)
            response = runtime.create_completion(
                prompt=prompt,
                max_tokens=self.config.max_tokens,
                temperature=self.config.temperature,
            )
            content = response["choices"][0]["text"].strip()
            try:
                payload = json.loads(extract_json_object(content))
                logger.debug("Local LLM returned valid JSON on attempt %s/%s", attempt + 1, retries + 1)
                return LLMInferenceResult.model_validate(payload)
            except (ValueError, json.JSONDecodeError, ValidationError):
                logger.warning("Local LLM returned non-JSON or invalid JSON on attempt %s/%s", attempt + 1, retries + 1)
                continue
        raise ValueError("Local LLM returned invalid JSON after bounded retries.")
