# Development log

## 2026-11-21 — Iteration 1: foundation and first vertical slice

### Built
- **Simulation core** (`src/ExecutiveCommand.Core`): deterministic engine with a daily tick, monthly close and fiscal quarters; ledger-based finance with enforced balance-sheet reconciliation; savings Proposed → Approved → Realized with double-count prevention; projects with milestones, capital spend and depreciation; delegated assignments whose findings reflect hidden ground truth with competence-dependent accuracy; risk-driven incidents with a 45-day spacing rule; lab-interface cutover incident with a binding CMO clinical-safety hold; CFO/CEO approval above player authority; quarterly reviews with listed evidence; CISO escalation as institutional memory.
- **Action system**: allowlisted registry (7 actions), strict parameter validation, authority checks, confirmation for high-impact actions; interpreter JSON schema generated from the registry.
- **Independent evaluation**: decision quality scored from state at decision time; outcomes recorded separately when consequences land.
- **AI layer**: rule-based interpreter (always available); `MeetingService` orchestration; deterministic stance engine; dialogue composer fallback; response validator (character, invented figures, stance contradiction, false action claims, implausible text); grounding check for model-proposed numbers; managed `llama-server` process.
- **Persistence**: SQLite slots with two generations, checksums, integrity check, schema migrations, and corruption fallback.
- **Godot 4.6.3 client**: main menu, new game, dashboard, inbox, meeting room, decisions, projects, finance, risk register, executives, performance and decision record, settings (AI, display scale, fullscreen, autosave), save/load; in-engine smoke test.
- **CLI**: interactive console plus a scripted two-process end-to-end demo.

### Verified (actually run)
- `dotnet test`: **60/60 passing**, including 4 tests against a real `llama-server` process.
- CLI `demo1` then `demo2` in **separate processes**: the reloaded state hash matches the saved hash exactly; decisions, savings, project and review history all persist.
- Godot headless smoke test: **ALL PASSED** (new game, every view, meeting reply, manipulation has no effect, delegation, confirmed approval, a month advanced, save → load hash identical). Also rendered under Xvfb/Mesa; screenshots are in `docs/screenshots/`.
- Windows export: produced `build/windows/ExecutiveCommand.exe` (PE32+ x86-64) with the self-contained .NET runtime and native `e_sqlite3.dll`.

### Not verified / limitations
- **The Windows executable has not been run.** Wine crashes at startup in this container (`free(): invalid pointer`, even for `cmd /c echo`). Next step: run `ExecutiveCommand.exe -- --smoke-test` on a real Windows 10/11 machine.
- **No real language model has been tested.** Hugging Face was unreachable, so the LLM path was exercised with a random-weight model (plumbing and safety only). Next step: benchmark Qwen3-8B / 4B Q4 GGUF (license to be confirmed on the official model card) using the plan in AI_RUNTIME.md.
- The bundled `llama-server` is a Linux build. Windows needs the official llama.cpp Windows release binaries in `runtime/llama/`; the export does not copy them yet.
- Balance values are first-pass. Executive portraits are placeholders (initials). There are no audio, achievements or Steamworks yet.

### Bugs found by testing and fixed
- Subledger and general ledger diverged mid-month (capex was posted to assets daily) → capex is now posted at close.
- "great**test**" matched the delegation verb "test" → delegation verbs now use word boundaries.
- A compound cut ("$250k from travel and $150k from consulting") applied the amount to the wrong line → the line nearest the amount is used, with a note.
- The CIO called a deadline "workable" and then missed it → estimates now use the engine's own capacity model.
- The CFO supported the $180k identity expansion after a $40k alternative was found → alternatives now drive CFO, CEO and EA opposition.
- Grammar-constrained JSON truncated by the token limit → schema strings are bounded, and `finish_reason=length` counts as failure.
- Locale-dependent formatting would change state hashes between machines → engine text culture is pinned.
- Godot view keys did not match the navigation; a confirmation card was left visible; a duplicate interjection appeared after confirmation; cached views leaked at exit.

### Next iteration (proposed order)
1. Run the Windows build on real Windows; bundle the Windows llama.cpp runtime in the export.
2. Benchmark real models; tune prompts and validator thresholds with a fixed evaluation corpus.
3. Balance pass via Monte Carlo over seeds (CLI); playtest the 6-month slice.
4. Data-driven scenario content (prepares for additional industries); more actions (hiring, reallocation, board presentation).
5. Career layer: offers, promotion and termination paths beyond the slice.
