# BarsTool

Command-line tool for managing Nintendo `.bars` and `.barslist` audio files (Splatoon 2, other games untested). Handles BARS archives, BFWAV, BFSTM, BFSTP (prefetch), and AMTA metadata. Supports WAV conversion and bulk operations.

Requires .NET 8.0.

## Build

```
dotnet build -c Release
```

The executable will be at `bin/Release/net8.0/BarsTool.exe`.

## Usage

```
BarsTool <command> [options]
```

### bars -- Manage .bars (BARS) files

**List assets in a BARS file:**
```
BarsTool bars list <file.bars>
```
```
BarsTool bars list BgmVersus.bars
```

**Add audio to a BARS file** (creates the file if it doesn't exist):
```
BarsTool bars add <file.bars> [audio|folder ...] [--name <name>] [--stream] [--waves <dir>] [--streams <dir>]
```
- `--stream` -- treat WAV files as streams (convert to BFSTM + generate BFSTP/AMTA) instead of BFWAV
```
BarsTool bars add BgmVersus.bars Jingle_win.wav
BarsTool bars add BgmVersus.bars Jingle_win.bfwav --name Jingle_win
BarsTool bars add BgmVersus.bars Stream\STRM_Versus01.bfstm
BarsTool bars add BgmVersus.bars my_track.wav --stream
BarsTool bars add BgmVersus.bars --waves extracted_bfwav --streams Stream
BarsTool bars add BgmVersus.bars --streams Stream
```

**Create a new BARS file:**
```
BarsTool bars create <output.bars> [audio|folder ...] [--names n1,n2,...] [--waves <dir>] [--streams <dir>]
```
```
BarsTool bars create BgmVersus_new.bars extracted_bfwav --streams Stream
BarsTool bars create BgmVersus_new.bars --waves extracted_bfwav --streams Stream
BarsTool bars create StreamOnly.bars --streams Stream
```

**Remove an asset by name:**
```
BarsTool bars remove <file.bars> <asset_name>
```
```
BarsTool bars remove BgmVersus.bars STRM_Versus01
```

**Extract all assets:**
```
BarsTool bars extract <file.bars> [--output <dir>] [--wav] [--streams-dir <dir>]
```
- `--wav` -- convert extracted BFWAVs to WAV
- `--streams-dir <dir>` -- write BFSTP (stream prefetch) files to a separate directory
```
BarsTool bars extract BgmVersus.bars --output extracted_bfwav --streams-dir extracted_bfstp
BarsTool bars extract BgmVersus.bars --output extracted_wav --wav --streams-dir extracted_bfstp
```

Paths can be `.wav`, `.bfwav`, `.bfstm` files, or folders containing them.
WAV files are auto-converted to BFWAV. BFSTM files auto-generate BFSTP + AMTA.
`--waves <dir>` adds all WAV/BFWAV files from a directory as wave assets.
`--streams <dir>` adds all BFSTM/WAV files from a directory as stream assets.

### barslist -- Manage .barslist (ARSL) files

**List entries:**
```
BarsTool barslist list <file.barslist>
```
```
BarsTool barslist list Sound.barslist
```

**Add entries:**
```
BarsTool barslist add <file.barslist> <name.bars> [name2.bars ...]
```
```
BarsTool barslist add Sound.barslist BgmVersus.bars BgmCoop.bars
```

**Remove an entry:**
```
BarsTool barslist remove <file.barslist> <name.bars>
```
```
BarsTool barslist remove Sound.barslist BgmVersus.bars
```

**Create a new barslist:**
```
BarsTool barslist create <output.barslist> --name <name> <entry1.bars> [...]
```
```
BarsTool barslist create Sound.barslist --name Sound BgmVersus.bars BgmCoop.bars
```

### Audio conversion

All conversion commands support `--bulk <input_dir> <output_dir>` for folder conversion.

**WAV <-> BFWAV:**
```
BarsTool wav2bfwav <input.wav> <output.bfwav>
BarsTool wav2bfwav --bulk <input_dir> <output_dir>
BarsTool bfwav2wav <input.bfwav> <output.wav>
BarsTool bfwav2wav --bulk <input_dir> <output_dir>
```
```
BarsTool wav2bfwav Jingle_win.wav Jingle_win.bfwav
BarsTool wav2bfwav --bulk wav_dir bfwav_dir
BarsTool bfwav2wav Jingle_win.bfwav Jingle_win.wav
BarsTool bfwav2wav --bulk bfwav_dir wav_dir
```

**WAV <-> BFSTM:**
```
BarsTool wav2bfstm <input.wav> <output.bfstm>
BarsTool wav2bfstm --bulk <input_dir> <output_dir>
BarsTool bfstm2wav <input.bfstm> <output.wav>
BarsTool bfstm2wav --bulk <input_dir> <output_dir>
```
```
BarsTool wav2bfstm track.wav track.bfstm
BarsTool wav2bfstm --bulk wav_dir bfstm_dir
BarsTool bfstm2wav STRM_Versus01.bfstm STRM_Versus01.wav
BarsTool bfstm2wav --bulk bfstm_dir wav_dir
```

**Generate BFSTP prefetch from BFSTM:**
```
BarsTool bfstm2bfstp <input.bfstm> <output.bfstp>
BarsTool bfstm2bfstp --bulk <input_dir> <output_dir>
```
```
BarsTool bfstm2bfstp STRM_Versus01.bfstm STRM_Versus01.bfstp
BarsTool bfstm2bfstp --bulk Stream bfstp_out
```

## Full pipeline example (Splatoon 2)

Extract everything from a BARS file, then rebuild it from the raw assets:

```bash
# 1. Extract BFWAVs and BFSTPs from the original BARS
BarsTool bars extract samples\BgmVersus.bars --output work\bfwav --streams-dir work\bfstp

# 2. Rebuild BARS using BFWAVs + original BFSTMs from the Stream folder
BarsTool bars create work\BgmVersus_rebuilt.bars --waves work\bfwav --streams samples\Stream
```

The rebuilt BARS will contain:
- BFWAV assets with auto-generated AMTA (from the extracted `.bfwav` files)
- BFSTP prefetch assets with auto generated AMTA (from the `.bfstm` files in `Stream`)
- Generated BFSTPs match the originals byte-for-byte

## Notes

- WAV files are automatically converted to BFWAV (DSP ADPCM) when added to BARS.
- BFSTM files added to BARS auto generate a BFSTP prefetch (first 5 interleaved sample blocks per channel) and AMTA metadata.
- AMTA metadata is computed from audio properties (sample rate, channels, loop points, loudness, peak amplitude).
- Audio folders can contain mixed formats (`.wav`, `.bfwav`, `.bfstm`) -- each is handled appropriately.
