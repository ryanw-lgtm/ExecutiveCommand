# Steam / commercial preparation notes (Phase 5 — not started)

Verify every item against current Steamworks documentation before submission. Policies change.

## Generative AI disclosure (draft)

Valve's revised disclosure (January 2026, per press coverage: [PC Gamer](https://www.pcgamer.com/software/ai/steam-updates-ai-disclosure-form-to-specify-that-its-focused-on-ai-generated-content-that-is-consumed-by-players-not-efficiency-tools-used-behind-the-scenes/), [VGC](https://www.videogameschronicle.com/news/valve-has-significantly-rewritten-steams-rules-for-how-developers-much-disclose-ai-use), [GamingOnLinux](https://gamingonlinux.com/2026/01/valve-tweak-steam-ai-disclosure-form-for-developers-to-clarify-its-for-content-consumed-by-players)) distinguishes pre-generated from **live-generated** content. Live-generated content requires a description of guardrails, and players can report illegal content through the overlay.

Draft answer:

> When the player enables the optional local AI, executive dialogue is generated at runtime by a language model running entirely on the player's PC. The model cannot change game state. All outcomes are decided by deterministic game rules. Guardrails: fixed professional character prompts; the model voices pre-computed positions only; every reply is validated (in character, plausible language, no invented figures, no claims of actions the game did not take) and replaced by scripted dialogue if it fails; output length is capped; no images, audio or adult content are generated; the player can disable AI entirely.

## Checklist

- [ ] Steamworks SDK integration (achievements for meaningful milestones; cloud saves of `user://saves.db`, ideally per slot)
- [ ] Windows export signed; installer/depot layout: `ExecutiveCommand.exe`, `data_*`, `runtime/llama/`, optional model DLC or download
- [ ] Offline operation verified (no network calls apart from Steam)
- [ ] Privacy policy (no telemetry today; crash reporting must be opt-in)
- [ ] `THIRD_PARTY_NOTICES.md` complete (Godot MIT, .NET MIT, SQLite public domain, SQLitePCLRaw Apache-2.0, llama.cpp MIT, model license)
- [ ] Accessibility: UI scale (done), keyboard navigation pass, colorblind-safe status colors (positions are already labeled in text, not color alone), text size
- [ ] Localization: move engine message text to keyed templates (currently pinned en-US)
- [ ] Controller support (future)
- [ ] Pricing/fees: verify current Steam terms. No ongoing inference costs (local AI only).
