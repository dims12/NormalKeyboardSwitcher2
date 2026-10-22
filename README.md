# NormalKeyboardSwitcher2

A Windows desktop language switcher with Alt+Tab-style recently used ordering and a language badge beside the text caret. Successor to NormalKeyboardSwitcher; the old source is unchanged.

## Build and run

Open `NormalKeyboardSwitcher2.sln` in JetBrains Rider. Requires Windows x64 and the .NET 10 SDK (already installed on the development machine).

```powershell
dotnet build NormalKeyboardSwitcher2.sln -c Release
dotnet run --project NormalKeyboardSwitcher2.Tests -c Release
dotnet publish NormalKeyboardSwitcher2 -c Release -o artifacts/app
.\artifacts\app\NormalKeyboardSwitcher2.exe
```

The published app uses the installed .NET 10 Windows Desktop runtime. Open settings on first launch and choose **OK**. Exit the original NormalKeyboardSwitcher first; two switchers must not handle the same gesture.

The tray icon is only for settings and Exit. The language indication itself is next to the caret.

## Switching

The top list shows installed languages. A single click switches the previously active application's language and keeps settings open; a double-click switches and returns to that application. Rows remain stable while settings are open.

The dropdown offers System, Ctrl+Shift (the original default), or left Alt+Shift. System restores Windows' shortcut and disables custom gesture handling while allowing the caret indicator and mouse selection.

OK saves the dropdown and indicator settings and closes the window. Cancel (or the close button) discards those pending edits; immediate language selections are not undone.

- Hold Ctrl (or left Alt), press and release Shift to select the previous language.
- Keep the first modifier held and tap Shift again to visit older languages.
- Release both modifiers to finish. The confirmed language moves to the front of the list.
- Another quick gesture returns to the previous language, regardless of how many layouts are installed.
- Either modifier may be pressed first. Right Alt / AltGr is excluded.
- Selection occurs on a modifier release, not its initial press, so ordinary Ctrl+Shift+letter shortcuts do not change language.
- Switching keeps the entire keyboard-layout handle, preserving layout variants.
- The app requests a change from the focused window and checks the reported result. A rejected or timed-out switch is not committed as successful.
- Switching focus during a gesture cancels the cycle. External language changes update recent ordering.
- History is shared across applications for this running session; it is not persisted across restarts. The app does not force a language onto every newly focused window.

## Automatic Windows shortcut management

Choosing a custom gesture backs up and disables only matching Ctrl+Shift or Alt+Shift entries under the current user's `Keyboard Layout\Toggle` key when settings are applied. It asks Windows to reload those settings with `SPI_SETLANGTOGGLE`. Win+Space and unrelated shortcuts are unchanged.

Switching gestures restores the old binding before managing the new one. Selecting **System** and pressing **OK** disables custom switching and restores the backup. Normal exit also restores it. The backup is written before any change; after a crash it is recovered at next startup. Restoration does not overwrite a value subsequently changed in Windows Settings. If Windows refuses to reload the setting, the app reports the failure.

Settings, the recovery journal and a small diagnostic log are in `%LOCALAPPDATA%\NormalKeyboardSwitcher2`. No typed text is logged.

## Caret and administrator windows

The indicator tries the Windows caret, explicit MSAA caret bounds, UI Automation TextPattern2 caret range, TextPattern insertion-point selection, and IAccessible2 caret offsets/character bounds. Explicit caret bounds take priority over text-range approximations: Chrome can report the start of its address field for a collapsed UI Automation range even when the visible caret is elsewhere. When an empty caret range has no rectangle, it queries an adjacent character range without reading or changing text. This edge approximation can require further tuning for bidirectional text and empty editors. Accessibility calls run on a separate background thread; unavailable or stale positions hide the badge. It uses physical screen coordinates, per-monitor DPI, and the monitor's working area. It does not activate or receive clicks. The small, borderless label sits at the lower-right of the caret. Its background is transparent; text follows the locally sampled caret color, with a black/white contrast fallback. Pixel samples are not saved. When no caret stroke is visible, the current background supplies a black/white contrast fallback, including on the first sample and for short accessibility placeholders. Caret colors are not remembered across samples or fields. Blinking is off by default and can be enabled with **Blink language indicator** in settings. When enabled, it blinks at twice the Windows-configured rate. A language change keeps the label visible for one second before blinking resumes. Color sampling runs on a background thread and cannot block language updates.

