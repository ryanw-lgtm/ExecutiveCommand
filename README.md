# Executive Command — Windows build

**Download:** [ExecutiveCommand-windows.zip](https://github.com/ryanw-lgtm/ExecutiveCommand/raw/windows-build/ExecutiveCommand-windows.zip)

1. Download and extract the zip (right-click → Extract All).
2. Open the `windows` folder and run `ExecutiveCommand.exe`. Keep the `.exe`, the `.pck` file and the `data_ExecutiveCommand_windows_x86_64` folder together.
3. Windows SmartScreen may warn because the executable is not code-signed: choose **More info → Run anyway**.

Notes:
- Built from `main` at commit fa3e553 with Godot 4.6.3 (.NET), x86-64, self-contained .NET 8 runtime.
- **Not yet tested on real Windows.** To run the automated check, open a command prompt in the `windows` folder and run `ExecutiveCommand.exe -- --smoke-test`, then `echo %ERRORLEVEL%`: 0 means pass, 1 means fail.
- The local AI runtime is not included yet; executives use scripted dialogue.
- The source code is on the [`main` branch](https://github.com/ryanw-lgtm/ExecutiveCommand). This branch contains only the build.
