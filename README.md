# Aira — Always-On Windows AI Assistant (v2.0) + Android Companion

A production-grade WPF desktop AI assistant written in C# / .NET 8 with strict
MVVM, streaming LLM integration (OpenAI or Anthropic Claude), function calling
that drives real OS actions, a self-echo-free voice loop (System.Speech +
NAudio + CoreAudio), **SQLite conversation history**, an in-app settings
dialog, and **user-approved destructive tools**.

> The application is named **Aira** throughout the codebase.

---

## 0. What's new in v2.0 — "VIP" (10 themes + red halo + packaging)

| Area | Improvement |
|------|-------------|
| **10 VIP themes** | Midnight Violet · Crimson Ember · Emerald Glass · Royal Gold · Neon Blade · Rose Noir · Frost Light (light) · Carbon Mono · Synthwave · Stealth Green. Each defines ~35 brushes: glass panels, gradient buttons, chat bubbles, aurora glows, status colors, legacy dialog palette. |
| **Live switching** | The palette moved out of XAML into `Infrastructure/ThemeEngine.cs`, which builds an app-level `ResourceDictionary` at runtime. Windows referencing brushes via `DynamicResource` repaint instantly — switching themes from Settings needs **no restart**. |
| **Theme picker** | Settings → **Theme** combo box: previews each theme live as you scroll, saves on Save, restores the previous theme on Cancel. Choic persisted in `Ui:Theme`. |
| **Red halo** | User request honored: the mic pulse ring (`HaloBrush`) is **RED in every theme** — `#E6FF4D4D` with a thicker (3.5px) expanding pulse while listening. |
| **Theme-aware chrome** | Aurora edge glows, brand wordmark gradient, logo chip, status pill, ghost buttons and scroll thumbs all follow the active theme. |
| **Frosted dialogs** | Settings + Sessions dialogs get the DWM acrylic backdrop (`WindowGlassHost`) and theme-correct glass. |
| **Windows package** | `dist/Windows/AiraAssistant.exe` — a single self-contained 158 MB binary (no .NET install needed) + `appsettings.json`. Desktop & Start-Menu shortcuts point at it. |
| **Android companion** | `dist/Android/Aira.apk` — a 9 MB installable APK (`.NET 8 / net8.0-android`): dark glass chat screen, OpenAI key prompt stored device-private, multi-turn memory (last 20). A chat companion; desktop tools & voice live in the Windows build. |

### Theme lineup

| # | Theme | Mood | Accent |
|---|-------|------|--------|
| 1 | Midnight Violet *(default)* | deep navy glass | blue → violet |
| 2 | Crimson Ember | warm graphite | red → ember |
| 3 | Emerald Glass | emerald night | mint → green |
| 4 | Royal Gold | gold on dark | amber → gold |
| 5 | Neon Blade | near-black electric | cyan → blue |
| 6 | Rose Noir | velvet dark | pink → rose |
| 7 | Frost Light | **light** glass | blue → violet |
| 8 | Carbon Mono | monochrome | silver → white |
| 9 | Synthwave | retro dusk | magenta → peach |
| 10 | Stealth Green | terminal night | lime → green |

The halo stays red in all ten.

---

## 1. What's new in v1.1

| Area | Improvement |
|------|-------------|
| **Build integrity** | All 56 duplicated " (2)" source files were merged into a single coherent generation and deleted; the project compiles again. |
| **Multi-tool flows** | Assistant turns carrying several tool calls are fully persisted; tool results are awaited and one continuation request is streamed (OpenAI requires *every* `tool_call_id` echoed back). Anthropic tool results are grouped into one user message per call bunch. |
| **Context integrity** | The AI service reuses the caller's `Conversation`; assistant/tool turns survive across user turns and restarts. |
| **DB persistence** | `Microsoft.Data.Sqlite` store at `%LOCALAPPDATA%\Aira\aira.db` (WAL). Sessions, messages and tool events persist; last session auto-restores; "Clear" wipes it. |
| **Voice loop** | STT is **suspended while Aira speaks** (unless barge-in is on), fixing the self-hearing feedback loop; barge-in cancels TTS on new speech. Dictation grammar loads once; the unsupported `AssumeCFGEnabled` setting was removed. TTS never calls `SelectVoice("")` (which aborted startup). |
| **Safety** | `shutdown_system`, `sleep_system`, `lock_workstation`, `type_text`, `clipboard_write` show a confirmation dialog first (`Tools:RequireConfirmation`). Runs as `asInvoker` — no UAC prompt on every start (admin is not required for any built-in tool). |
| **Settings UI** | Gear button in the top bar: provider, model, system prompt, STT/TTS cultures, TTS voice picker with live preview, barge-in and always-on-top. Saved back to `appsettings.json`. |
| **Tray** | System-tray icon with Show/Hide, voice toggle, Exit + double-click toggle. |
| **Logging** | Rolling JSONL + text logs moved to `%LOCALAPPDATA%\Aira\Logs`, auto-pruned after 14 days. |
| **Threading** | All VM mutations marshaled to the UI thread (`Ui.Post/InvokeAsync`) — no more cross-thread binding exceptions from speech/HTTP events. |
| **Glass UI (v2)** | VIP frosted-glass redesign: system acrylic/mica backdrop, borderless window with custom title bar, floating glass cards, breathing status dot, halo-pulsing mic with 360° spin, pop-in chat bubbles, gradient buttons with 3D hover/press motion, animated focus glow, slim glass scrollbars, aurora edge glows. |
| **Sessions** | Sessions dialog: switch, rename, or delete stored conversations; the current session restores on startup. |
| **Secrets** | API keys can be entered in the Settings dialog and are stored DPAPI-encrypted in `%LOCALAPPDATA%\Aira\secrets.dat` — appsettings.json stays clean. |
| **First-run UX** | A missing API key no longer aborts startup — Aira runs, chats show guidance, and Settings collects the key. |

