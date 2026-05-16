# ScrollerView By Unnamed10110

A lightweight Windows tray utility that captures **scrollable regions** of the
screen and stitches them into one clean, seamless PNG. It supports
**classic region**, **horizontal scroll**, **vertical scroll**, **auto
direction**, **manual scroll fallback**, and **full-page browser** captures.

Press a global hotkey, drag a rectangle (or click an automatically detected
UI element) over the content you want, and ScrollerView will:

1. (Optionally) wait a configurable countdown so you can move focus.
2. Detect the underlying scrollable UI element on the chosen axis.
3. Reset its scroll position to the start.
4. Take screenshots, scroll one step, take more screenshots, repeat.
5. Stitch them into a single image with **no overlap and no cut content**.
6. Restore the original scroll position.
7. Open a built-in AMOLED editor where you can crop, annotate (rectangles,
  arrows, highlights, blur, speech balloons, text, numbered step markers,
   magnifiers, spotlights), strip-delete rows/columns, zoom in/out, run
   OCR from a dialog, search inside the capture, export to PDF, share via custom commands,
   undo/redo (labeled in the undo stack), and press `Enter` to save and copy in
   one shot.

## Features

- Runs in the system tray, no main window.
- Four rebindable global hotkeys (defaults can be customized in
`Settings...`):
  - `Shift + Alt + A` - classic region capture (single screenshot, no scrolling).
  - `Shift + Alt + S` - vertical scroll capture.
  - `Shift + Alt + D` - horizontal scroll capture.
  - *(unassigned)* - auto-direction scroll capture (detects best axis and asks
  if ambiguous).
- Region selection overlay with a **deep black** full-screen dim (not a milky
gray haze), plus **UIA preselection** that highlights
the window/pane/grid/document under the cursor. `Tab` cycles candidates,
click accepts, drag overrides, `Esc` cancels.
- Auto-detect direction: when both axes are scrollable, an AMOLED chooser
appears; when neither is scrollable, classic region or manual scroll is
offered.
- Manual scroll fallback with a topmost controller overlay: scroll the page
yourself, frames are sampled, and ScrollerView stitches them on `Enter`.
- Full-page browser capture via Chrome / Edge DevTools Protocol
(`Page.captureScreenshot` with `captureBeyondViewport`). If no debug
endpoint is running, ScrollerView can launch a dedicated browser
profile with remote debugging enabled.
- Multi-monitor and Per-Monitor V2 DPI aware (HiDPI correct).
- Pixel-exact stitching with no interpolation. The matcher uses grayscale
SAD on a central band perpendicular to the scroll axis so scrollbars and
sticky headers/footers cannot poison the alignment.
- Conservative end detection: stops when UI Automation reports the scroll
has reached the end, when two consecutive scroll attempts fail to move
the content, or when the captured frame becomes pixel-identical to the
previous one. During vertical or horizontal scroll capture, press **Esc**
to stop early and stitch whatever frames were captured so far.
- Cursor parking: the cursor is moved out of the capture region before each
frame, so it never appears in the stitched image.
- Sticky header / footer trimming (`Auto` / `Aggressive` / `Off`) during
vertical stitching, so repeated top/bottom bands are not duplicated.
- Editor with a full object model: every annotation is selectable, movable,
resizable, and undoable. Includes:
  - Rectangle, highlight, blur (editable, non-destructive), arrow, text,
  numbered step marker, magnifier, spotlight, speech balloon.
  - **OCR / Search** dialog (toolbar): run Windows OCR, view and edit full extracted text,
  and search with live match highlights on the canvas.
  - PDF export with `A4`, `Letter`, or `Fit width` page sizes for tall
  captures.
  - Configurable share targets (custom executable + arguments).
- Recent captures tray submenu (last N) with `Open`, `Copy`, `Open folder`.
- Filename template support with tokens like `{date}`, `{time}`,
`{datetime}`, `{mode}`, `{direction}`, `{app}`, `{title}`, `{width}`,
`{height}`.

