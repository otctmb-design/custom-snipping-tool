# Custom Snipping Tool

A standalone Windows screenshot utility with automatic saving to Downloads.
The original Windows Snipping Tool is not modified.

## Features

- Rectangle, freeform, window, and full virtual-screen capture.
- Optional automatic uniquely named PNG saves to the Windows Downloads folder.
- Clipboard image copying.
- Local Windows OCR with text copying.
- Aspect-ratio-preserving resizing that saves a copy.
- HEX and RGB screen color picking.
- Save clipboard text as a UTF-8 file.
- Notification-area operation and optional background startup.
- Compact Capture, Save, Clear, Tools, and Settings controls.
- Custom draggable, resizable window frame with minimize/maximize controls.
- Charcoal-and-blue styling, subtle shadows, gradients, and hover/click fades.
- Clear preview and text, with ownership-checked clearing of the app's clipboard copy.
- Persistent settings for auto-save, preview opening, sign-in startup, minimized
  startup, shortcuts, and button animation.
- Original blue capture-frame and mint flash icon for the desktop, taskbar,
  tray, and app header.

## Requirements

64-bit Windows 10 or Windows 11 with .NET Framework 4.x and Windows
PowerShell 5.1. OCR requires an installed language supported by Windows OCR.
No external packages or cloud OCR service are used.

## Build and test

From Windows PowerShell in this project folder:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The script compiles the application and runs image-processing self-tests.
The execution-policy option affects only that PowerShell process.
The build output is in `dist`; keep the executable and OCR script together.

```powershell
.\dist\CustomSnippingTool.exe
```

For quiet startup:

```powershell
.\dist\CustomSnippingTool.exe --background
```

Use **Settings > Start with Windows (at sign-in)** to manage automatic startup.
**Start minimized to the tray when Windows starts** controls whether sign-in
startup opens the window. Opening the desktop shortcut always opens the UI.
Startup uses a per-user Windows Run entry. The app also recognizes and safely
replaces an earlier Startup shortcut that points to this same executable.

## Shortcuts and use

- **Shift + backslash:** start rectangle capture. On a US keyboard, this
  replaces typing `|` while capture shortcuts are enabled.
- **Print Screen:** start rectangle capture while the application is running.
  If another application intercepts it, use the desktop shortcut or menu.
- **Fn + Y:** only works if keyboard firmware maps it to Print Screen.
  Windows cannot normally register Fn as a keyboard modifier.
- **Escape or right-click:** cancel selection.

Open the app window to choose other capture modes and actions.
Closing the window hides it to the notification area. Double-click the tray
icon to reopen it. Right-click the tray icon and choose **Exit and restore
normal keys** to stop it. In Settings, uncheck **Enable capture shortcuts** to pause key
overrides.

**Capture...** offers all four capture modes; the hotkeys capture a rectangle
directly. **Tools...** contains copying, OCR, resizing, color picking, clipboard
text saving, and Open Downloads.

Auto-save and preview opening are enabled by default, preserving the original
behavior. In Settings:

- Turn off **Automatically save captures to Downloads** for clipboard-only
  captures. Use **Save** to persist the current capture, or **Save copy** for
  another uniquely named PNG. Unsaved captures are lost when replaced, cleared,
  or the app exits.
- Turn off **Open the preview after a screenshot** for quiet captures. The app
  stays in the tray after success and shows a brief notification. Reopen it
  through the desktop shortcut or tray. Canceling restores the previous
  visibility; errors reopen the window.
- Disable button animations for instant transitions. Windows reduced-animation
  settings are also respected.

Captures always copy to the clipboard. OCR and color picking
replace the clipboard contents with text. Resize preserves the original PNG.
**Resize a copy** explicitly saves the resized result even if auto-save is off.
OCR also works on unsaved captures using a temporary PNG that is removed after
recognition; it does not permanently save the capture to Downloads.
**Clear** resets the preview and text and removes the current clipboard contents
only if they are still the image or text copied by this app. Newer copies from
other apps and all saved files are preserved. Clear is disabled during OCR.
Interface borders are decorative and are not added to saved screenshots.

## Limitations

- Window capture saves visible pixels within a window's bounds; it does not
  recover obscured content.
- Full-screen capture includes all monitors.
- Protected/DRM content may appear black.
- OCR has a Windows-defined maximum image dimension; resize large captures
  before extracting text.
- This version does not provide video capture, annotations, batch renaming,
  ZIP creation, or Downloads organization.

## Source and tests

- `src/CustomSnippingTool.cs`: WinForms application and image self-tests.
- `src/Read-ScreenshotText.ps1`: local Windows OCR bridge.
- `src/CustomSnippingTool.exe.manifest`: DPI-awareness and non-admin execution.
- `build.ps1`: compilation and self-test runner.
- `assets/build-icon.ps1`: generates the original multi-resolution Windows icon
  and PNG artwork; the build embeds the icon in the executable.
- `test-behavior.ps1` and `tests/BehaviorTests.cs`: interactive behavior tests.

Self-tests cover rectangle cropping, off-screen bounds, freeform transparency,
aspect-ratio resizing, invalid resize rejection, unique PNG filenames, PNG
roundtripping, and Downloads detection. Hotkeys, capture overlays, clipboard
behavior, and OCR also require interactive desktop testing.

Run interactive tests from an unlocked Windows desktop:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\test-behavior.ps1
```

These tests exercise all four auto-save/preview combinations, manual saving,
Clear, button fades, and OCR on an unsaved image. They capture the screen and
replace the clipboard; test-created saved PNGs are deleted afterward. They do
not change your preferences or startup configuration.

## Removal

Disable **Start with Windows** in Settings, exit through the tray menu, remove any desktop shortcuts you
created, then remove the application folder. Saved captures are not deleted.
Settings are stored under `HKCU\Software\CustomSnippingTool`. Startup uses
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CustomSnippingTool`.

## GitHub upload

Create a repository named `custom-snipping-tool` in your GitHub account.
Private visibility is a reasonable starting choice. Extract this package,
then upload the contents of this project folder to the repository, including
the hidden `.gitignore` file. Do not upload screenshots or personal clipboard
files. This package contains source only, not captured images or credentials.
