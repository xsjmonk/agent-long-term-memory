# Harness Agent-Integrated Control-Flow Design

## 1. Purpose

This revision closes the remaining gap between:

- a **deterministic harness runtime**, and
- a **real agent-facing workflow** where the end user stays entirely inside the agent UI.

The current harness source now has a meaningful machine-readable protocol:

- `plan-task`
- `describe-protocol`
- `--stdout-json true`
- `HarnessRunManifest`
- `nextAction`
- deterministic artifact output
- strict MCP call order inside the runner

That is good, but it is still not enough for the required user experience.

The missing piece is the **agent integration layer** that makes the harness invisible to the user and gives the planning agent a strict, deterministic entrypoint.

This document defines that missing layer.

---

## 2. What the current source already solved

Based on the fixed source, the harness already implements important control-plane mechanics:

1. a separate client-side harness runtime
2. deterministic planning stages
3. MCP preflight
4. required primary call order:
   - `get_server_info`
   - `retrieve_memory_by_chunks`
   - `merge_retrieval_results`
   - `build_memory_context_pack`
5. plan synthesis after memory retrieval
6. worker-packet generation
7. machine-readable stdout manifest
8. protocol self-description via `describe-protocol`

That means the harness executable itself is no longer the main problem.

The remaining problem is that the **agent is not yet forced to use it automatically**.

---

## 3. Why the current repo still does not satisfy the intended user flow

The desired flow is:

1. user gives task description in the agent
2. harness controls the agent to retrieve memory
3. agent analyzes retrieved memory
4. agent creates a detailed plan
5. user uses the plan to drive another agent

The current repo still falls short in one decisive way:

- it has a **harness runtime protocol**
- but it does **not yet have an in-agent integration protocol bundle**

So the burden is still on the agent implementation or the human operator to know:

- when to invoke the harness
- how to invoke it
- how to interpret success/failure
- which artifact to consume next
- when to stop raw planning and switch to worker execution mode

That is exactly the gap the user is objecting to.

---

## 4. Final architecture decision

## 4.1 Keep the harness runtime

Keep `HarnessMcp.AgentClient` as the control-plane runtime.

Do not move planning into the MCP server.
Do not let the execution worker retrieve memory directly.
Do not replace the harness with ad hoc agent prompting.

## 4.2 Add an agent integration layer

Add a lightweight integration bundle that sits **above** the harness runtime and **inside the repository**.

This integration bundle exists only to make agent behavior deterministic and invisible to the end user.

It must include:

1. **agent rule files / skills**
2. **one hidden stable invocation surface**
3. **one stable machine-readable result manifest**
4. **one stable execution handoff artifact**
5. **tests that verify the integration contract**

The user must only chat with the agent.
The agent must decide to invoke the harness.
The user must not be asked to run commands manually.

---

## 5. Budget-saving implementation strategy

To keep cost low, do **not** invent a second runtime or a plugin framework.

Reuse what already exists:

- the current harness executable
- the current stdout manifest
- the current deterministic artifacts
- the existing hidden PowerShell wrapper script

Only add the minimal repository files needed so an external coding agent can use the harness automatically and consistently.

That means:

- do not redesign the harness core
- do not redesign the MCP server
- do not add autonomous agent loops
- do not add a general-purpose orchestration service
- do not add browser UI
- do not add persistent job queues

---

## 6. Final integration model

## 6.1 User-visible behavior

The user interacts only with the coding agent.

Example:

> Add year switching to the yearly weighted card without changing engine logic.

The user does not mention:
- harness
- command line
- PowerShell
- output directories
- manifests
- MCP tool order

The agent is responsible for the hidden flow.

## 6.2 Hidden agent behavior

When a non-trivial task is received, the planning agent must:

1. write the raw user task into a temporary task file or pass it as `--task-text`
2. call the hidden harness invocation surface
3. consume `HarnessRunManifest`
4. if success:
   - read `10-execution-plan.md`
   - read `11-worker-packet.md`
   - present the plan to the user
   - if the user wants execution in another agent, hand off `11-worker-packet.md`
5. if failure:
   - stop
   - report harness validation/retrieval failure
   - do not continue with speculative planning

This is the deterministic control-plane flow.

---

## 7. Files that must be added

The current repo still needs the following files.

## 7.1 Agent rule / skill files

Add repository-local agent-control files.

### A. `.cursor/rules/00-harness-planning.mdc`
Purpose:
- force planning-first flow
- require hidden harness invocation before implementation work
- forbid direct planning from raw task on non-trivial requests