## Usage

<video controls src="Scroller_Unnamed10110.mp4" title="Title"></video>

## Requirements

- Windows 10 / 11 (x64).
- .NET 8 SDK to build from source, or .NET 8 Desktop Runtime to run a
framework-dependent build. A `publish`ed self-contained build has no
runtime requirement.
- OCR requires a Windows 10 1809 (build 17763) or newer image with an OCR
language pack installed (most US/English/Spanish/etc. installs already
have one).
- Full-page browser capture requires Chrome or Edge. If your browser is
not running with `--remote-debugging-port`, ScrollerView can launch
a dedicated profile for you.

## Build

From the repo root, in a Developer Command Prompt or any cmd shell:

```bat
build.bat            REM Debug build
build.bat release    REM Release build
build.bat publish    REM Self-contained single-file Release build (win-x64)
```

Outputs:

- Debug:   `bin\Debug\net8.0-windows10.0.19041.0\ScrollerCapture.exe`
- Release: `bin\Release\net8.0-windows10.0.19041.0\ScrollerCapture.exe`
- Publish: `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\ScrollerCapture.exe`

You can also build directly with the SDK:

```bat
dotnet build -c Release
```

## Usage

1. Run `ScrollerCapture.exe` (product name: **ScrollerView By Unnamed10110**). A small arrow icon appears in the system tray.
2. Switch to the app containing the content you want to capture.
3. Press the hotkey for your desired capture mode:
  - `Shift + Alt + A` - region capture (single screenshot).
  - `Shift + Alt + S` - vertical scroll capture.
  - `Shift + Alt + D` - horizontal scroll capture.
  - Or use the tray menu for `Capture auto`, `Full-page browser capture`,
  or `Manual scroll capture`.
4. Drag a rectangle over the area you want to capture, or hover over a
  detected UI element (highlighted with a red preselection box) and click
   to accept it. Press `Esc` to cancel.
5. Wait for the optional countdown (`Settings...` > `Capture delay`).
6. The editor opens with the result. Annotate, OCR-search, export, then
  press `Enter` to save and copy to the clipboard (or `Esc` to discard).
7. A tray balloon appears with the saved file name. The tray's
  `Recent captures` submenu remembers the last N captures.

### Editor shortcuts


| Key                             | Action                                                                                                  |
| ------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `Enter`                         | Save flattened image to the default folder, copy it to the clipboard, and close the editor              |
| `Esc`                           | If something is selected: deselect. Otherwise close without saving (prompts if there are unsaved edits) |
| `V`                             | Select tool                                                                                             |
| `X`                             | Cutout / crop tool                                                                                      |
| `C`                             | Strip cutout (full-width row band or full-height column band)                                           |
| `R`                             | Rectangle annotation                                                                                    |
| `B`                             | Blur annotation (editable, non-destructive until save)                                                  |
| `A`                             | Arrow                                                                                                   |
| `H`                             | Highlight                                                                                               |
| `S`                             | Speech balloon                                                                                          |
| `T`                             | Text annotation                                                                                         |
| `N`                             | Numbered step marker                                                                                    |
| `M`                             | Magnifier                                                                                               |
| `O`                             | Spotlight                                                                                               |
| `Double-click`                  | Edit the text of the speech balloon / text annotation under the cursor                                  |
| `Delete`                        | Delete the currently selected annotation                                                                |
| `Alt + Left/Right/Up/Down`      | Snap the selected speech balloon's tail to that direction                                               |
| `[` / `]`                       | Send selected annotation backward / forward in z-order                                                  |
| `Ctrl + Wheel`                  | Zoom in/out around the cursor                                                                           |
| `Shift + Wheel`                 | Pan horizontally                                                                                        |
| `Wheel`                         | Pan vertically                                                                                          |
| `Middle drag` or `Space + drag` | Pan                                                                                                     |
| `F`                             | Fit image to window                                                                                     |
| `1`                             | Zoom to 100%                                                                                            |
| `Ctrl + Shift + O`              | Open **OCR / Search** (same as **Tools** menu)                                                          |
| `Ctrl + Z` / `Ctrl + Y`         | Undo / Redo                                                                                             |
| `Ctrl + S`                      | Save As (choose path and format) - also copies to clipboard                                             |
| `Ctrl + C`                      | Copy flattened image to clipboard                                                                       |


