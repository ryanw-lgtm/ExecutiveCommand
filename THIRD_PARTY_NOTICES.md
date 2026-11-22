# Third-party notices (working list — verify before release)

| Component | Use | License |
|---|---|---|
| Godot Engine 4.6.3 | Game engine | MIT |
| .NET 8 runtime / BCL | Runtime | MIT |
| Microsoft.Data.Sqlite 8.0.11 | Saves | MIT |
| SQLitePCLRaw (bundle_e_sqlite3) | SQLite native binding | Apache-2.0 |
| SQLite | Database engine | Public domain |
| llama.cpp / ggml (`llama-server.exe`, built from source, commit 29b52d3) | Local AI engine, bundled in `runtime/llama/` | MIT |
| xUnit (tests only, not shipped) | Testing | Apache-2.0 |
| IBM Plex Sans / IBM Plex Mono | Interface typefaces (`game/assets/fonts`) | SIL Open Font License 1.1 |
| Phosphor Icons (core 2.1.1) | Interface icons (`game/scripts/UI/IconData.cs`) | MIT |
| Qwen3-4B-Instruct-2507 (Q4_K_M GGUF) | AI model, downloaded on first launch to the user data folder | Apache-2.0 (verified on the official model card and LICENSE) |

The full license texts must be included in the shipped build.