---

## 2. Features

- **Modern MVVM WPF UI** — 10 VIP themes with live switching (Settings → Theme),
  glass sidebar with provider/model and a live tool log, chat pane with
  streaming bubbles and auto-scroll, red mic halo.
- **Always-on voice session** — mic button (or Ctrl background tray menu) starts
  an always-listening session; transcripts → LLM → streamed reply → local TTS.
- **Streaming LLM** — OpenAI Chat Completions and Anthropic Messages, both with
  end-to-end streamed tool calling.
- **Real OS control**: open URLs, launch whitelisted apps, set volume / mute,
  lock, sleep, shutdown (conditional + approved), all through one dispatcher.
- **Persistence** — conversations restart exactly where they left off.
- **Sessions manager** — open, rename or delete stored sessions; start new ones.
- **Structured logging** — rolling JSONL + text under `%LOCALAPPDATA%\Aira\Logs`.

---

## 3. Project Layout

```
AiraAssistant/
├── AiraAssistant.sln
├── .gitignore / LICENSE
├── dist/
│   ├── Windows/AiraAssistant.exe     # published single-file package
│   └── Android/Aira.apk              # signed companion APK
└── src/
    ├── AiraAssistant/
    │   ├── AiraAssistant.csproj
    │   ├── App.xaml / App.xaml.cs        # DI composition root (+ theme apply)
    │   ├── app.manifest                   # asInvoker + PerMonitorV2 DPI
    │   ├── appsettings.json
    │   ├── Infrastructure/
    │   │   ├── AppBootstrapper.cs         # post-DI wiring (thread-marshaled)
    │   │   ├── AppConfigLoader.cs         # typed config + SaveToFile
    │   │   ├── ThemeEngine.cs             # 10 VIP themes → runtime brush dict
    │   │   ├── WindowGlassHost.cs         # DWM acrylic for windows/dialogs
    │   │   ├── AdminService.cs            # elevation helper
    │   │   ├── TrayIconService.cs         # system tray presence
    │   │   ├── UiDispatcher.cs            # UI-thread marshaling helper
    │   │   └── Logging/FileLogger.cs      # rolling JSONL + text, auto-prune
    │   ├── Models/                        # POCOs, enums, ToolCallInfo
    │   ├── Services/
    │   │   ├── Ai/                        # OpenAI/Anthropic streaming, Router, ToolCatalog
    │   │   ├── Audio/                     # NAudio microphone (config-driven)
    │   │   ├── Speech/                    # System.Speech STT (+Suspend) + TTS
    │   │   ├── History/                   # SQLite persistence (WAL)
    │   │   └── SystemControl/             # whitelisted OS actions
    │   └── UI/
    │       ├── Themes/ Modern.xaml + ChatBubble.xaml   # styles (DynamicResource brushes)
    │       ├── Converters/
    │       ├── ViewModels/                # Main, Settings (+base/commands)
    │       └── Views/                     # MainWindow, SettingsDialog, SessionsDialog
    └── AiraMobile/                        # Android companion (net8.0-android)
        ├── AiraMobile.csproj
        ├── AndroidManifest.xml
        └── MainActivity.cs                # programmatic dark-glass chat UI
```

---

## 4. Prerequisites