**OCR / Search**: use the menu **Tools → OCR / Search…**, the toolbar button **OCR / Search…** (after **Copy**), or **Ctrl+Shift+O**. Run **Run OCR** to fill an editable full-text area (also copied to the clipboard); use the search box for on-image highlights. Closing the dialog clears highlights.

Tray menu options:

- **Capture region / vertical / horizontal / auto** - trigger any mode
manually (current shortcut is shown in parentheses).
- **Full-page browser capture** - DevTools-based capture of Chrome/Edge.
- **Manual scroll capture** - frame-sampled fallback when UIA is unavailable.
- **Recent captures** - reopen, copy to clipboard, or open the folder of
any recent capture.
- **Settings...** - rebind hotkeys, set capture delay, sticky-trim mode,
and filename template.
- **Open output folder** - opens the captures folder.
- **Exit** - quit the app.

### Changing capture shortcuts and settings

Open the tray menu and choose `Settings...`:

1. Click a hotkey field, press the desired combination (any of `Ctrl`,
  `Shift`, `Alt` plus a regular key, e.g. `Ctrl+Alt+H`, `Shift+F9`,
   `Ctrl+Numpad 1`). `Backspace` clears, `Esc` cancels capturing.
2. Pick a capture delay (`0`, `1`, `2`, `3`, or `5` seconds). When > 0 an
  AMOLED countdown overlay appears before each capture.
3. Choose a sticky header/footer trimming mode (`Off`, `Auto`, `Aggressive`).
4. Edit the filename template if you want, using tokens like
  `{date}`, `{time}`, `{datetime}`, `{mode}`, `{direction}`, `{app}`,
   `{title}`, `{width}`, `{height}`.
5. Click `Save`. ScrollerView immediately unregisters previous hotkeys,
  registers the new ones, and persists everything to
   `%LOCALAPPDATA%\ScrollerView\settings.json`. If a chosen combination
   is already in use elsewhere it rolls back to the previously working set.

Use `Reset to defaults` to restore `Shift+Alt+A/S/D` and the default
capture options.

### Speech balloon editing

After you create a balloon with `S`, the editor automatically switches to
`V` (Select). While a balloon is selected:

- Drag inside the body to **move** it.
- Drag any corner/edge handle to **resize** it.
- Drag the green tail handle to **reposition the tail**.
- `Alt + Left/Right/Up/Down` snaps the tail to that direction.
- `Double-click` edits the text. `Delete` removes it.

### Strip cutout (`C`)

Remove an entire row band from a tall screenshot (or an entire column band
from a wide screenshot) and join the remaining parts seamlessly:

- If your drag rectangle is **wider than tall**, ScrollerView deletes
rows `[Y, Y + Height)` across the **full image width**.
- If it is **taller than wide**, it deletes columns `[X, X + Width)`
across the **full image height**.

Annotations entirely above/left of the strip stay in place, annotations
entirely below/right are shifted so their relative position is preserved,
and annotations that intersect the strip are dropped. Undoable.

### Full-page browser capture

Tray > `Full-page browser capture`:

1. ScrollerView probes the DevTools endpoints configured in
  `Settings...` > `Browser` (default port 9222).
2. If found, it lists open tabs and lets you pick one. The selected tab is
  captured beyond the viewport using `Page.captureScreenshot`.
