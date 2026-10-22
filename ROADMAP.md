# NormalKeyboardSwitcher2 roadmap

NormalKeyboardSwitcher2 will provide recently used input-language switching and a language indicator beside the text caret on Windows and Linux. It is especially useful with more than two installed input languages. The Linux goal covers major desktops and display servers through native integrations, with support earned by testing each environment.

## Product requirements

- The language indicator belongs beside the active text caret. A panel indicator, pointer-following badge, or fixed screen position is not a substitute.
- Prefer supported operating-system, desktop, compositor, and input-method facilities wherever they provide the required behavior. Do not assume Windows offers the best integration model, or copy Windows mechanisms onto Linux without evaluating native alternatives.
- Preserve quick toggling between the two most recently used languages and cycling through additional languages. Respect normal shortcuts, AltGr, composition, and the user's existing input methods.
- Confirm actual language changes before updating history. Preserve layout variants and distinguish language selection from an IME's composition mode.
- Do not steal focus, consume ordinary typing, or insert text to display the indicator. If caret geometry becomes unavailable, hide the badge and report the unsupported condition in settings; do not display a guessed location.
- Keep typed text out of logs. Request geometry and input-state metadata where possible. Any input-method integration that receives surrounding text must discard it when it is not needed.

## Native capability investigation

Before choosing a UI framework or promising platform coverage, build small prototypes that switch languages and display a badge beside the caret. Evaluate event delivery, coordinate systems, focus, permissions, and coexistence with the user's input-method framework.

| Target | Integration paths to evaluate | Required proof |
| --- | --- | --- |
| Windows | Native caret, MSAA, UI Automation, IAccessible2, keyboard-layout APIs | Correct source selection, confirmed switching, mixed-DPI placement, and elevated-window behavior |
| Linux on X11 | XKB, desktop input-source services, AT-SPI, X11 window placement | Switching and caret-relative placement across native and browser text fields |
| GNOME on Wayland | GNOME Shell and its input-method integration, supported accessibility facilities | A caret-anchored badge with the existing input-method setup |
| KDE Plasma on Wayland | KWin/Plasma and supported input-method protocols or extensions | A caret-anchored badge with native and XWayland applications |
| Other Wayland desktops | Advertised input-method/popup protocols and compositor extension mechanisms | A tested backend for each compositor family; do not assume protocol availability |

Wayland text-input protocols let participating applications report a caret rectangle. Input-method protocols can let the compositor position input UI near that rectangle. These are useful native mechanisms, not ordinary global window-positioning APIs. Support, access policy, and geometry availability must be checked for the chosen compositor and application. The input-method-v2 protocol allows only one input method per seat, so investigate extending or cooperating with the existing framework before proposing a replacement. See the [text-input protocol](https://raw.githubusercontent.com/wayland-mirror/wayland-protocols/main/unstable/text-input/text-input-unstable-v3.xml) and [input-method protocol](https://raw.githubusercontent.com/swaywm/wlroots/master/protocol/input-method-unstable-v2.xml).

## Milestone 1 Windows distribution

Ship a reproducible per-user Windows x64 installer with the runtime included, Start menu entries, upgrades, and uninstallation. Preserve user preferences across upgrades, avoid changing startup preferences without a user choice, and protect shortcut restoration when the application is running. Keep generated binaries out of source commits; provide build outputs or release assets separately.

Acceptance: validate fresh installation, repeated installation/upgrade, installed executable launch, startup registration handling, and uninstallation. Document supported OS/architecture, build requirements, package signing status, and how to reproduce the package.

## Milestone 2 Linux feasibility prototypes

Develop the X11, GNOME Wayland, KDE Wayland, and other Wayland prototypes independently. Choose the most capable supported integration for each environment. Record exact desktop, compositor, toolkit, and protocol versions in a compatibility matrix, together with the applications tested and any missing functionality.

Acceptance: demonstrate the badge beside a moving caret and confirmed language switching in an ordinary text editor, a browser page, a browser address bar, LibreOffice, and a terminal. Test Home/End, selection changes, scrolling, empty fields, input-method composition, and multiple languages. An environment remains experimental until it passes its applicable checks.

## Milestone 3 Shared core and platform contracts

Extract language history, switch reconciliation, preferences, and platform-independent tests from the Windows application. Keep keyboard event translation, language identifiers, accessibility providers, and lifecycle handling in platform backends.

Define contracts for enumerating input sources, requesting and observing switches, handling gestures, tracking caret anchors, and presenting the indicator. A caret anchor must support both screen rectangles and compositor-managed surface anchors; global screen coordinates must not be a universal requirement. Expose backend capabilities and failures explicitly.

Select shared or native settings UI after the prototypes establish the integration constraints. Retain the working Windows UI until a replacement demonstrably preserves its behavior. Avoid requiring a common rendering toolkit for compositor-owned indicators.

Acceptance: shared tests pass for all backends, and Windows behavior remains stable while Linux integrations are added.

## Milestone 4 Desktop integration and reliability

Turn validated prototypes into maintained backends. Handle focus transitions, desktop locking, suspend/resume, compositor restarts, hotplugged displays, fractional scaling, multiple seats where supported, and accessibility-provider failures. Keep UI updates independent of slow provider calls and reject stale results.

Acceptance: repeated tests on light/dark fields, mixed DPI, RTL text, scrolling, language changes during composition, and fields without caret accessibility. Verify the indicator never blocks clicks or remains beside an inactive field. Test with both two and more than two input languages.

## Milestone 5 Linux packaging and releases

Package the service and any required desktop extensions together with explicit version compatibility. Choose distribution packages, user-service installation, or sandboxed packaging according to the integrations actually required; do not assume one package format can access every desktop facility.

Publish reproducible builds, checksums, installation/removal instructions, and a tested support matrix. Add signing where signing credentials are available. Release desktop backends independently when needed, with clear experimental status for environments that have not passed acceptance.

Acceptance: clean install, upgrade, and uninstall on each advertised target; existing input methods and shortcut settings remain usable after removal. Public documentation contains product requirements and reproducible procedures, not development-machine details.
