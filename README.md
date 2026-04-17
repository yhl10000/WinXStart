# WinXStart

A lightweight Windows 10‑style Start Menu replacement built with WPF.  
Press **Win + Alt + Z** anywhere to bring up a gorgeous, fully customizable app launcher.

![demo](assets/demo.png)

## Features

- **Global Hotkey** — `Win+Alt+Z` toggles the launcher from anywhere
- **App Scanner** — automatically discovers installed apps from Start Menu folders
- **Pin & Tile Groups** — pin apps as resizable tiles, organize them into custom groups
- **Drag & Drop** — reorder tiles within or across groups
- **Tile Resizing** — Small / Medium / Large per tile
- **Search** — instant filtering as you type
- **Pin from File** — right-click to pin any `.exe` or `.lnk` directly
- **System Tray** — lives quietly in the notification area; double-click to open
- **Start with Windows** — optional auto-start via registry
- **Fully Themeable** — edit `settings.json` to customize gradient colors, opacity, border, fonts, and more

## Requirements

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or later)

## Build & Run

```bash
dotnet build
dotnet run
```

## Configuration

On first launch a `settings.json` file is created next to the executable.  
Open it from the tray icon → **Open Settings**, or edit manually:

```jsonc
{
  "WindowSizePercent": 70,       // window size as % of screen (10–100)
  "Opacity": 80,                 // background opacity (0–100)
  "GradientColors": [            // any number of hex color stops
    "#FF0000", "#FF7700", "#FFFF00",
    "#00CC00", "#0000FF", "#4B0082", "#8B00FF"
  ],
  "GradientDirection": "Diagonal", // Diagonal / Horizontal / Vertical
  "CornerRadius": 8,
  "BorderColor": "#66FFFFFF",
  "TileFontSize": 12,
  "GroupFontSize": 13
}
```

Changes take effect after restarting the app.

## Tech Stack

| Layer | Technology |
|-------|------------|
| UI | WPF (XAML + C#) |
| Runtime | .NET 10 |
| Architecture | MVVM (ViewModelBase, RelayCommand) |
| Hotkey | Win32 `RegisterHotKey` via P/Invoke |
| Tray | WinForms `NotifyIcon` |
| Persistence | JSON (System.Text.Json) |

## License

MIT