3. If no endpoint is running, the app offers to launch a dedicated
  Chrome / Edge instance with `--remote-debugging-port` and a clean
   user-data directory. The original browser is never touched.

### Manual scroll fallback

Use this when a control doesn't expose UI Automation scrolling (some
games, RDP, custom canvases). The capture region is locked, a topmost
overlay appears with instructions, and you scroll the page manually.
ScrollerView samples frames every few hundred ms, keeps only ones with
a real pixel delta, infers the dominant direction, and stitches them when
you press `Enter`. Press `Esc` to cancel.

## Output

PNG files default to:

```
%USERPROFILE%\Pictures\ScrollerView\<filename-template>.png
```

The default template is `scroll-capture-{datetime}-{mode}`. Pressing
`Enter` in the editor saves the file **and** copies the same flattened
image to the Windows clipboard. The tray's `Recent captures` submenu and
`%LOCALAPPDATA%\ScrollerView\history.json` keep track of the last N
captures.

## How it works

```
Hotkey  ->  RegionSelectionForm  ->  CaptureRouter
                                    |
              ----------------------+----------------------
              |          |         |           |          |
        Region only  Horizontal  Vertical  Auto detect  Manual / Browser
                          |         |
                          v         v
                  ScrollCaptureService  +  ImageStitcher
                                    |
                                    v
                        CaptureEditorForm (AMOLED editor)
                                    |
                                    v
                  Save + clipboard + history + (PDF / share / OCR)
```

See [ImageStitcher.cs](ImageStitcher.cs) for the matching algorithm,
[ScrollCaptureService.cs](ScrollCaptureService.cs) for the direction-aware
scroll loop, [BrowserDevTools.cs](BrowserDevTools.cs) for the CDP capture,
and [OcrService.cs](OcrService.cs) for OCR.

## Project layout