Core rules:
- for any non-trivial implementation/refactor/design/debug request, invoke the harness first
- do not start coding before harness success
- use `12-harness-run-manifest.json` or stdout JSON as the authoritative result
- use `11-worker-packet.md` as the execution handoff
- do not retrieve long-term memory independently outside the harness flow

### B. `.cursor/rules/01-harness-failure.mdc`
Purpose:
- force safe handling on harness failure

Core rules:
- if harness returns `success=false`, stop the planning flow
- show the harness errors
- do not continue with replacement planning
- do not silently degrade into normal agent behavior

### C. `.cursor/rules/02-harness-execution.mdc`
Purpose:
- constrain the execution agent

Core rules:
- if operating from a worker packet, do not retrieve long-term memory independently
- do not replace the plan
- do not expand scope
- do not reinterpret architecture unless the worker packet explicitly allows it

These files are the cheapest way to give the agent a deterministic behavior envelope.

## 7.2 Stable hidden invocation surface

Keep the user away from raw CLI arguments by defining one internal entry surface.

Use the existing script path pattern and standardize it:

### `Scripts/invoke-harness-plan.ps1`
This script is **for the agent**, not for the user.

Responsibilities:
1. accept:
   - `-TaskText`
   - `-OutputDir`
   - optional model/MCP overrides
2. call `HarnessMcp.AgentClient.exe plan-task`
3. force `--stdout-json true`
4. optionally force `--print-worker-packet false`
5. echo only the JSON manifest to stdout
6. return non-zero exit code when harness fails

This is the stable bridge between agent and harness runtime.

The user never sees it.

## 7.3 Protocol documentation file

Keep and expand:

### `docs/harness_protocol.md`

It must describe:
- when the planning agent must invoke harness
- exact success/failure semantics
- how to consume `HarnessRunManifest`
- how to use `nextAction`
- how to move from planning to execution agent
- what is forbidden for the execution worker

This doc is for agents/repo maintainers, not for the end user.

## 7.4 Agent integration documentation

Add:

### `docs/agent_controlled_flow.md`

This is a higher-level integration design doc describing:

- user-facing flow
- hidden harness invocation
- planning-agent responsibilities
- execution-agent responsibilities
- failure behavior
- handoff contract
- repository-local rule files
- why the user must remain unaware of harness details

This file should become the definitive documentation for the integrated flow.

---

## 8. Strict deterministic entrypoint

## 8.1 Why the current source is still not a strict agent entrypoint

Even with `describe-protocol`, the current harness is still only a deterministic **tool**.
It is not yet a deterministic **agent entrypoint**.

A strict agent entrypoint must answer:

- when must the agent call the harness?
- what exact hidden command must the agent use?
- what exact stdout contract must the agent parse?
- what exact file must the agent consume next?
- when is the agent forbidden from continuing?

The current code only covers the lower half of that contract.

## 8.2 Final strict entrypoint design

The strict agent entrypoint is:

1. repository rule triggers harness on qualifying tasks
2. agent calls `Scripts/invoke-harness-plan.ps1`
3. wrapper calls `HarnessMcp.AgentClient.exe plan-task --stdout-json true`
4. wrapper returns only `HarnessRunManifest`
5. agent branches on:
   - `success`
   - `nextAction`
6. on success, planning agent reads:
   - `10-execution-plan.md`
   - `11-worker-packet.md`
7. execution agent uses only `11-worker-packet.md`

This is deterministic and machine-driven.

---

## 9. Required manifest semantics

The existing manifest is good, but the integration contract must standardize how agents use it.

`HarnessRunManifest` must remain the canonical contract.

Required semantics:

- `success=true` means the agent may continue
- `success=false` means the agent must stop planning
- `nextAction=paste_worker_packet_into_execution_agent` means the planning phase succeeded and the next phase is execution handoff
- `nextAction=fix_errors_and_rerun_harness` means the agent must not continue with manual planning
- `workerPacketMarkdownPath` is the authoritative execution-handoff file
- `executionPlanMarkdownPath` is the authoritative planning-summary file

Optional:
- `workerPacketText` may be embedded only when explicitly requested; default should remain false to keep output small

---

## 10. Planning agent behavior contract

The planning agent must:

1. accept the user task
2. decide whether the task is trivial or non-trivial
3. for non-trivial tasks, invoke the harness before planning
4. consume only the harness result manifest
5. present the generated plan to the user
6. preserve the worker packet for execution handoff

The planning agent must not:

- plan directly from raw task for non-trivial work
- retrieve memory directly outside harness
- create a competing plan after harness failure
- skip the worker packet handoff model

---

## 11. Execution agent behavior contract

The execution agent receives only the worker packet.

It must:

- execute the listed steps
- stay within scope
- honor hard constraints
- honor forbidden actions
- produce the required output sections

