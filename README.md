<p align="center">
  <img src="assets/banner.jpg" alt="TypingMimic Banner" width="100%" />
</p>

# TypingMimic

[![Release](https://img.shields.io/badge/Release-v1.1.0-blue.svg)](https://github.com/junaidakbar076/TypingMimic/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-brightgreen.svg)](https://github.com/junaidakbar076/TypingMimic)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-purple.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-orange.svg)](LICENSE)

A local Windows desktop app built with C#, .NET 8, and WPF. Import a text-based PDF or paste text, review the editable preview, then send paced Unicode keyboard input to another application. No server, accounts, database, telemetry, or cloud services are used by the application.

## 📥 Downloads (v1.1.0)

| Package | Size | Description | Download |
| :--- | :--- | :--- | :--- |
| **Offline Setup** *(Recommended)* | ~48.8 MB | Includes private .NET runtime; no internet or extra downloads needed. | [**Download Offline Setup**](https://github.com/junaidakbar076/TypingMimic/releases/download/v1.1.0/TypingMimic-1.1.0-Offline-Setup.exe) |
| **Online Setup** | ~3.0 MB | Minimal installer; uses or downloads system .NET 8 Desktop Runtime. | [**Download Online Setup**](https://github.com/junaidakbar076/TypingMimic/releases/download/v1.1.0/TypingMimic-1.1.0-Online-Setup.exe) |
| **Portable Executable** | ~159.7 MB | Self-contained single-file `.exe`; run directly without installation. | [**Download TypingMimic.exe**](https://github.com/junaidakbar076/TypingMimic/releases/download/v1.1.0/TypingMimic.exe) |

> [!NOTE]
> ### 🛡️ Windows SmartScreen / "Windows protected your PC"
> Because this open-source executable is freshly compiled and not signed with an expensive commercial EV certificate, Windows Defender SmartScreen may display: *"Windows protected your PC - Microsoft Defender SmartScreen prevented an unrecognized app from starting."*
>
> **To install or run anyway:**
> 1. Click **"More info"** on the popup.
> 2. Click the **"Run anyway"** button.
>
> *(Alternatively: Right-click the downloaded `.exe` $\rightarrow$ **Properties** $\rightarrow$ check **Unblock** at the bottom $\rightarrow$ click **OK**).*


## Build and run

Requires Windows and the .NET 8 SDK or a newer SDK able to target .NET 8. Framework-dependent execution requires the .NET 8 Windows Desktop Runtime.

```powershell
dotnet restore TypingMimic.sln
dotnet build TypingMimic.sln -c Release --no-restore
dotnet test TypingMimic.sln -c Release --no-build
dotnet run --project src/TypingMimic.App/TypingMimic.App.csproj -c Release
```

Open `TypingMimic.sln` in Visual Studio with the .NET desktop development workload, or use the commands above.

## Publish a self-contained Windows executable

For **Online and Offline setup installers**, see [installer/README.md](installer/README.md). Build both using `./installer/Build-Installers.ps1`; share the setup files from `publish/installers`.

```powershell
dotnet publish src/TypingMimic.App/TypingMimic.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
```

Run `publish/win-x64/TypingMimic.exe`. A self-contained build includes .NET. Native components may extract at startup. Do not enable trimming for this WPF application.

## Using the app

1. Choose **Open PDF**, drop one PDF onto the window, or use **Paste** (inserts at the preview selection). You can also type or paste directly into the preview. Importing a PDF replaces the preview only after successful nonempty extraction.
2. Review and edit the extracted text. The filename, page count, and extracted character count describe the most recent PDF; the estimate describes the current editor content.
3. Configure speed, variation, punctuation pauses, and optional corrected errors. All settings are validated; starting is disabled while any setting is invalid. Settings and text are frozen during a session. Text over 10,000 UTF-16 code units triggers confirmation.
4. Click **Start typing**, then focus the destination text field during the countdown (five seconds by default). The foreground window is captured at the countdown boundary before typing. Focusing this app or no window prevents input.
5. Use **Ctrl+Alt+F11** to pause/resume globally, including during countdown. While paused, focus the original target before resuming. Use **Ctrl+Alt+F12** to cancel immediately. The Stop button also cancels. Use the global shortcut while working in the destination: bringing this app forward while typing normally triggers focus-change protection.

The shortcuts above are defaults. Edit **Pause / resume shortcut** and **Stop shortcut** beneath the progress area and click **Apply shortcuts**. Use Ctrl or Alt, optionally Shift, plus a letter, digit, or F1–F24 (for example `Ctrl+Shift+P`). The shortcuts must differ. Changes are disabled during a session and last for the current launch. The shortcut summary and typing/paused messages show the active bindings.

Hotkeys must both register successfully or starting is disabled. If a requested shortcut is reserved or already used, the app attempts to restore the previous pair and reports whether those remain active. If restoration fails, starting stays disabled until a working pair is applied. You can choose different shortcuts without restarting. Hotkeys are unregistered and active work is cancelled when the window closes. A second session cannot start while one is active. Input waits while Ctrl, Alt, Shift, or a Windows key is held so shortcut key releases do not modify typed characters.

The preview accepts tabs; press Ctrl+Tab or Shift+Tab to navigate out with the keyboard. Controls have accessible names and labels, and the layout scrolls on smaller/scaled displays.

## Settings and output semantics

**Keep screen on while typing** is enabled by default. During active typing (including random input), the app requests that Windows keep the display and computer awake using `SetThreadExecutionState`. The request starts after the countdown, is released on pause, stop, completion, error, or app exit, and is renewed on resume. Uncheck it before starting to use normal power behavior. This does not change your power-plan settings, prevent manual sleep, disable a screensaver, or override lock/security policies. The status area shows whether the request is active; a failure to acquire it stops the session with an error.

Select **Random English words** to type without importing or entering text. Set **Random characters** (default 1,000; range 1–100,000), then start and focus the destination during the countdown. Each session randomly selects complete words from a bundled, curated English vocabulary, with spaces, punctuation, and line breaks when preservation is enabled. No dictionary download is needed. Words are randomly combined, not grammatical sentences; the final word is chosen to fit rather than cut short. The preview is ignored and kept intact. The session stops at the requested character count (including spaces and punctuation); pause/resume, emergency stop, focus protection, WPM, variation, temporary corrections, and punctuation/newline pauses all use the normal typing engine. No random command keys or modifier shortcuts are generated. The pre-start estimate uses a representative sample; the session estimate uses the actual generated text.

| Setting | Default | Allowed range |
| --- | --- | --- |
| WPM (five characters per word) | 60 | 1–1000 |
| Speed variation | 15% | 0–100% |
| Temporary error probability per eligible character | 2% | 0–25% |
| Countdown | 5 seconds | 1–60 whole seconds |
| Comma pause | 150 ms | 0–10000 whole ms |
| Sentence-ending pause | 350 ms | 0–10000 whole ms |
| Newline pause | 500 ms | 0–10000 whole ms |
| New line shortcut | Enter | Enter, Shift+Enter, Ctrl+Enter, Alt+Enter |

Preserve line breaks, temporary errors, and stop-on-window-change are enabled by default. Character pacing uses `60000 / (WPM * 5)` milliseconds with uniform variation. Punctuation adds its configured delay. Blank lines receive a pause per newline. Estimates exclude countdown and paused time and are approximate; scheduling and target performance affect actual duration. Character counts use Unicode scalar values, so an emoji counts once, while a combining sequence may count more than once.

CRLF and CR normalize to one logical newline, sent using the configured **New line shortcut** (Enter by default; choose Shift+Enter or Ctrl+Enter for chat and messaging apps like Slack, Teams, Discord, or WhatsApp where Enter sends the message, or Alt+Enter for spreadsheets). Disabling preservation converts each logical newline into one space and updates the preview at start. The engine preserves the resulting prepared text in a normally functioning plain-text destination. It sends UTF-16 Unicode events (surrogate pairs in one input batch), Enter or modifier newlines, Tab, and Backspace through `SendInput`; it does not use the clipboard to type. Clipboard access occurs only when the user chooses Paste or uses normal editor paste.

Temporary errors use QWERTY neighbors and are corrected with Backspace before proceeding. Only simple ASCII prose words are eligible. Tokens containing URLs, email addresses, paths, digits, internal punctuation, mixed-case identifiers, or underscores are protected. Code fences, backticks, indented code, and common code punctuation protect entire lines. This is a conservative heuristic, not a programming-language parser; disable temporary errors for source code or other exacting content. Non-ASCII characters are sent directly without deliberate errors.

## Safety and limitations

Use only where automated input is permitted. This app has no features to bypass anti-cheat, proctoring, anti-bot, monitoring, or security systems.

- Use an editable plain-text target and test a short sample first. Autocorrect, smart quotes, indentation, keyboard handlers, IMEs, selections, or rejected input can change the result. Enter may submit a form or message; Tab may move focus. The app cannot verify the destination's final text or guarantee exact output in arbitrary applications.
- Cancellation or focus loss can leave partial text, including a temporary wrong character. Immediate stop takes priority over correction. No cleanup input is sent after cancellation. Review the target before restarting; a new session starts at the beginning, not the last position.
- Focus protection tracks the top-level window, not individual text controls, caret position, or document tabs. Windows focus can change between checking the handle and sending input. Avoid interacting with the target during typing. Turning protection off allows subsequent input to follow other foreground windows, but input into TypingMimic itself is always blocked. A pending correction never Backspaces into a different foreground window.
- Windows can block input into elevated applications or secure desktops. Input rejection reports an error. The app does not elevate itself or bypass Windows input restrictions.
- PDF extraction uses [UglyToad PdfPig](https://github.com/UglyToad/PdfPig) (`PdfPig` NuGet package), page by page with paragraph heuristics. Columns, tables, ligatures, reading order, and unusual font encodings may extract imperfectly. Always review the preview.
- Scanned/image-only or empty PDFs show an informative message; OCR is not implemented. Encrypted, damaged, and inaccessible documents report an error and retain existing preview text. There is no password prompt. Mixed text/image PDFs may extract only their text portions. PDF parsing runs off the UI thread; cancellation is checked between pages and cannot interrupt an individual parser call.
- Settings and text are not persisted between launches.

## Structure and tests

- `src/TypingMimic.App`: WPF interface, `MainViewModel`, Windows input, foreground-window checks, and global hotkey lifecycle.
- `src/TypingMimic.Core`: immutable validated settings, delay/error logic, asynchronous typing engine, and `IPdfTextExtractor`/PdfPig implementation. A future OCR implementation can use the extractor interface.
- `tests/TypingMimic.Tests`: xUnit tests use only fake keyboard/foreground services. They never send keyboard input to the desktop. Fixtures generate small text/blank/damaged PDFs locally.

Tests run on Windows and cover WPM, randomized delay bounds, error probability and neighbors, protected tokens, Unicode output after correction, newline preparation, pause/resume, cancellation, duplicate sessions, target changes, modifier waiting, validation, PDF extraction/empty-text detection, and WPF layout/bindings. The layout test never shows a desktop window or registers native hotkeys.

Manual release checks: use a plain-text editor for a short Unicode/paragraph/tab sample; check both global shortcuts during countdown and typing; change windows; test hotkey conflicts; try a scanned and encrypted PDF; resize at 100%, 150%, and 200% display scaling. Automated tests cannot establish compatibility with every external target.

## Verification performed

On September 17, 2026, dependency restore completed, the Release solution built with zero warnings/errors, and all 34 automated tests passed. A separate executable smoke check confirmed the WPF main window opened and closed normally with exit code 0. No real keyboard input was sent during verification. External-editor compatibility, physical global shortcut use, and display-scaling checks remain manual checks.
