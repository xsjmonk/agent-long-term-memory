from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field


@dataclass
class BuildStats:
    processed_files: int = 0
    skipped_files: int = 0
    failed_files: int = 0
    created_items: int = 0
    reconciled_removed_files: int = 0
    superseded_items: int = 0
    archived_items: int = 0
    labels: Counter = field(default_factory=Counter)
    retrieval_classes: Counter = field(default_factory=Counter)

    def as_dict(self) -> dict:
        return {
            "processed_files": self.processed_files,
            "skipped_files": self.skipped_files,
            "failed_files": self.failed_files,
            "created_items": self.created_items,
            "reconciled_removed_files": self.reconciled_removed_files,
            "superseded_items": self.superseded_items,
            "archived_items": self.archived_items,
            "labels": dict(self.labels),
            "retrieval_classes": dict(self.retrieval_classes),
        }