| File                                                                                                                                                                                  | Role                                                                                                                         |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| [Program.cs](Program.cs)                                                                                                                                                              | Entry point, single-instance mutex, DPI setup                                                                                |
| [TrayApplicationContext.cs](TrayApplicationContext.cs)                                                                                                                                | NotifyIcon, hotkeys, capture orchestration, recent captures                                                                  |
| [AppSettings.cs](AppSettings.cs)                                                                                                                                                      | Persisted hotkeys, capture options, output, browser settings                                                                 |
| [HotkeyBinding.cs](HotkeyBinding.cs) / [HotkeySettings.cs](HotkeySettings.cs)                                                                                                         | Modifier + key bindings for region / vertical / horizontal / auto                                                            |
| [SettingsForm.cs](SettingsForm.cs)                                                                                                                                                    | Settings dialog (hotkeys, delay, sticky mode, filename template)                                                             |
| [NativeMethods.cs](NativeMethods.cs)                                                                                                                                                  | P/Invoke (RegisterHotKey, BitBlt, SendInput, DPI, foreground info)                                                           |
| [CaptureMode.cs](CaptureMode.cs) / [CaptureResult.cs](CaptureResult.cs)                                                                                                               | Capture command shape                                                                                                        |
| [RegionSelectionForm.cs](RegionSelectionForm.cs)                                                                                                                                      | Dark selection overlay with UIA preselection                                                                                 |
| [UiElementDetector.cs](UiElementDetector.cs)                                                                                                                                          | UI Automation candidate detection                                                                                            |
| [ScrollableElementFinder.cs](ScrollableElementFinder.cs)                                                                                                                              | Direction-aware scrollability discovery and inspection                                                                       |
| [ScreenCapture.cs](ScreenCapture.cs)                                                                                                                                                  | GDI BitBlt screen capture                                                                                                    |
| [ScrollCaptureService.cs](ScrollCaptureService.cs)                                                                                                                                    | Direction-aware scroll + capture loop, cursor parking                                                                        |
| [ManualCaptureService.cs](ManualCaptureService.cs)                                                                                                                                    | Manual scroll fallback sampling and stitching                                                                                |
| [BrowserCaptureService.cs](BrowserCaptureService.cs) / [BrowserDevTools.cs](BrowserDevTools.cs) / [DevToolsClient.cs](DevToolsClient.cs) / [BrowserDiscovery.cs](BrowserDiscovery.cs) | DevTools Protocol full-page browser capture                                                                                  |
| [ImageStitcher.cs](ImageStitcher.cs)                                                                                                                                                  | Pixel-shift alignment and final composition, sticky trim                                                                     |
| [CountdownOverlayForm.cs](CountdownOverlayForm.cs) / [CaptureDirectionChooserForm.cs](CaptureDirectionChooserForm.cs)                                                                 | Pre-capture overlays                                                                                                         |
| [CaptureEditorForm.cs](CaptureEditorForm.cs)                                                                                                                                          | Post-capture editor window with toolbar and status                                                                           |
| [EditorCanvasControl.cs](EditorCanvasControl.cs)                                                                                                                                      | Canvas: zoom/pan, drag preview, tool dispatch, selection, undo/redo, search overlays                                         |
| [EditorTool.cs](EditorTool.cs)                                                                                                                                                        | Enum of editor tools                                                                                                         |
| [EditorAnnotation.cs](EditorAnnotation.cs)                                                                                                                                            | Annotation object model (rectangle / blur / arrow / highlight / speech balloon / text / step marker / magnifier / spotlight) |
| [OcrSearchForm.cs](OcrSearchForm.cs)                                                                                                                                                  | OCR + in-capture search dialog                                                                                               |
| [ImageEditing.cs](ImageEditing.cs)                                                                                                                                                    | Crop, mosaic blur, strip cutout, flatten                                                                                     |
| [FilenameTemplateService.cs](FilenameTemplateService.cs) / [CaptureHistoryService.cs](CaptureHistoryService.cs)                                                                       | Filename templates and persistent recent-captures history                                                                    |
| [ForegroundInfo.cs](ForegroundInfo.cs)                                                                                                                                                | Foreground window title / process                                                                                            |
| [PdfExportService.cs](PdfExportService.cs)                                                                                                                                            | Tall capture -> paginated PDF via PdfSharp                                                                                   |
| [ShareTarget.cs](ShareTarget.cs)                                                                                                                                                      | Configurable share targets / custom commands                                                                                 |
| [OcrService.cs](OcrService.cs)                                                                                                                                                        | Windows.Media.Ocr text extraction with bounding boxes                                                                        |
| [ScrollerCapture.csproj](ScrollerCapture.csproj)                                                                                                                                      | Project file (net8.0-windows10.0.19041.0, WinForms + WPF + WinRT)                                                            |
| [app.manifest](app.manifest)                                                                                                                                                          | Long-path support manifest                                                                                                   |
| [build.bat](build.bat)                                                                                                                                                                | Convenience build script                                                                                                     |


## Limitations

ScrollerView relies on Windows UI Automation to know whether the content
under the cursor is scrollable on the requested axis. This covers a wide
range of apps: most browser pages, Win32 / WPF / WinUI / WinForms controls,
Office, Visual Studio, file dialogs, grids, editors, etc.

UIA cannot reach surfaces such as some games, full-screen GPU apps, RDP /
Citrix sessions, or certain custom-drawn canvases. For those, use the
**manual scroll** fallback or the **full-page browser** mode.

If the underlying control snaps scroll by a full viewport with no overlap
between adjacent frames, the stitcher aborts with a clear message instead
of producing a torn image.

OCR requires a Windows 10 1809+ image with an OCR language pack installed.
The OCR dialog reports "OCR not available" if the platform requirements
are not met.

## Credits

Created by **Unnamed10110**.

- [trojan.v6@gmail.com](mailto:trojan.v6@gmail.com)
- [sergiobritos10110@gmail.com](mailto:sergiobritos10110@gmail.com)

