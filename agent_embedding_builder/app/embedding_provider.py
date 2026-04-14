from __future__ import annotations

import logging
from pathlib import Path
from typing import Any

from app.models import EmbeddingConfig

logger = logging.getLogger(__name__)

_HASHING_FALLBACK_MODEL = "__hashing_fallback__"


def _detect_auto_device() -> str:
    try:
        import torch

        if torch.cuda.is_available():
            return "cuda"
        mps_backend = getattr(torch.backends, "mps", None)
        if mps_backend is not None and mps_backend.is_available():
            return "mps"
    except Exception:
        pass
    return "cpu"


def _resolve_device(force_device: str) -> str:
    """
    Resolve the embedding device with explicit operator override.

    Allowed values:
      - ""     -> auto-detect
      - "cpu"  -> force CPU
      - "cuda" -> require torch.cuda.is_available()
      - "mps"  -> require torch.backends.mps.is_available()
    """

    if force_device == "":
        return _detect_auto_device()
    if force_device == "cpu":
        return "cpu"

    import torch

    if force_device == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("embedding.force_device=cuda requested but torch.cuda.is_available() is false.")
        return "cuda"

    if force_device == "mps":
        mps_backend = getattr(torch.backends, "mps", None)
        if mps_backend is None or not mps_backend.is_available():
            raise RuntimeError("embedding.force_device=mps requested but MPS is not available in this torch build.")
        return "mps"

    raise ValueError(f"Unsupported embedding force_device={force_device!r}.")


class EmbeddingProvider:
    def __init__(self, config: EmbeddingConfig) -> None:
        self.config = config
        self.device = _resolve_device(config.force_device or "")
        self.model_path = self.ensure_model()
        self._model = None

    def ensure_model(self) -> str:
        local_dir = Path(self.config.local_model_path)
        if local_dir.exists():
            logger.info("Embedding model ready at %s", local_dir)
            return str(local_dir)
        if not self.config.download_if_missing:
            logger.warning("Embedding model directory %s is missing; using hashing fallback embeddings.", local_dir)
            return _HASHING_FALLBACK_MODEL
        from sentence_transformers import SentenceTransformer

        local_dir.parent.mkdir(parents=True, exist_ok=True)
        try:
            logger.info("Downloading embedding model %s into %s using device=%s", self.config.model_name, local_dir, self.device)
            SentenceTransformer(self.config.model_name, device=self.device).save(str(local_dir))
            logger.info("Embedding model is ready at %s", local_dir)
            return str(local_dir)
        except Exception as exc:
            logger.warning("Falling back to local hashing embeddings because model download failed: %s", exc)
            return _HASHING_FALLBACK_MODEL

    def _load(self) -> Any:
        if self._model is None:
            if self.model_path == _HASHING_FALLBACK_MODEL:
                from sklearn.feature_extraction.text import HashingVectorizer

                logger.debug("Using hashing fallback embeddings.")
                self._model = HashingVectorizer(
                    n_features=384,
                    alternate_sign=False,
                    norm="l2",
                    ngram_range=(1, 2),
                )
                return self._model
            from sentence_transformers import SentenceTransformer

            logger.info("Embedding model loaded path=%s device=%s", self.model_path, self.device)
            self._model = SentenceTransformer(self.model_path, device=self.device)
        return self._model

    def embed(self, texts: list[str]) -> list[list[float]]:
        logger.debug("Embedding %s text item(s) using device=%s.", len(texts), self.device)
        model = self._load()
        if self.model_path == _HASHING_FALLBACK_MODEL:
            matrix = model.transform(texts).toarray()
            return [list(map(float, row)) for row in matrix]
        vectors = model.encode(
            texts,
            batch_size=self.config.batch_size,
            normalize_embeddings=self.config.normalize_embeddings,
            show_progress_bar=False,
            convert_to_numpy=True,
        )
        return [list(map(float, vector)) for vector in vectors]

    def model_metadata(self) -> tuple[str, str | None]:
        if self.model_path == _HASHING_FALLBACK_MODEL:
            return self.model_path, "hashing-384"
        return self.model_path, None