It must not:

- retrieve long-term memory independently
- regenerate architecture decisions
- expand scope
- replace the plan

This matches the original design intent.

---

## 12. Task classification rule

The agent integration needs a simple trigger rule so the harness is not called for every message.

Use this budget-saving rule:

### Trivial task — harness optional
Examples:
- explain a concept
- small text rewrite
- single obvious one-line change
- answer-only discussion

### Non-trivial task — harness mandatory
Examples:
- implementation task
- refactor
- bugfix with investigation
- architecture change
- design review that may turn into implementation
- anything requiring repo changes across multiple files
- anything where memory retrieval can improve quality

This trigger rule belongs in the planning-agent rule file.

---

## 13. Required repository outputs after integration

After the integration work, the repo should contain at least:

```text
docs/
  harness_protocol.md
  agent_controlled_flow.md

Scripts/
  invoke-harness-plan.ps1

.cursor/
  rules/
    00-harness-planning.mdc
    01-harness-failure.mdc
    02-harness-execution.mdc
```

The harness runtime remains under:

```text
src/HarnessMcp.AgentClient/
```

No second runtime host is needed.

---

## 14. Required tests

The existing tests validate harness runtime behavior, but not the agent integration contract.

Add these tests.

### A. `ProtocolDescriptionTests`
Extend to verify:
- `describe-protocol` clearly states the harness is to be invoked before execution work
- `nextAction` meaning is stable
- worker-packet handoff is explicit

### B. `PlanTaskCommandProtocolTests`
Verify:
- `--stdout-json true` returns exactly one JSON object
- success manifest contains `nextAction=paste_worker_packet_into_execution_agent`
- failure manifest contains `nextAction=fix_errors_and_rerun_harness`

### C. `InvokeHarnessPlanScriptTests`
Add script-level tests that verify the wrapper:
- always passes `--stdout-json true`
- returns only JSON to stdout
- exits non-zero on harness failure

These can be string/content tests if you want to keep cost low.

### D. `AgentRuleContentTests`
Add low-cost tests that validate rule-file contents:
- planning rule contains mandatory harness-first instructions
- failure rule forbids fallback manual planning
- execution rule forbids worker-side memory retrieval

### E. `PlanningSessionRunnerFlowTests`
Keep and extend:
- execution plan synthesis happens only after `build_memory_context_pack`
- worker packet is not written when plan validation fails

These tests are cheap and directly protect the integration contract.

---

## 15. Design decision on “skills”

You asked whether skills may be needed.

Final decision:

- **Yes, add skills/rules**
- but do **not** rely on skills alone

Reason:
- skills/rules tell the agent **when and how** to use the harness
- the harness executable and manifest still provide the hard deterministic protocol
- together they are stronger than either one alone

So the final design is:

- harness runtime = deterministic control-plane engine
- agent rules/skills = deterministic in-agent trigger and behavior envelope

That is the lowest-cost design that satisfies the desired UX.

---

## 16. Final assessment of the current source

## 16.1 Does the current source strictly control the agent yet?

Not yet.

It already provides:
- deterministic runtime protocol
- machine-readable manifest
- explicit next action
- hidden-tool compatibility

But it still lacks:
- repository-local agent rule files / skills
- standardized hidden wrapper usage contract
- integrated flow documentation for planning-agent vs execution-agent
- tests for the agent-integration layer

So the source is **necessary but not sufficient**.

## 16.2 Can it give the agent a strict entrypoint and deterministic flow right now?

Partially.

It gives a deterministic **runtime** entrypoint.
It does not yet give a deterministic **agent integration** entrypoint.

That is the exact gap this revision closes.

---

## 17. Final budget-saving decision summary

Keep:
- current harness runtime
- current manifest design
- current deterministic planning flow
- current tests for runtime behavior

Add only:
1. rule/skill files
2. hidden wrapper contract
3. integration docs
4. small integration tests

Do not add:
- extra runtime services
- autonomous orchestration loops
- plugin frameworks
- user-facing commands
- UI surfaces
- extra agent servers

This is the minimum complete solution.

---

## 18. Final integrated flow

```text
User
  -> talks only to coding agent

Planning Agent
  -> detects non-trivial task
  -> invokes hidden harness wrapper
  -> wrapper calls HarnessMcp.AgentClient.exe plan-task --stdout-json true
  -> harness performs deterministic retrieval + planning flow
  -> harness writes artifacts and returns HarnessRunManifest
  -> planning agent reads manifest
  -> planning agent shows generated plan
  -> planning agent provides worker packet for execution

Execution Agent
  -> receives only worker packet
  -> executes
  -> does not retrieve memory independently
```

This is the final complete integration model.