| Requirement | Minimum |
|-------------|---------|
| Windows | 10 1809+ or 11 |
| .NET SDK | 8.0 (https://dot.net) |
| Microphone + speakers | any (for voice mode) |
| Internet | required (LLM API) |

> Speech recognition is a Windows feature: install regional language packs
> (e.g. English or Arabic) for the `SttCulture` you choose.

---

## 5. Setup

```powershell
git clone <your-repo> AiraAssistant
cd AiraAssistant
dotnet restore
dotnet build AiraAssistant.sln -c Release
dotnet run --project src/AiraAssistant
```

1. First launch opens with an empty chat. Click the **⚙ gear** in the top bar.
2. Choose provider (OpenAI / Anthropic) and paste the **API key** into the
   settings dialog — it is stored DPAPI-encrypted in
   `%LOCALAPPDATA%\Aira\secrets.dat`. (Alternative: put
   `Ai.OpenAiApiKey` / `Ai.AnthropicApiKey` in `appsettings.json`; an explicit
   JSON key wins over the DPAPI store.)
3. Pick model (`gpt-4o-mini`, `claude-3-5-sonnet-20241022`, …), STT/TTS
   cultures, voice; click Save; restart when prompted.
4. Voice mode: press the 🎙 button in the composer.

### Package for distribution

**Windows** (single self-contained exe — no .NET install required):

```powershell
dotnet publish src/AiraAssistant -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# then copy src/AiraAssistant/bin/Release/net8.0-windows/win-x64/publish/* → dist/Windows
```

**Android** (installable APK):

```powershell
dotnet build src/AiraMobile/AiraMobile.csproj -c Debug `
    -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk"
# signed APK: src/AiraMobile/bin/Debug/net8.0-android/com.aira.mobile-Signed.apk → dist/Android/Aira.apk
```

The prebuilt outputs ship in `dist/` — see `dist/Android/اقرأني - التثبيت.txt`
for Arabic sideload instructions.

---

## 6. Using Aira

- **Chat** — type and press Enter.
- **Voice** — click 🎙; status pill: green = listening, blue = thinking,
  orange = speaking. Speak while she talks to interrupt (barge-in) — depends on
  `Speech:BargeInEnabled` and Windows echo handling.
- **Tools** — say *"open youtube"*, *"set volume 25"*, *"launch calculator"*,
  *"lock my pc"*, *"shutdown in 60 seconds"*. Destructive requests ask first
  (10s-safe dialog with No as default).
- **Clear** — wipes the current session (DB included) and starts a fresh one.

---

## 7. How function calling works

```
user ─▶ MainViewModel ─(conversation)─▶ OpenAI / Anthropic (SSE)
              │ streamed text chunks
              │ tool_calls (chunked by index; all calls persisted)
              ▼
        FunctionRouter ──▶ confirmation? ──▶ SystemControlService
              │ tool result JSON appended to the conversation
              ▼
        one continuation request (all tool_call_ids echoed)
              ▼
        assistant final text → chat + TTS (+ SQLite row)
```

Supports **multi-step** flows ("open YouTube then search for X") and even
multiple calls in a single model turn.

---

## 8. Extending

- **Swap the speech engine** — implement `ISpeechToTextService` (set
  `IsPushAudio = true` if it consumes the NAudio stream, e.g. Whisper/Vosk);
  register in `App.xaml.cs`. The voice loop and mic wiring adapt automatically.
- **Swap the AI provider** — implement `IAiService` and add it to
  `AiServiceFactory.Create`.
- **Add a tool** — append a `FunctionDefinition` in `ToolCatalog` and a `case`
  in `FunctionRouter` (or implement `ISystemControlService` action). LLM
  discovers it next turn.

---

## 9. Security notes

- Runs `asInvoker`. Only user-approved destructive actions (dialog with No as
  default) execute: shutdown always waits ≥ requested delay with a confirmation.
- Whisper of trust: every tool call is logged (name + args + result) to the
  rolling log files and the sidebar "Recent actions" list.
- URLs must be absolute or hostname-like; nullable schemes outside
  http/https fall back to system behavior — do not add autostart scripts with
  admin rights without reviewing tools first.
- API keys are stored DPAPI-encrypted in `%LOCALAPPDATA%\Aira\secrets.dat`
  (in-app Settings). Keys may alternatively be placed in `appsettings.json`
  (which wins) — but never commit an installer image containing keys.

---

## 10. Troubleshooting

| Symptom | Fix |
|---------|-----|
| Mic button unresponsive | Windows Settings → Privacy → Microphone → allow desktop apps. |
| TTS silent | Check default output device; verify a TTS voice exists for `TtsCulture`. |
| STT never recognizes | Install the language pack for `SttCulture` (e.g. English (US)). |
| "No API key is configured" bubble | Click ⚙, paste the key, Save → restart. Or set `Ai.OpenAiApiKey` in appsettings.json. |
| Tool does nothing | Check the sidebar tool log + Logs for router messages; confirm `Tools:RequireConfirmation` didn't silently deny. |
| History lost | Only Clear deletes; check `%LOCALAPPDATA%\Aira\aira.db` exists and is writable. |
| Log files | `%LOCALAPPDATA%\Aira\Logs\aira-*.jsonl` (auto-pruned after 14 days). |

---

## 11. Roadmap (next)

- Whisper.net local STT engine (interface + mic pipeline already push-ready).
- Markdown rendering + code-copy buttons in bubbles.
- "Test key" button in Settings (one-shot provider ping).
- Wake-word "Hey Aira" + configurable RMS gate file saved per session.

## 12. License

MIT — see `LICENSE` at the repo root.
