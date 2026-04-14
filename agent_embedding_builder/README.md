```
conda env create -f environment.yml
conda activate agent-embedding-builder
pwsh Scripts/run_builder.ps1
```

# Important notes
conda env should have `llama-cpp-python`

# Agent Embedding Builder

Local-LLM embedding builder for retrieval-ready long-term memory storage. The builder ingests `.md` and `.txt` files, creates hierarchical candidate spans, infers dynamic labels, resolves a stable `retrieval_class`, generates retrieval text and profiles, creates local embeddings, and writes into PostgreSQL + pgvector.

## Architecture Summary

- `agent_embedding_builder.py`: root no-arg entrypoint
- `app/config_loader.py`: config path resolution and pydantic validation
- `app/file_discovery.py`: `.md` and `.txt` discovery
- `app/markdown_parser.py` / `app/text_parser.py`: source parsing
- `app/segmenter.py`: file / section / subsection / paragraph-group candidates
- `app/category_rules.py` + `app/local_llm.py` + `app/category_inference.py`: layered classification
- `app/class_resolution.py`: stable retrieval-class mapping
- `app/scope_tag_extractor.py`: scopes and tags
- `app/retrieval_profile_builder.py`: route-specific retrieval text
- `app/embedding_provider.py`: local embedding model loading
- `app/database.py`: transactional persistence
- `app/pipeline.py`: explicit stage orchestration

## Required Config

Edit `config/builder_config.jsonc`.

System-owned config and governance artifacts live under `config/`:
- `config/builder_config.jsonc`
- `config/builder_defaults.json`
- `config/builder_config.schema.json`
- `config/category_ontology.json`
- `config/category_ontology.schema.json`

Config lookup order:

1. explicit `--config <path>`
2. `EMBEDDING_BUILDER_CONFIG`
3. `./config/builder_config.jsonc`
4. deprecated fallback: `./config/builder_config.json`

Path notes:
- relative config paths resolve from the active config file directory
- absolute paths are supported
- on Windows, prefer forward slashes like `C:/Docs/Repo/github/pipesim/pip-pipesim/Stingray/.ai/docs/`
- for supported path fields, ordinary backslash Windows paths are also tolerated
- knowledge-base roots are scanned recursively, and symlink behavior is controlled by `knowledge_base.follow_symlinks`

## Conda Setup

```bash
conda env create -f environment.yml
conda activate agent-embedding-builder
```

`environment.yml` is the single source of truth for the runtime environment. `Scripts/run_builder.ps1` activates the named Conda environment and then validates the required runtime packages/capabilities are already present (it does not install or repair dependencies).

## Database Initialization

```bash
# `Scripts/run_builder.ps1` initializes the DB schema automatically (unless `-SkipDatabase` is provided).
# Make sure your PostgreSQL instance has required extensions (e.g., `pgvector`) available before running.
```

If `pgvector` is missing, install the extension in your PostgreSQL instance before running `run_builder.ps1`.

## Local Model Auto-Download

- If `llm.local_model_path` does not exist and `llm.download_if_missing=true`, the builder downloads the configured GGUF model from Hugging Face into `llm.download_source.local_dir`.
- If `embedding.local_model_path` does not exist and `embedding.download_if_missing=true`, the embedding model is downloaded locally and reused on later runs.
- After the model exists locally, inference is fully local.

## Run

The default command is exactly (from the project root):

```bash
pwsh Scripts/run_builder.ps1
```

Optional override:

```bash
pwsh Scripts/run_builder.ps1 -ConfigPath ./config/builder_config.jsonc
```

## Build Modes

- `build-changed`: default; only rebuilds artifacts whose hash or pipeline/model versions changed
- `build-all`: rebuilds all discovered artifacts

## Troubleshooting

- Missing model: verify `config/builder_config.jsonc` paths or enable auto-download.
- Invalid config: the process fails fast with pydantic validation errors.
- DB connection failure: verify host, port, username, password, and database name in config.
- `pgvector` failure: ensure the extension is installed and `CREATE EXTENSION vector;` succeeds.

### CUDA available, but llama-cpp-python GPU offload missing
`torch.cuda.is_available()` being `true` only means PyTorch can access CUDA. It does **not** guarantee that `llama-cpp-python` in your Conda environment was built with CUDA GPU offload support.

If `Scripts/run_builder.ps1` prints a warning with:
- PyTorch CUDA is available, but
- llama-cpp-python GPU offload is NOT available,
then the launcher will not install/repair anything and will continue launching the application using CPU fallback. If you want llama GPU offload, recreate the Conda environment from the updated `environment.yml`:
`conda env remove -n agent-embedding-builder` then `conda env create -f environment.yml`.
