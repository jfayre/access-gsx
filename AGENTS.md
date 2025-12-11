# Repository Guidelines

## Project Structure & Module Organization
- WPF app targeting `net8.0-windows` (`GSXRemote.csproj`).
- UI: `App.xaml`, `MainWindow.xaml` (layout) and `MainWindow.xaml.cs` (SimConnect, GSX menu logic, Tolk speech).
- Native/managed deps in repo root: `SimConnect*.dll`, `Tolk.dll`, `TolkDotNet.dll`, `nvdaControllerClient64.dll`.
- Config/assets: `SimConnect.cfg` (SimConnect client settings). Build artifacts should stay in `bin/` and `obj/` (ignored).

## Build, Test, and Development Commands
- Build: `dotnet build` (Windows, .NET SDK 8+, resolves WPF). Ensure `SimConnect.dll` and `Tolk.dll` sit beside the EXE or in `lib/` as referenced.
- Run (from project root): `dotnet run` (launches WPF app; requires Microsoft Flight Simulator running to fully exercise SimConnect).
- No automated tests are present; add under a `Tests/` project if needed.

## Coding Style & Naming Conventions
- C# 8+ with nullable enabled; follow .NET conventions: PascalCase for types/methods/properties, camelCase for locals/fields (private fields prefixed `_`).
- WPF: keep UI layout in XAML, bind accessible names, minimize code-behind UI logic except for event wiring and SimConnect/Tolk hooks.
- Indentation: 4 spaces; keep lines concise and comments purposeful.

## Testing Guidelines
- None included yet. If adding tests, prefer `xUnit` with method names describing behavior (e.g., `Should_SetRemoteControl_WhenSimulatorRestarts`). Place under a separate test project and wire into `dotnet test`.

## Commit & Pull Request Guidelines
- Write clear, imperative commit messages (e.g., “Add Tolk speech toggle to menu”).
- PRs should describe behavior changes, list manual test steps (build/run with MSFS), and note accessibility impacts (e.g., screen reader output).
- Include screenshots only when UI changes are visual; otherwise summarize new keyboard interactions or speech output.

## Accessibility & Speech Notes
- Screen reader output uses Tolk; guard `Tolk.Load()`/`Tolk.Unload()` and fall back gracefully if missing.
- Menu speech is optional (checkbox). Keep new UI controls focusable with sensible `TabIndex` and `AutomationProperties.Name`.
