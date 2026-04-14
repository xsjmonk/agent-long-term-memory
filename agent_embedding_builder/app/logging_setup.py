from __future__ import annotations

import logging


def configure_logging(level: str) -> None:
    resolved_level = getattr(logging, level.upper(), logging.INFO)
    logging.basicConfig(
        level=resolved_level,
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )

    # Silence third-party noisy loggers by default.
    for noisy_name in [
        "sentence_transformers",
        "transformers",
        "torch",
        "urllib3",
        "huggingface_hub",
    ]:
        logging.getLogger(noisy_name).setLevel(logging.WARNING)
