[中文](README.md) | **English**

# Dropline

A drag-and-drop shelf for Windows. Start dragging a file and a shelf slides out at the right edge of the screen. Park files there, switch windows, desktops, or apps, then drag them out to their destination.

Inspired by [Yoink for Mac](https://eternalstorms.at/yoink/mac/) by Eternal Storms Software. Dropline is an independent open-source implementation. It is not affiliated with Yoink or its developer and does not use their code or assets.

## Features

- The shelf appears when dragging files from Explorer or the desktop. The tray menu can enable detection for dragging in any app.
- Files placed on the shelf are referenced by path rather than copied. Dragging them out uses normal Windows file drag and drop.
- Multiple files dropped together form a stack. Use the context menu to split it.
- Images, text, and links dragged from browsers or editors are saved as PNG, TXT, and URL files in the clippings folder.
- When occupied, the shelf can collapse into a small edge tab; hover to expand it.
- `Ctrl + Alt + D` toggles the shelf.
- Tray options include startup at login, retaining items after dragging, clearing the shelf, and opening the clippings folder.
- Double-click to open an item. Right-click to reveal it in Explorer, copy its path, or remove it from the shelf.
- Shelf contents survive restarts and are stored under `%AppData%\Dropline`.

## Download and run

Download `Dropline-Windows-vX.Y.Z.zip` from [Releases](https://github.com/jiuyang5354/Dropline-/releases), extract it, and run `Dropline.exe`. It runs in the system tray without an installer. Requires Windows 10/11 and .NET Framework 4.8.

## Build

```powershell
cd windows
dotnet build -c Release
```

## Known limitations

- Windows exposes no public system event for the start of a file drag. Automatic appearance is inferred with a global mouse hook after the pointer moves more than 10 pixels while pressed, excluding title-bar window drags.
- Windows privilege isolation (UIPI) prevents drag and drop between administrator windows and a normally running Dropline.
- The shelf currently docks only to the right edge of the primary monitor.

## License

[MIT](LICENSE).
