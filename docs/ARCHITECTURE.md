# Architecture

## Principle: authority flows one way

```
 Player text ──► Interpreter (LLM w/ JSON schema │ rule-based) ──► ActionProposal (untrusted)
                                                                     │
                                         ActionValidator: allowlist, strict params, authority, money
                                                                     │
                                SimulationEngine.Submit ──► state mutation + DecisionRecord
                                                                     │
                     StanceEngine (deterministic positions) ──► Dialogue (LLM │ composer) ──► validator ──► UI
```

* The **engine** (`SimulationEngine`) is the only code that mutates `GameState`. The UI and the AI
  layer produce `ActionProposal`s, and the UI buttons use the same path as typed dialogue.
* The **action registry** is an allowlist. Unknown actions and unknown parameters are rejected. The
  JSON schema the model must follow is *generated from the registry*, so the grammar and the validator
  cannot drift apart.
* **Stances** (who supports or opposes what, and why) are computed from game state, executive
  priorities, biases and trust. The model is told the stance and may only voice it.
* **Dialogue validation** rejects output that breaks character, cites dollar figures not in the facts it
  was given, contradicts the computed stance, claims actions the engine did not perform, or is not
  plausible English (protection against a broken model). Rejected output falls back to the deterministic
  `DialogueComposer`.
* **Grounding:** money and deadlines in a model-interpreted action must appear in what the player
  actually said. Otherwise the game asks rather than guesses. Conduct detection (manipulation, threats,
  self-praise) is always rule-based. When the rules and the model disagree, a valid rule-based
  interpretation wins.

## Module boundaries (`src/ExecutiveCommand.Core`)

| Namespace | Responsibility |
|---|---|
| `Model` | Plain serializable state: company, executives, departments and budget lines, finance and ledger, technology (including **hidden ground truth**), proposals, projects, assignments, savings, risks, incidents, contracts, decisions, memory, inbox, reviews |
| `Simulation` | `DeterministicRng` (SplitMix64, state persisted), `TextCulture` |
| `Engine` | `SimulationEngine` (orchestrator, daily tick); `FinancialEngine` (monthly close, reconciliation, fiscal rollover); `ProjectEngine` and `ProjectEffects`; `AssignmentEngine` and `Findings` (delegation outcomes); `EventEngine` (scheduled business, risk-driven hazards, CFO/CEO approvals); `IncidentEngine` (cutover lab-interface incident, CMO safety hold); `PerformanceEngine` (quarterly reviews); `ExecutiveDynamics` (workload drift, CISO escalation) |
| `Actions` | `ActionProposal`, `ActionSpec`, `ActionRegistry`, `ActionValidator`, handlers: `delegate_assignment`, `request_assessment`, `approve_proposal`, `reject_proposal`, `defer_proposal`, `reduce_budget`, `incident_response` |
| `Evaluation` | `DecisionEvaluator`: rules-based decision quality (evidence, stakeholders, financial, risk, authority, compliance), computed **before** the action takes effect; institutional trust effects |
| `Executives` | `StanceEngine` |
| `AI` | `RuleBasedInterpreter`, `ContextBuilder` (facts and memory retrieval), `DialogueComposer`, `ResponseValidator`, `MeetingService` (orchestration), `Llm/LlamaServer` (process manager and client) |
| `Persistence` | `StateSerializer` (JSON, checksum, migrations), `SaveRepository` (SQLite) |
| `Scenarios` | `HealthcareCtoScenario` |

## Determinism

* All randomness comes from `DeterministicRng`, whose state is part of `GameState`. Same seed and same
  decisions give an identical state hash, including across save, reload and continue (tested).
* Money is `decimal`. Engine text is formatted with a pinned culture (`TextCulture`) because message
  text is part of the saved state.
* The only non-deterministic input is the seed chosen at new-game time (wall clock), which is then stored.

## Finance

Every amount is a `LedgerEntry`. Monthly close derives the income statement from that month's ledger,
posts capital spend to the fixed-asset subledger, and **asserts** `Cash + Net PP&E = Debt + Equity` and
subledger = general ledger. Any mismatch throws. Savings are tracked as Proposed → Approved → Realized:

* a budget cut reduces a line's *discretionary* pool, so the same dollars cannot be cut twice;
* delegation budgets are *earmarked* from the discretionary pool, so they cannot also be cut;
* realization happens only at month close, once the reduction is in effect;
* one-time savings expire at fiscal-year rollover.

## Hidden information

The organization believes its 2022 test result (24 h recovery). The truth (60–84 h), backup integrity
(70–85% restorable) and whether the EHR vendor supports cloud DR are seeded per game. Assessments reveal
the truth with an accuracy that depends on the assigned executive's competence, workload and budget, so
a weak assessment can give false assurance. Executives only ever see reported values.

## Persistence

SQLite (`Microsoft.Data.Sqlite`), one database with named slots. Each slot keeps a current and a
previous generation. Writes are transactional (WAL, `synchronous=FULL`). On load: `PRAGMA quick_check`,
SHA-256 checksum, schema check (newer schemas are refused, older ones migrate stepwise through
`SaveMigrations`). A corrupted current generation falls back to the previous one with a warning. A
`decision_log` table mirrors decisions for future cross-save tooling.

## Godot client (`game/`)

Thin presentation layer. `GameSession` owns the engine, the save store and the optional inference
process. Screens are built in C# from a shared theme (`UI/Palette.cs`). The meeting view shows engine
results ("RECORDED: …") separately from dialogue, labels AI-generated lines, and requires explicit
confirmation for high-impact actions. `SmokeTest.cs` drives the real scenes headlessly in CI.

## Scaling to more industries

Industry-specific content lives in `Scenarios/` and in the effect keys, topics and stance rules. The next
architectural step (Phase 3) is to move scenario content (proposals, effects, stance weights, schedules)
into data files so that new industries do not require engine changes.
