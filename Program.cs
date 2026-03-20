using BarsTool;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string command = args[0].ToLowerInvariant();
try
{
    return command switch
    {
        "barslist" => HandleBarsList(args[1..]),
        "bars" => HandleBars(args[1..]),
        "wav2bfwav" => HandleWav2Bfwav(args[1..]),
        "bfwav2wav" => HandleBfwav2Wav(args[1..]),
        "wav2bfstm" => HandleWav2Bfstm(args[1..]),
        "bfstm2wav" => HandleBfstm2Wav(args[1..]),
        "bfstm2bfstp" => HandleBfstm2Bfstp(args[1..]),
        _ => PrintUsage()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

static int HandleWav2Bfwav(string[] args)
{
    if (args.Length >= 1 && args[0] == "--bulk")
        return BulkConvert(args[1..], ".wav", ".bfwav", d => BfwavFile.ConvertFromWav(d));
    if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool wav2bfwav <input.wav> <output.bfwav>\n       BarsTool wav2bfwav --bulk <input_dir> <output_dir>"); return 1; }
    byte[] bfwav = BfwavFile.ConvertFromWav(args[0]);
    File.WriteAllBytes(args[1], bfwav);
    Console.WriteLine($"Converted {args[0]} -> {args[1]} ({bfwav.Length} bytes)");
    return 0;
}

static int HandleBfwav2Wav(string[] args)
{
    if (args.Length >= 1 && args[0] == "--bulk")
        return BulkConvert(args[1..], ".bfwav", ".wav", BfwavFile.ConvertToWav);
    if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool bfwav2wav <input.bfwav> <output.wav>\n       BarsTool bfwav2wav --bulk <input_dir> <output_dir>"); return 1; }
    byte[] wavData = BfwavFile.ConvertToWav(File.ReadAllBytes(args[0]));
    File.WriteAllBytes(args[1], wavData);
    Console.WriteLine($"Converted {args[0]} -> {args[1]} ({wavData.Length} bytes)");
    return 0;
}

static int HandleWav2Bfstm(string[] args)
{
    if (args.Length >= 1 && args[0] == "--bulk")
        return BulkConvert(args[1..], ".wav", ".bfstm", d => BfstmFile.ConvertFromWav(d));
    if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool wav2bfstm <input.wav> <output.bfstm>\n       BarsTool wav2bfstm --bulk <input_dir> <output_dir>"); return 1; }
    byte[] bfstm = BfstmFile.ConvertFromWav(File.ReadAllBytes(args[0]));
    File.WriteAllBytes(args[1], bfstm);
    Console.WriteLine($"Converted {args[0]} -> {args[1]} ({bfstm.Length} bytes)");
    return 0;
}

static int HandleBfstm2Wav(string[] args)
{
    if (args.Length >= 1 && args[0] == "--bulk")
        return BulkConvert(args[1..], ".bfstm", ".wav", BfstmFile.ConvertToWav);
    if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool bfstm2wav <input.bfstm> <output.wav>\n       BarsTool bfstm2wav --bulk <input_dir> <output_dir>"); return 1; }
    byte[] wavData = BfstmFile.ConvertToWav(File.ReadAllBytes(args[0]));
    File.WriteAllBytes(args[1], wavData);
    Console.WriteLine($"Converted {args[0]} -> {args[1]} ({wavData.Length} bytes)");
    return 0;
}

static int HandleBfstm2Bfstp(string[] args)
{
    if (args.Length >= 1 && args[0] == "--bulk")
        return BulkConvert(args[1..], ".bfstm", ".bfstp", BfstmFile.GenerateBfstp);
    if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool bfstm2bfstp <input.bfstm> <output.bfstp>\n       BarsTool bfstm2bfstp --bulk <input_dir> <output_dir>"); return 1; }
    byte[] bfstp = BfstmFile.GenerateBfstp(File.ReadAllBytes(args[0]));
    File.WriteAllBytes(args[1], bfstp);
    Console.WriteLine($"Generated {args[1]} ({bfstp.Length} bytes)");
    return 0;
}

static int BulkConvert(string[] args, string srcExt, string dstExt, Func<byte[], byte[]> convert)
{
    if (args.Length < 2) { Console.Error.WriteLine($"Usage: --bulk <input_dir> <output_dir>"); return 1; }
    string inputDir = args[0], outputDir = args[1];
    if (!Directory.Exists(inputDir)) { Console.Error.WriteLine($"Input directory not found: {inputDir}"); return 1; }
    Directory.CreateDirectory(outputDir);

    var files = Directory.GetFiles(inputDir, "*" + srcExt);
    if (files.Length == 0) { Console.Error.WriteLine($"No {srcExt} files found in '{inputDir}'"); return 1; }

    int converted = 0;
    foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
    {
        string outName = Path.GetFileNameWithoutExtension(file) + dstExt;
        string outPath = Path.Combine(outputDir, outName);
        try
        {
            byte[] dstData = convert(File.ReadAllBytes(file));
            File.WriteAllBytes(outPath, dstData);
            Console.WriteLine($"  {Path.GetFileName(file)} -> {outName}");
            converted++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  {Path.GetFileName(file)}: FAILED - {ex.Message}");
        }
    }
    Console.WriteLine($"Converted {converted}/{files.Length} files.");
    return 0;
}

static int HandleBarsList(string[] args)
{
    if (args.Length == 0) return PrintBarsListUsage();
    string sub = args[0].ToLowerInvariant();

    switch (sub)
    {
        case "list":
        {
            if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool barslist list <file.barslist>"); return 1; }
            var bl = BarsListFile.Read(args[1]);
            Console.WriteLine($"Name: {bl.Name}");
            Console.WriteLine($"Version: {bl.Version}");
            Console.WriteLine($"Entries ({bl.Entries.Count}):");
            foreach (var entry in bl.Entries)
                Console.WriteLine($"  {entry}");
            return 0;
        }

        case "add":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool barslist add <file.barslist> <name.bars> [name2.bars ...]"); return 1; }
            var bl = BarsListFile.Read(args[1]);
            for (int i = 2; i < args.Length; i++)
            {
                string name = args[i];
                bl.AddEntry(name);
                Console.WriteLine($"Added: {name}");
            }
            bl.Save(args[1]);
            Console.WriteLine($"Saved {args[1]}");
            return 0;
        }

        case "remove":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool barslist remove <file.barslist> <name.bars>"); return 1; }
            var bl = BarsListFile.Read(args[1]);
            string name = args[2];
            if (bl.RemoveEntry(name))
            {
                bl.Save(args[1]);
                Console.WriteLine($"Removed '{name}' from {args[1]}");
            }
            else
            {
                Console.Error.WriteLine($"Entry '{name}' not found.");
                return 1;
            }
            return 0;
        }

        case "create":
        {
            if (args.Length < 4) { Console.Error.WriteLine("Usage: BarsTool barslist create <output.barslist> --name <name> <entry1.bars> [...]"); return 1; }
            string output = args[1];
            string? listName = null;
            var entries = new List<string>();

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--name" && i + 1 < args.Length)
                    listName = args[++i];
                else
                    entries.Add(args[i]);
            }

            if (listName == null) { Console.Error.WriteLine("--name is required."); return 1; }

            var bl = new BarsListFile { Name = listName, Entries = entries };
            bl.Save(output);
            Console.WriteLine($"Created {output} with {entries.Count} entries.");
            return 0;
        }

        default:
            return PrintBarsListUsage();
    }
}

