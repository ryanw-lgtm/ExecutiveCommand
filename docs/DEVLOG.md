# Development log

## 2026-11-22 — Iteration 4: visual redesign (Rune / UI-UX Pro Max principles)
- **Design tokens** (`game/scripts/UI/Palette.cs`): semantic surfaces (base/sunken/surface/elevated), no pure black or white, executive-gold accent (no default blue/purple), green/red reserved for data and always paired with ▲▼● symbols.
- **Typography**: IBM Plex Sans for UI, IBM Plex Mono for every number; 12/14/16/18/24/32 scale, 16px body, semibold headings.
- **Icons**: Phosphor (MIT), rasterized at exact size for crispness; icon navigation with a gold active marker.
- **Dashboard**: non-uniform hierarchy — attention list + financial pulse, KPI strip with change vs last month and sparklines (from new month-end `History` snapshots), ideas + timeline.
- **Meeting room**: chat thread with avatars, position pills, AI label, grey typing bubble, distinct RECORDED card; confirmed decisions get a short acknowledgement instead of repeated reasoning.
- **Interaction**: Ctrl+K command palette; 2px focus rings; press feedback (scale 0.97, 120ms); fades ≤ 200ms ease-out, exits faster; no animation for keyboard-initiated navigation; "Reduce motion" setting.
- **Empty states** everywhere say what's missing and how to fill it. Main menu uses an asymmetric layout with a briefing panel.
- Verified: 64/64 tests; smoke test ALL PASSED incl. command palette; screenshots reviewed.

## 2026-11-21 — Iteration 3: executive typing times
- Replies in the meeting room now arrive one at a time with a "<name> is typing…" indicator. Each executive has their own pace (`Core/AI/TypingPace.cs`): decisive people answer sooner, cautious ones (e.g. the CFO, CMO) pause before replying, overloaded people are slower, and longer answers take longer.
- **Skip** button reveals everything at once; Settings offers instant / quick / natural pacing (default natural). Purely presentational — no effect on game state.
- Verified: 63/63 tests; smoke test checks the typing indicator appears and the reply is delivered.

## 2026-11-21 — Iteration 2: usability pass

### Changed
- **Welcome guide** on first launch and a **Help** button (F1): the game loop in four steps.
- **Dashboard "Needs your attention"**: incidents, late work, new findings, pending decisions, contract deadlines, savings progress, the unaddressed DR gap. Each item has a button that goes to the right screen or opens the right meeting.
- **"Things you could say"**: context-aware example sentences on the dashboard and as clickable chips in the meeting room. A unit test guarantees every suggestion is understood by the interpreter and validates.
- **Meeting room**: one-click attendee presets (Technology team, Executive committee, CFO one-on-one, Clinical leaders, Vendor), larger text, clearer instructions.
- **Decisions**: every executive's position is shown in plain view with a one-line reason; the engine now tags reasons as for or against, so the headline never contradicts the position. Plain-language authority text; "Discuss first" button.
- **Inbox**: "Discuss with …" and "Respond to incident" buttons, "Mark all read", and the view sized to the window.
- **Top bar**: Next day / week / month with tooltips and shortcuts (F5/F6/F7), Ctrl+1–9 to switch screens, hover help on navigation and dashboard tiles; the company line truncates so the layout never overflows.

### Verified
- 62/62 tests passing; Godot smoke test ALL PASSED (now also covers help, suggestions, meeting-from-suggestion, inbox jump); screenshots re-rendered and reviewed at 1600×900.

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
