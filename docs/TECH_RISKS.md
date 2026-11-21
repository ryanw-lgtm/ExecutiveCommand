# Technology stack evaluation and risks

## Stack decision

| Proposed | Decision | Notes |
|---|---|---|
| Godot 4 stable, C# | **Godot 4.6.3 .NET**, C# throughout | 4.7.x exists. 4.6.3 is a mature patch release on .NET 8. Upgrade after the vertical slice. |
| llama.cpp, GGUF | **Bundled `llama-server` child process** (not in-process bindings) | A crash or out-of-memory error in inference cannot take the game down. Version upgrades are a file swap. Grammar-constrained JSON is available through the OpenAI-compatible API. |
| SQLite | **SQLite** via Microsoft.Data.Sqlite 8.0.11 | Transactions, integrity checks, single-file saves. |
| Tests | xUnit, plus an in-engine Godot smoke test, plus the CLI two-process demo | |

Not a web app: the client is native Godot, and the core is a plain .NET library.

## Significant risks

| Risk | Impact | Mitigation / status |
|---|---|---|
| **Model quality vs. hardware.** 7–8B models at Q4 need about 5–6 GB RAM; CPU replies take seconds. | Slow or poor dialogue on low-end PCs | AI is optional. The deterministic composer and rule interpreter make the game fully playable without it. A settings page covers threads and GPU layers. **Not yet benchmarked with a real model** (weights could not be downloaded in this environment). |
| **LLM sycophancy and prompt injection** | Breaks the core design | Stances, evaluation and authority are deterministic. Conduct detection is rule-based. Output validators and grounding are covered by tests, including a hostile fake model. |
| **Truncated or malformed structured output** | Crashes or bad actions | Found in testing: JSON truncated by the token limit. Fixed by bounding schema strings, treating `finish_reason=length` as failure, and falling back to rules. |
| **llama.cpp API churn** (the pinned commit is very recent) | Breakage on upgrade | Pin a tested release per game version. The integration tests run against the real binary. |
| **Chat-template handling in llama-server** | Grammar init fails for nonstandard templates | Observed with a synthetic template. Standard ChatML works. Each candidate model must pass the integration tests. |
| **Godot C# export of native NuGet deps** (`e_sqlite3`) | Saves fail on Windows | Covered by the Windows export test (see DEVLOG). Must be verified on real Windows. |
| **Orphaned inference process** if the game crashes | Leftover CPU/RAM use | The process is killed on normal exit. TODO: Windows Job Object (kill-on-close) and a stale-PID cleanup at launch. |
| **Content balance.** Thresholds and weights are first-pass. | Exploits, frustration | Determinism makes balance testing scriptable (Monte Carlo over seeds). Playtesting is needed. |
| **Scenario content is code** | Slow to add industries | Plan: data-driven scenarios in Phase 3. |
| **Licensing** (models, runtime, fonts) | Store rejection or legal exposure | See AI_RUNTIME.md. Verify against primary sources before shipping. |
| **Steam generative-AI disclosure** for live-generated content | Store review | Disclosure and guardrail description drafted in STEAM_PREP.md. |