static int HandleBars(string[] args)
{
    if (args.Length == 0) return PrintBarsUsage();
    string sub = args[0].ToLowerInvariant();

    switch (sub)
    {
        case "list":
        {
            if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool bars list <file.bars>"); return 1; }
            var bars = BarsFile.Read(args[1]);
            Console.WriteLine($"Version: 0x{bars.Version:X4}");
            Console.WriteLine($"Assets ({bars.Assets.Count}):");
            foreach (var asset in bars.Assets)
            {
                string audioInfo = "(no audio)";
                if (asset.AudioData != null && asset.AudioData.Length >= 4)
                {
                    string audioMagic = System.Text.Encoding.ASCII.GetString(asset.AudioData, 0, 4);
                    audioInfo = $"{audioMagic}, {asset.AudioData.Length} bytes";
                }
                int amtaSize = asset.Amta?.BuildNew().Length ?? 0;
                Console.WriteLine($"  {asset.Name} [0x{asset.Hash:X8}] - AMTA: {amtaSize}B, Audio: {audioInfo}");
            }
            return 0;
        }

        case "add":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool bars add <file.bars> [audio|folder ...] [--name <name>] [--stream] [--waves <dir>] [--streams <dir>]"); return 1; }
            string barsPath = args[1];
            string? explicitName = null;
            string? streamsDir = null;
            string? wavesDir = null;
            bool treatAsStream = false;
            var audioPaths = new List<string>();

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--name" && i + 1 < args.Length)
                    explicitName = args[++i];
                else if (args[i] == "--streams" && i + 1 < args.Length)
                    streamsDir = args[++i];
                else if (args[i] == "--waves" && i + 1 < args.Length)
                    wavesDir = args[++i];
                else if (args[i] == "--stream")
                    treatAsStream = true;
                else
                    audioPaths.Add(args[i]);
            }

            var bars = File.Exists(barsPath) ? BarsFile.Read(barsPath) : new BarsFile();

            var resolvedPaths = ResolveAudioPaths(audioPaths, treatAsStream);
            foreach (string audioPath in resolvedPaths)
            {
                string assetName = (explicitName != null && resolvedPaths.Count == 1)
                    ? explicitName
                    : Path.GetFileNameWithoutExtension(audioPath);

                string ext = Path.GetExtension(audioPath).ToLowerInvariant();
                if (ext == ".bfstm" || ext == ".bfstp" || (treatAsStream && ext == ".wav"))
                {
                    byte[] fileData;
                    if (ext == ".wav")
                    {
                        Console.WriteLine($"Converting {Path.GetFileName(audioPath)} to BFSTM...");
                        fileData = BfstmFile.ConvertFromWav(File.ReadAllBytes(audioPath));
                    }
                    else
                        fileData = File.ReadAllBytes(audioPath);

                    if (ext == ".bfstp")
                    {
                        bars.AddAudio(assetName, bars.FindAsset(assetName)?.Amta ?? throw new Exception($"BFSTP import requires existing AMTA for '{assetName}'"), fileData);
                    }
                    else
                    {
                        var info = BfstmFile.ReadInfo(fileData);
                        byte[] bfstp = BfstmFile.GenerateBfstp(fileData);
                        var amta = AmtaFile.CreateFromBfstm(assetName, info, fileData);
                        bars.AddAudio(assetName, amta, bfstp);
                    }
                    Console.WriteLine($"Added stream '{assetName}'");
                }
                else
                {
                    byte[] bfwavData = LoadAsBfwav(audioPath);
                    var bfwavInfo = BfwavFile.ReadInfo(bfwavData);
                    var amta = AmtaFile.CreateFromBfwav(assetName, bfwavInfo, bfwavData);
                    bars.AddAudio(assetName, amta, bfwavData);
                    Console.WriteLine($"Added '{assetName}'");
                }
            }

            // Process waves directory
            if (wavesDir != null)
            {
                var waveFiles = ResolveAudioPaths([wavesDir], false);
                foreach (string wavePath in waveFiles)
                {
                    string assetName = Path.GetFileNameWithoutExtension(wavePath);
                    byte[] bfwavData = LoadAsBfwav(wavePath);
                    var bfwavInfo = BfwavFile.ReadInfo(bfwavData);
                    var amta = AmtaFile.CreateFromBfwav(assetName, bfwavInfo, bfwavData);
                    bars.AddAudio(assetName, amta, bfwavData);
                    Console.WriteLine($"Added '{assetName}'");
                }
            }

            // Process streams directory
            if (streamsDir != null)
            {
                var streamFiles = ResolveAudioPaths([streamsDir], true);
                foreach (string streamPath in streamFiles)
                {
                    string assetName = Path.GetFileNameWithoutExtension(streamPath);
                    string ext = Path.GetExtension(streamPath).ToLowerInvariant();
                    byte[] fileData;

                    if (ext == ".wav")
                    {
                        Console.WriteLine($"Converting {Path.GetFileName(streamPath)} to BFSTM...");
                        fileData = BfstmFile.ConvertFromWav(File.ReadAllBytes(streamPath));
                    }
                    else if (ext == ".bfstm")
                    {
                        fileData = File.ReadAllBytes(streamPath);
                    }
                    else continue;

                    var info = BfstmFile.ReadInfo(fileData);
                    byte[] bfstp = BfstmFile.GenerateBfstp(fileData);
                    var amta = AmtaFile.CreateFromBfstm(assetName, info, fileData);
                    bars.AddAudio(assetName, amta, bfstp);
                    Console.WriteLine($"Added stream '{assetName}'");
                }
            }

            bars.Save(barsPath);
            Console.WriteLine($"Saved {barsPath} ({bars.Assets.Count} total assets)");
            return 0;
        }

        case "create":
        {
            if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool bars create <output.bars> [audio|folder ...] [--names n1,n2,...] [--waves <dir>] [--streams <dir>]"); return 1; }
            string output = args[1];
            var audioPaths = new List<string>();
            string[]? names = null;
            string? streamsDir = null;
            string? wavesDir = null;

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--names" && i + 1 < args.Length)
                    names = args[++i].Split(',');
                else if (args[i] == "--streams" && i + 1 < args.Length)
                    streamsDir = args[++i];
                else if (args[i] == "--waves" && i + 1 < args.Length)
                    wavesDir = args[++i];
                else
                    audioPaths.Add(args[i]);
            }

            var bars = new BarsFile();
            var resolvedPaths = ResolveAudioPaths(audioPaths, false);

            for (int i = 0; i < resolvedPaths.Count; i++)
            {
                string audioPath = resolvedPaths[i];
                string assetName = (names != null && i < names.Length) ? names[i] : Path.GetFileNameWithoutExtension(audioPath);
                string ext = Path.GetExtension(audioPath).ToLowerInvariant();

                if (ext == ".bfstm")
                {
                    byte[] bfstmData = File.ReadAllBytes(audioPath);
                    var info = BfstmFile.ReadInfo(bfstmData);
                    byte[] bfstp = BfstmFile.GenerateBfstp(bfstmData);
                    var amta = AmtaFile.CreateFromBfstm(assetName, info, bfstmData);
                    bars.AddAudio(assetName, amta, bfstp);
                    Console.WriteLine($"Added stream '{assetName}'");
                }
                else
                {
                    byte[] bfwavData = LoadAsBfwav(audioPath);
                    var bfwavInfo = BfwavFile.ReadInfo(bfwavData);
                    var amta = AmtaFile.CreateFromBfwav(assetName, bfwavInfo, bfwavData);
                    bars.AddAudio(assetName, amta, bfwavData);
                    Console.WriteLine($"Added '{assetName}'");
                }
            }

            // Process waves directory
            if (wavesDir != null)
            {
                var waveFiles = ResolveAudioPaths([wavesDir], false);
                foreach (string wavePath in waveFiles)
                {
                    string assetName = Path.GetFileNameWithoutExtension(wavePath);
                    byte[] bfwavData = LoadAsBfwav(wavePath);
                    var bfwavInfo = BfwavFile.ReadInfo(bfwavData);
                    var amta = AmtaFile.CreateFromBfwav(assetName, bfwavInfo, bfwavData);
                    bars.AddAudio(assetName, amta, bfwavData);
                    Console.WriteLine($"Added '{assetName}'");
                }
            }

            // Process streams directory
            if (streamsDir != null)
            {
                var streamFiles = ResolveAudioPaths([streamsDir], true);
                foreach (string streamPath in streamFiles)
                {
                    string assetName = Path.GetFileNameWithoutExtension(streamPath);
                    string ext = Path.GetExtension(streamPath).ToLowerInvariant();
                    byte[] fileData;

                    if (ext == ".wav")
                    {
                        Console.WriteLine($"Converting {Path.GetFileName(streamPath)} to BFSTM...");
                        fileData = BfstmFile.ConvertFromWav(File.ReadAllBytes(streamPath));
                    }
                    else if (ext == ".bfstm")
                        fileData = File.ReadAllBytes(streamPath);
                    else continue;

                    var info = BfstmFile.ReadInfo(fileData);
                    byte[] bfstp = BfstmFile.GenerateBfstp(fileData);
                    var amta = AmtaFile.CreateFromBfstm(assetName, info, fileData);
                    bars.AddAudio(assetName, amta, bfstp);
                    Console.WriteLine($"Added stream '{assetName}'");
                }
            }

            if (bars.Assets.Count == 0) { Console.Error.WriteLine("No audio assets specified. Provide files, --waves, or --streams."); return 1; }

            bars.Save(output);
            Console.WriteLine($"Created {output} with {bars.Assets.Count} assets.");
            return 0;
        }

        case "remove":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool bars remove <file.bars> <asset_name>"); return 1; }
            string barsPath = args[1];
            string assetName = args[2];

            var bars = BarsFile.Read(barsPath);
            if (bars.RemoveAsset(assetName))
            {
                bars.Save(barsPath);
                Console.WriteLine($"Removed '{assetName}' from {barsPath} ({bars.Assets.Count} remaining)");
            }
            else
            {
                Console.Error.WriteLine($"Asset '{assetName}' not found in {barsPath}.");
                return 1;
            }
            return 0;
        }

        case "extract":
        {
            if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool bars extract <file.bars> [--output <dir>] [--wav] [--streams-dir <dir>]"); return 1; }
            string barsPath = args[1];
            string outputDir = Path.GetFileNameWithoutExtension(barsPath);
            bool convertToWav = false;
            string? streamsOutDir = null;

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--output" && i + 1 < args.Length)
                    outputDir = args[++i];
                else if (args[i] == "--wav")
                    convertToWav = true;
                else if (args[i] == "--streams-dir" && i + 1 < args.Length)
                    streamsOutDir = args[++i];
            }

            var bars = BarsFile.Read(barsPath);
            Directory.CreateDirectory(outputDir);
            if (streamsOutDir != null) Directory.CreateDirectory(streamsOutDir);

            foreach (var asset in bars.Assets)
            {
                if (asset.AudioData == null || asset.AudioData.Length == 0) continue;
                string audioMagic = asset.AudioData.Length >= 4
                    ? System.Text.Encoding.ASCII.GetString(asset.AudioData, 0, 4) : "";

                if (audioMagic == "FSTP" && streamsOutDir != null)
                {
                    File.WriteAllBytes(Path.Combine(streamsOutDir, asset.Name + ".bfstp"), asset.AudioData);
                    Console.WriteLine($"Extracted: {asset.Name}.bfstp -> {streamsOutDir}");
                }
                else if (convertToWav && audioMagic == "FWAV")
                {
                    try
                    {
                        byte[] wavData = BfwavFile.ConvertToWav(asset.AudioData);
                        File.WriteAllBytes(Path.Combine(outputDir, asset.Name + ".wav"), wavData);
                        Console.WriteLine($"Extracted: {asset.Name}.wav");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to convert {asset.Name}: {ex.Message}, saving raw.");
                        File.WriteAllBytes(Path.Combine(outputDir, asset.Name + ".bfwav"), asset.AudioData);
                    }
                }
                else
                {
                    string audioExt = audioMagic switch
                    {
                        "FWAV" => ".bfwav",
                        "FSTP" => ".bfstp",
                        "BWAV" => ".bwav",
                        _ => ".bin"
                    };
                    File.WriteAllBytes(Path.Combine(outputDir, asset.Name + audioExt), asset.AudioData);
                    Console.WriteLine($"Extracted: {asset.Name}{audioExt}");
                }
            }
            return 0;
        }

        default:
            return PrintBarsUsage();
    }
}


