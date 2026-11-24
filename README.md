# MIDI Trim & Merge

A small Windows desktop tool (WinForms, .NET Framework 4.8) for trimming and merging Standard MIDI files.
The interface is available in English and Serbian (Srpski).

## Features

- **Add files** by button or drag & drop; reorder by dragging or with Move up / Move down.
- **Trim** each file to a start/end time — originals are never modified.
- **Merge** in two modes:
  - *One after another* — songs play sequentially in list order.
  - *Layered* — all songs play together from the start.
- **Overlap between songs** — start the next song a few seconds early for smooth transitions (global or per song).
- **Per-song adjustments** — transpose, speed, volume, forced instruments per channel.
- **Karaoke support** — detects lyric text and allows editing it, with code-page selection.
- **Key & tempo detection** — reads key signatures or estimates the key from the notes.
- **Built-in player** — preview through any installed Windows MIDI output device.
- **Undo** and crash-safe handling that keeps the file list intact.

## Build

Requires the .NET SDK (targets `net48`, runs on Windows 7 SP1+ with .NET 4.8 and is built into Windows 10/11).

```
dotnet build -c Release
```

The executable is produced as `bin/Release/net48/MidiTrimMerge.exe`.

## Project layout

| File | Purpose |
|---|---|
| `Program.cs` | Entry point and global error handling |
| `MainForm.cs` | Main window, file list, trim/merge UI, localized strings |
| `MainForm.Advanced.cs` | Per-song settings (transpose, speed, volume, instruments, karaoke) |
| `MainForm.Player.cs` | Preview playback UI |
| `MainForm.Undo.cs` | Undo history |
| `MainForm.Safety.cs` | Crash handling / state recovery |
| `Midi.cs` | MIDI file reading/writing, tempo map, trim/merge operations |
| `MidiPlayer.cs` | Playback through Windows MIDI output devices |
