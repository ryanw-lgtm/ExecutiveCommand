# Executive Command — Windows build

**Download:** [ExecutiveCommand-windows.zip](https://github.com/ryanw-lgtm/ExecutiveCommand/raw/windows-build/ExecutiveCommand-windows.zip) (about 83 MB)

1. Download and extract the zip (right-click → Extract All).
2. Open the `windows` folder and run `ExecutiveCommand.exe`. Keep everything in that folder together.
3. Windows SmartScreen may warn because the executable is not code-signed: choose **More info → Run anyway**.
4. **First launch:** click **Download AI model** on the main menu (one-time, 2.5 GB, about 4 GB of RAM needed). The game verifies the file, starts the AI automatically and from then on runs fully offline. You can play while it downloads; executives use short scripted lines until the AI is ready.

Notes:
- Built from `main` at commit 109e495 with Godot 4.6.3 (.NET), x86-64. AI engine: llama.cpp `llama-server.exe` (CPU, AVX2) in `runtime/llama/`. Model: Qwen3-4B-Instruct-2507 (Apache-2.0). Licenses are in `licenses/`.
- Replies are written live on your PC's CPU; expect a few seconds per executive (faster on newer CPUs).
- **Not yet tested on real Windows.** To run the automated check: open a command prompt in the `windows` folder, run `ExecutiveCommand.exe -- --smoke-test`, then `echo %ERRORLEVEL%` (0 = pass).
- Source code: [`main` branch](https://github.com/ryanw-lgtm/ExecutiveCommand).
