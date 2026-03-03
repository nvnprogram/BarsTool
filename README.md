# BarsTool

Command line tool for managing Nintendo Switch `.bars` and `.barslist` audio files (for Splatoon 2, other games untested). Supports creating, modifying, and extracting BARS archives, converting between WAV and BFWAV formats, and managing BARSLIST resource lists.

Requires .NET 8.0.

## Build

```
dotnet build -c Release
```

The executable will be at `bin/Release/net8.0/BarsTool.exe`.

## Usage

```
BarsTool <command> <subcommand> [options]
```

### barslist -- Manage .barslist (ARSL) files

**List entries:**
```
BarsTool barslist list <file.barslist>
```

**Add entries:**
```
BarsTool barslist add <file.barslist> <name.bars> [name2.bars ...]
```

**Remove an entry:**
```
BarsTool barslist remove <file.barslist> <name.bars>
```

**Create a new barslist:**
```
BarsTool barslist create <output.barslist> --name <name> <entry1.bars> [entry2.bars ...]
```

### bars -- Manage .bars (BARS) files

**List assets in a BARS file:**
```
BarsTool bars list <file.bars>
```

**Add audio to a BARS file** (creates the file if it doesn't exist):
```
BarsTool bars add <file.bars> <audio.wav|bfwav|folder> [...] [--name <name>]
```

**Create a new BARS file:**
```
BarsTool bars create <output.bars> <audio.wav|bfwav|folder> [...] [--names name1,name2,...]
```

**Remove an asset by name:**
```
BarsTool bars remove <file.bars> <asset_name>
```

**Extract all assets:**
```
BarsTool bars extract <file.bars> [--output <dir>] [--wav]
```
Use `--wav` to automatically convert extracted BFWAV files to WAV.

### convert -- Convert between audio formats

**Single file:**
```
BarsTool convert wav2bfwav <input.wav> <output.bfwav>
BarsTool convert bfwav2wav <input.bfwav> <output.wav>
```

**Bulk convert a folder:**
```
BarsTool convert folder <input_dir> <output_dir> [--wav|--bfwav]
```
- `--wav` (default) -- convert all `.bfwav` in input to `.wav` in output
- `--bfwav` -- convert all `.wav` in input to `.bfwav` in output

## Notes

- WAV files are automatically converted to BFWAV (DSP ADPCM) when added to BARS files.
- AMTA metadata is auto generated from the audio properties (sample rate, channels, loop points, duration).
