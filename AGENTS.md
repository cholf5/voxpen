# AGENTS.md — VoxPen (声写)

Offline Chinese voice-to-text input helper. C# / .NET 8 / Avalonia rewrite of
[HaujetZhao/CapsWriter-Offline](https://github.com/HaujetZhao/CapsWriter-Offline).
Currently Windows 10/11 x64 only; cross-platform abstractions live in `VoxPen.Core`.

Read `README.md` before making user-facing or config changes — it doubles as
end-user docs and pins the config schema, `hot-rule.txt`, `hot.txt`, and
CLI-smoke-test contracts.

## Layout

- `src/VoxPen.Core/` — target `net8.0`. Abstract interfaces (`IGlobalHotkey`,
  `IAudioCapture`, `ITextOutput`, `IAsrEngine`, `IForegroundApp`,
  `IAudioDecoder`, `INotificationService`), pipeline state machine, post-
  process, hot-rule / hot.txt phoneme RAG, transcription, config, storage,
  diary. No Windows API here.
- `src/VoxPen.Platform.Windows/` — target `net8.0-windows10.0.19041.0`.
  Concrete impls: SharpHook, PortAudioSharp2, sherpa-onnx Paraformer,
  SendInput, MediaFoundation decode, UWP Toast.
- `src/VoxPen.App/` — Avalonia 12 UI, tray, `Services/AppHost.cs`
  composition root. Uses `CommunityToolkit.Mvvm`.
- `src/VoxPen.Cli/` — headless smoke-test runner and `transcribe` batch mode.
- `tests/VoxPen.Core.Tests/` — xUnit; `InternalsVisibleTo` is granted to it.
- `docs/plans/` — dated markdown design / review notes; add one when
  scoping non-trivial UI or config work.
- `hot-rule.txt` — repo-root fixture; `VoxPen.App.csproj` copies it to the
  output directory via `<None … CopyToOutputDirectory="PreserveNewest">`.
- `models/`, `recordings/`, `config.json`, `publish/`, `logs/`, `artifacts/`
  are all `.gitignore`d. Do not check them in.

## Commands

Solution file is `VoxPen.slnx` (SDK-style; use with modern `dotnet` CLI).

```bash
dotnet build src/VoxPen.App                 # main build
dotnet test  tests/VoxPen.Core.Tests        # xUnit, ~120+ cases, should stay green
dotnet run   --project src/VoxPen.Cli -- <cmd>   # smoke tests, see below
```

CLI smoke commands (no ASR model required for the first three):
`test-postprocess`, `test-hotword`, `test-diary`, `test-merger`,
`--file <wav>`, `transcribe <audio>...`, `run` (headless CapsLock listener).

Single-file publish (Windows only):

```bash
dotnet publish src/VoxPen.App/VoxPen.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o publish/win-x64
```

Headless env vars useful in scripts / CI:
- `CAPSWRITER_LOG_FILE=<path>` — mirror App logs to file.
- `CAPSWRITER_AUTO_EXIT_SECS=<n>` — auto-shutdown after n seconds.

## Architecture rules

- **`VoxPen.Core` must stay platform-neutral.** Anything touching Win32,
  SharpHook, sherpa-onnx, NAudio, Toast, PortAudio, or MediaFoundation goes
  in `VoxPen.Platform.Windows` behind a `Core/Abstractions/I*.cs` interface.
  When adding a new capability (e.g. new engine), add the interface in
  `Core/Abstractions/` first.
- `AppHost` is the composition root; it wires interfaces to Windows impls
  and owns the pipeline. New services should be registered there, not in
  `App.axaml.cs`.
- **Config back-compat matters.** `config.json` fields come from the
  original Python project. Never rename or drop existing keys; add new keys
  with defaults so `AppHost.LoadOrCreateConfig` fills them for old configs.
- **Hot-reload contract**: edits to `config.json`, `hot-rule.txt`, `hot.txt`
  auto-apply within ~3 s. Keep changes idempotent. Shortcut keys, model
  path, and diary root still require restart — call that out in UI.
- `hot-rule.txt` regex/literal + `\1..\n` backref format is fixed by the
  upstream Python project. Do not "improve" the syntax.

## Coding conventions

- `net8.0`, `Nullable enable`, `ImplicitUsings enable` project-wide — keep
  new files null-safe and rely on implicit usings.
- MVVM in App layer via `CommunityToolkit.Mvvm` source generators
  (`[ObservableProperty]`, `[RelayCommand]`); match the existing view-model
  style rather than hand-rolling `INotifyPropertyChanged`.
- Comments/log messages in this repo are frequently Chinese — match the
  surrounding language rather than forcing English.
- Tests are xUnit; put new tests under the matching `tests/…/<Area>/`
  folder so `InternalsVisibleTo` gives them access.

## Commit conventions

- **Never commit without explicit user permission.** After editing files,
  stop at the working-tree stage and wait for the user to say "提交" /
  "commit" / "push". Do not run `git add` + `git commit` on your own
  initiative, even for "obviously safe" changes.
- **Commit directly on `main`.** This is a solo project — no feature
  branches, no PRs by default. Once the user authorises a commit, stage,
  commit, and (if asked) push straight to `main`; do not
  `git checkout -b`.
- **Every commit MUST use Conventional Commits.** The release workflow
  (`.github/workflows/release.yml`) groups Release notes by prefix; a
  commit without a recognised prefix falls into a catch-all "其他"
  bucket and is treated as a mistake.
- Format: `<type>(<optional scope>)!?: <subject>`. Recognised types:
  `feat` → 🚀 新功能, `fix` → 🐛 修复, `refactor` / `perf` → 🔧 优化,
  `docs` / `chore` / `build` / `ci` / `style` / `test` → 📝 杂项.
  Breaking change marker `!` is allowed (`feat!: ...`).
- Subject follows the surrounding-language rule — Chinese or English,
  whichever matches recent history in the area.
- Rebase / squash-merge is fine; only the final commit landing on
  `main` needs the prefix.

## Gotchas

- **Never junction (`mklink /J`) `publish/models` → `models/`.** PowerShell
  `Remove-Item -Recurse` follows junctions and will nuke the real
  `models/` tree; `-Exclude` does not stop the follow. Copy the model
  directory instead, or `cmd /c rd <junction>` before recursive deletes.
- Missing `models/paraformer/` is fail-fast at startup with a clear log
  message — don't paper over it.
- Sending input to an elevated foreground window requires VoxPen itself to
  run elevated (SendInput limitation).
- Single-file `.exe` is ~100 MB after P7 — extra size comes from NAudio,
  Toolkit.Uwp.Notifications, ToolGood.Words.Pinyin.
- `MainWindow` close button minimises to tray; real exit is via tray menu
  or the window's Exit button. Keep that behaviour.
