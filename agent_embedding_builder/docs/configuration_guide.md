# Configuration Guide

## Which file a normal user edits

Edit `config/builder_config.jsonc`.

It is the primary operator-facing config and supports comments. It contains only the settings a normal project operator should reasonably change:
- database connection
- input roots and file filters
- local LLM model path and download source

## Which files are internal/system-owned

These files are system-owned and normally should not be edited during day-to-day use:
- `config/builder_defaults.json`
- `config/builder_config.schema.json`
- `config/category_ontology.json`
- `config/category_ontology.schema.json`

`builder_defaults.json` contains specialized internal defaults and policy knobs that require builder-specific knowledge.
It is not intended for normal users and should not be part of day-to-day editing.
It owns the less frequently changed sections such as embedding defaults, ontology wiring, runtime mode/logging, and other internal tuning values.

## Why some config is intentionally hidden

Fields such as segmentation thresholds, rebuild-trigger matrices, LLM inference tuning, embedding batch knobs, and version identifiers are implementation-policy settings rather than ordinary operator inputs. Keeping them out of the main operator config reduces accidental misconfiguration and keeps the editable surface understandable.

## How ontology config works

`config/category_ontology.json` is the canonical machine-readable category governance artifact. The builder loads it, validates it against `config/category_ontology.schema.json`, then uses it to:
- normalize aliases
- rewrite deprecated labels when replacements exist
- reject invalid labels when unknown labels are disallowed
- guide lexical matching and retrieval-class resolution

## How front matter and sidecar metadata interact with categorization

Metadata precedence is:
1. markdown front matter
2. sidecar metadata such as `foo.txt.meta.json`
3. path and structural hints
4. rule-based inference
5. local LLM inference
6. ontology normalization
7. stable retrieval-class resolution

All configured filesystem paths support both relative and absolute forms.
Relative paths are resolved against the chosen config file's directory, not the current working directory.
If you use `--config` or `EMBEDDING_BUILDER_CONFIG` to load a config from another folder, paths inside that config resolve relative to that config file.
On Windows, prefer forward slashes such as `C:/Docs/Repo/github/pipesim/pip-pipesim/Stingray/.ai/docs/`.
For supported path fields, backslash Windows paths are also accepted when they are represented as valid JSONC string values.
UNC paths such as `\\server\share\models\qwen.gguf` are also accepted for supported path fields.

## LLM model path behavior

`llm.local_model_path` is the exact final GGUF location used by the runtime.
If a valid model already exists there, it is used directly.
If it does not exist and downloading is enabled, the model may be staged in `llm.download_source.local_dir`, but the final usable GGUF is copied into exactly `llm.local_model_path`.
Tiny placeholder files are rejected and are not treated as valid models.

## Recursive discovery

Each `knowledge_base.input_roots` entry is treated as a corpus root.
The builder scans every nested subfolder recursively for eligible `.md` and `.txt` files.
Symlinked files and directories are included only when `knowledge_base.follow_symlinks` is `true`.
When symlink following is enabled, discovered source paths remain the visible paths under the configured root rather than collapsing to the physical symlink target path.

## Folder sync behavior

`runtime.sync.reconcile_removed_files` and `runtime.sync.archive_removed_artifacts` default to `true`.
With those settings enabled, each run reconciles the database against the current contents of the configured input roots:
- new files are inserted
- changed files supersede prior active knowledge rows
- deleted files mark artifacts as removed and archive their active knowledge rows

## How to start the builder

```bash
python agent_embedding_builder.py
```

Optional override:

```bash
python agent_embedding_builder.py --config ./config/builder_config.jsonc --mode build-all
```