Use **Restart as administrator** for elevated editors, system property sheets and administrator CMD. This requires the normal Windows UAC approval. The application is not a driver and does not run on the UAC secure desktop, lock screen or sign-in screen.

Classic console layout detection resolves the console host thread by its window, avoiding the stale client-thread layout reported by Windows compatibility behavior. Classic ConsoleWindowClass windows also use an isolated helper that queries cursor visibility, position, font-cell size and viewport through the Windows console API. It reads no console text or input. The helper opens a read/write console handle because cursor-info queries on some hosts reject a read-only handle; it never writes to the console. The helper exits when its parent pipe closes. Caret painting runs on a 16 ms timer, and keyboard activity wakes accessibility polling. The implementation includes accessibility paths used by modern applications, but universal compatibility is not guaranteed. Some custom controls and IMEs require additional work. The badge reports the input language, not the IME's internal conversion mode. Multiple layouts of one language currently share the same short language label; full names appear in settings.

This is an initial implementation, not yet a verified replacement across every application. Specifically, classic CMD, Windows Terminal, browser editors, elevated property sheets and mixed-DPI physical monitor transitions need interactive acceptance testing on this machine. A successful build does not establish that coverage.

## Validation

The dependency-free regression executable exercises MRU cycles, failed-switch reconciliation, language installation/removal, modifier ordering, shortcut interference, AltGr, and badge placement with negative monitor coordinates.

A read-only runtime probe (no keyboard hook or settings changes):

```powershell
.\artifacts\app\NormalKeyboardSwitcher2.exe --diagnose "$PWD\artifacts\diagnostics.json"
```

An off-screen settings preview (no keyboard hook or settings changes):

```powershell
.\artifacts\app\NormalKeyboardSwitcher2.exe --render-preview "$PWD\artifacts\settings-preview.png"
```

Acceptance checklist:
- With three or more languages, quick-toggle the last two, then hold the modifier and visit all others.
- Confirm typed characters match the badge after each switch.
- Test Ctrl+Shift+C/V/arrows, both left/right Ctrl and Shift, and AltGr.
- Test ordinary text fields, CMD under both console hosts, browser inputs and the main editor.
- Repeat in elevated CMD and a system property sheet with the switcher elevated.
- Move between displays with different scaling and negative desktop coordinates; test near each edge.
- Apply each gesture, exit, and check that the previous Windows shortcut returns.
- Try changing the Windows shortcut externally while the switcher is running; verify restoration preserves that change.

## API references

- [Input-language change requests](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-inputlangchangerequest)
- [Windows shortcut reload: SPI_SETLANGTOGGLE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
- [UI Automation caret ranges](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern2-getcaretrange)
- [UI Automation security](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)

A future non-elevated UIAccess distribution would need appropriate signing, protected installation, and separate validation; the current unsigned build uses explicit elevation.






## LibreOffice accessibility

The installed LibreOffice 24.2 required a screen-reader flag or `SAL_FORCE_IACCESSIBLE2` before exposing its accessibility bridge. Forcing that bridge caused the Calc test instance to hang even with the indicator stopped, so the workaround was withdrawn. `Start-LibreOfficeAccessible.cmd` now starts Calc normally with that override cleared. The standard IAccessible2 fallback queries only focus, caret offsets and character geometry.

## Start with Windows

The settings checkbox registers the current executable in the current user?s Windows Run key when OK is clicked. Cancel leaves startup unchanged. Enabling it replaces the original NormalKeyboardSwitcher Run entry. At sign-in the configured switcher starts quietly, with standard permissions; use Restart as administrator for elevated applications. Disabling startup does not restore the old switcher. Keep the published executable in its registered location. Language rows show locale-derived three-letter badges using Windows theme colors.

When an application exposes mirrored editors without reliable accessibility focus (such as the Calc formula input line), the tracker can use the most recently clicked editor as a fallback. It reads only caret geometry within that control, with bounded traversal. The mouse hook observes left clicks without swallowing them or recording text. Tab, Enter, Escape and F6 clear that fallback.

Caret color sampling requires consistent ink over at least 80% of the full caret height. Partial text strokes, colored font edges and spell-check underlines cannot establish a caret color merely by contributing three matching pixels. Genuine colored carets remain supported.

The label paints its halo and fill with the same pixel-aligned glyphs to keep small letters legible.
