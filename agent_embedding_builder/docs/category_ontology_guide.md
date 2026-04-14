# Category Ontology Guide

`config/category_ontology.json` is the canonical machine-readable category governance artifact for the builder.

It defines:
- canonical labels
- aliases and deprecations
- confidence floors
- lexical hints
- default retrieval-class mappings
- special resolution rules

Metadata authors may use these fields in markdown front matter or sidecar metadata:
- `title`
- `domain`
- `module`
- `feature`
- `labels`
- `authority_label`
- `status`
- `tags`
- `source_type`

Normalization behavior:
- aliases are rewritten to canonical labels
- deprecated labels are rewritten to replacements when defined
- unknown labels fail when `allow_unknown_labels` is `false`
