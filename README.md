# Executive Command

An executive leadership and business-management simulation for PC (Windows first; Linux/macOS later).
Players lead a fictional organization through budgets, projects, delegation, vendors, crises and
board politics, and they talk to their executives in free text.

**Core principle:** Executive Command is a deterministic business simulation with AI-powered characters,
not a chatbot disguised as a game. The simulation engine is the only authority over game state. Language
models interpret what the player says and voice the characters. They cannot move money, approve
anything, or award success.

Status: **Phase 1–2 prototype / first vertical slice** (healthcare CTO scenario). See `docs/DEVLOG.md`.

## Repository layout

| Path | What it is |
|---|---|
| `src/ExecutiveCommand.Core` | Pure C# simulation core (no Godot dependency): model, engine, finance, actions, evaluation, AI orchestration, persistence |
| `tests/ExecutiveCommand.Core.Tests` | xUnit tests: determinism, reconciliation, authority, anti-manipulation, persistence, delegation, real llama-server integration |
| `src/ExecutiveCommand.Cli` | Headless developer console and scripted two-process end-to-end demo |
| `game/` | Godot 4.6.3 (.NET) client: theme, screens, meeting room, in-engine smoke test |
| `tools/make_test_model.py` | Builds a tiny random-weight GGUF for inference **plumbing** tests (not a usable model) |
| `docs/` | Architecture, technical risks, AI runtime and licensing, Steam preparation, development log, screenshots |

## Build and test

Requirements: .NET 8 SDK and Godot 4.6.3 **.NET edition** (only needed for the game client).

```bash
dotnet test tests/ExecutiveCommand.Core.Tests
```

```bash
dotnet run --project src/ExecutiveCommand.Cli -- play --db saves.db
```

The end-to-end demo runs as two separate processes against one save database:

```bash
dotnet run --project src/ExecutiveCommand.Cli -- demo1 --db demo.db
```

```bash
dotnet run --project src/ExecutiveCommand.Cli -- demo2 --db demo.db
```

To run the game, open `game/project.godot` in the Godot 4.6.3 .NET editor and press Play. The automated in-engine check is:

```bash
godot --headless --path game -- --smoke-test
```

## Local AI (optional)

The game is fully playable without a model. Executives then speak through a deterministic dialogue
composer. To enable local AI, place a `llama-server` build in `runtime/llama/` and a GGUF instruction
model in `models/`, or choose a model in Settings. Setup, model selection and licensing are covered in
`docs/AI_RUNTIME.md`.

All organizations, people, vendors and figures in the game are fictional.
