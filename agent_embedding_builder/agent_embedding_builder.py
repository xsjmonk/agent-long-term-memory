from __future__ import annotations

import argparse
import json
from pathlib import Path

from app.config_loader import load_config
from app.logging_setup import configure_logging
from app.pipeline import run_pipeline


def main() -> int:
    parser = argparse.ArgumentParser(description="Local-LLM embedding builder")
    parser.add_argument("--mode", choices=["build-all", "build-changed"], default=None)
    parser.add_argument("--config", default=None)
    args = parser.parse_args()

    root = Path(__file__).resolve().parent
    config = load_config(root, override=args.config)
    if args.mode:
        config.builder.mode = args.mode
    configure_logging(config.logging.level)
    stats = run_pipeline(root, config)
    print(json.dumps(stats.as_dict(), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