static int PrintUsage()
{
    Console.WriteLine("""
    BarsTool - Nintendo BARS/BARSLIST audio tool

    Usage: BarsTool <command> [options]

    Commands:
      barslist        Manage .barslist (ARSL) files
      bars            Manage .bars (BARS) files
      wav2bfwav       Convert WAV to BFWAV  (--bulk for folder)
      bfwav2wav       Convert BFWAV to WAV  (--bulk for folder)
      wav2bfstm       Convert WAV to BFSTM  (--bulk for folder)
      bfstm2wav       Convert BFSTM to WAV  (--bulk for folder)
      bfstm2bfstp     Generate BFSTP from BFSTM (--bulk for folder)

    Run 'BarsTool <command>' for more help.
    """);
    return 1;
}

static int PrintBarsListUsage()
{
    Console.WriteLine("""
    BarsTool barslist - Manage .barslist (ARSL) files

    Subcommands:
      list    <file.barslist>                                List entries
      add     <file.barslist> <name.bars> [name2.bars ...]   Add entries
      remove  <file.barslist> <name.bars>                    Remove an entry
      create  <output.barslist> --name <name> <entry1.bars> [...]  Create new
    """);
    return 1;
}

static int PrintBarsUsage()
{
    Console.WriteLine("""
    BarsTool bars - Manage .bars (BARS) files

    Subcommands:
      list    <file.bars>                                              List assets
      add     <file.bars> [audio|folder ...] [--name <n>] [--stream] [--waves <dir>] [--streams <dir>]
      remove  <file.bars> <asset_name>                                Remove an asset
      create  <output.bars> [audio|folder ...] [--names n1,n2,...] [--waves <dir>] [--streams <dir>]
      extract <file.bars> [--output <dir>] [--wav] [--streams-dir <dir>]

    Paths can be .wav, .bfwav, .bfstm files, or folders.
    WAV files are auto-converted. BFSTM files generate BFSTP + AMTA.
    --waves <dir>    Add WAV/BFWAV files from dir as wave assets
    --streams <dir>  Add BFSTM/WAV files from dir as stream assets
    """);
    return 1;
}


static List<string> ResolveAudioPaths(List<string> inputs, bool includeStreams)
{
    var result = new List<string>();
    foreach (string input in inputs)
    {
        if (Directory.Exists(input))
        {
            var files = Directory.GetFiles(input, "*.*")
                .Where(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".wav" || ext == ".bfwav") return true;
                    if (includeStreams && (ext == ".bfstm" || ext == ".bfstp")) return true;
                    return false;
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                Console.Error.WriteLine($"Warning: no audio files found in '{input}'");

            result.AddRange(files);
        }
        else
        {
            result.Add(input);
        }
    }
    return result;
}

static byte[] LoadAsBfwav(string audioPath)
{
    byte[] audioData = File.ReadAllBytes(audioPath);
    string ext = Path.GetExtension(audioPath).ToLowerInvariant();

    if (ext == ".wav")
    {
        Console.WriteLine($"Converting {Path.GetFileName(audioPath)} to BFWAV...");
        return BfwavFile.ConvertFromWav(audioData);
    }
    if (ext == ".bfwav")
        return audioData;

    throw new InvalidOperationException($"Unsupported audio format: {ext}");
}
