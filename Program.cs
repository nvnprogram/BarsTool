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
        "convert" => HandleConvert(args[1..]),
        _ => PrintUsage()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
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
            // BarsTool barslist create <output.barslist> --name <name> <entry1.bars> [entry2.bars ...]
            if (args.Length < 4) { Console.Error.WriteLine("Usage: BarsTool barslist create <output.barslist> --name <name> <entry1.bars> [...]"); return 1; }
            string output = args[1];
            string? listName = null;
            var entries = new List<string>();

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--name" && i + 1 < args.Length)
                {
                    listName = args[++i];
                }
                else
                {
                    entries.Add(args[i]);
                }
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
                Console.WriteLine($"  {asset.Name} [0x{asset.Hash:X8}] - AMTA: {asset.AmtaData?.Length ?? 0}B, Audio: {audioInfo}");
            }
            return 0;
        }

        case "add":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool bars add <file.bars> <audio|folder> [more...] [--name <name>]"); return 1; }
            string barsPath = args[1];
            string? explicitName = null;
            var audioPaths = new List<string>();

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--name" && i + 1 < args.Length)
                    explicitName = args[++i];
                else
                    audioPaths.Add(args[i]);
            }

            var resolvedPaths = ResolveAudioPaths(audioPaths);
            var bars = File.Exists(barsPath) ? BarsFile.Read(barsPath) : new BarsFile();

            foreach (string audioPath in resolvedPaths)
            {
                string assetName = (explicitName != null && resolvedPaths.Count == 1)
                    ? explicitName
                    : Path.GetFileNameWithoutExtension(audioPath);

                byte[] bfwavData = LoadAsBfwav(audioPath);
                var bfwavInfo = BfwavFile.ReadInfo(bfwavData);
                var amta = AmtaFile.CreateFromBfwav(assetName, bfwavInfo);
                bars.AddAudio(assetName, amta.BuildNew(), bfwavData);
                Console.WriteLine($"Added '{assetName}'");
            }

            bars.Save(barsPath);
            Console.WriteLine($"Saved {barsPath} ({bars.Assets.Count} total assets)");
            return 0;
        }

        case "create":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool bars create <output.bars> <audio|folder> [...] [--names n1,n2,...]"); return 1; }
            string output = args[1];
            var audioPaths = new List<string>();
            string[]? names = null;

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--names" && i + 1 < args.Length)
                    names = args[++i].Split(',');
                else
                    audioPaths.Add(args[i]);
            }

            var resolvedPaths = ResolveAudioPaths(audioPaths);

            var bars = new BarsFile();
            for (int i = 0; i < resolvedPaths.Count; i++)
            {
                string audioPath = resolvedPaths[i];
                string assetName = (names != null && i < names.Length) ? names[i] : Path.GetFileNameWithoutExtension(audioPath);
                byte[] bfwavData = LoadAsBfwav(audioPath);

                var bfwavInfo = BfwavFile.ReadInfo(bfwavData);
                var amta = AmtaFile.CreateFromBfwav(assetName, bfwavInfo);
                bars.AddAudio(assetName, amta.BuildNew(), bfwavData);
                Console.WriteLine($"Added '{assetName}'");
            }

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
            // BarsTool bars extract <file.bars> [--output <dir>] [--wav]
            if (args.Length < 2) { Console.Error.WriteLine("Usage: BarsTool bars extract <file.bars> [--output <dir>] [--wav]"); return 1; }
            string barsPath = args[1];
            string outputDir = Path.GetFileNameWithoutExtension(barsPath);
            bool convertToWav = false;

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--output" && i + 1 < args.Length)
                    outputDir = args[++i];
                else if (args[i] == "--wav")
                    convertToWav = true;
            }

            var bars = BarsFile.Read(barsPath);
            Directory.CreateDirectory(outputDir);

            foreach (var asset in bars.Assets)
            {
                if (asset.AudioData != null && asset.AudioData.Length > 0)
                {
                    string magic = asset.AudioData.Length >= 4
                        ? System.Text.Encoding.ASCII.GetString(asset.AudioData, 0, 4) : "";

                    if (convertToWav && magic == "FWAV")
                    {
                        try
                        {
                            byte[] wavData = BfwavFile.ConvertToWav(asset.AudioData);
                            File.WriteAllBytes(Path.Combine(outputDir, asset.Name + ".wav"), wavData);
                            Console.WriteLine($"Extracted: {asset.Name}.wav (converted from BFWAV)");
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Failed to convert {asset.Name}: {ex.Message}, saving raw.");
                            File.WriteAllBytes(Path.Combine(outputDir, asset.Name + ".bfwav"), asset.AudioData);
                        }
                    }
                    else
                    {
                        string audioExt = magic switch
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
            }
            return 0;
        }

        default:
            return PrintBarsUsage();
    }
}

static int HandleConvert(string[] args)
{
    if (args.Length == 0) return PrintConvertUsage();
    string sub = args[0].ToLowerInvariant();

    switch (sub)
    {
        case "wav2bfwav":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool convert wav2bfwav <input.wav> <output.bfwav>"); return 1; }
            byte[] bfwav = BfwavFile.ConvertFromWav(args[1]);
            File.WriteAllBytes(args[2], bfwav);
            Console.WriteLine($"Converted {args[1]} -> {args[2]} ({bfwav.Length} bytes)");
            return 0;
        }

        case "bfwav2wav":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool convert bfwav2wav <input.bfwav> <output.wav>"); return 1; }
            byte[] bfwavData = File.ReadAllBytes(args[1]);
            byte[] wavData = BfwavFile.ConvertToWav(bfwavData);
            File.WriteAllBytes(args[2], wavData);
            Console.WriteLine($"Converted {args[1]} -> {args[2]} ({wavData.Length} bytes)");
            return 0;
        }

        case "folder":
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: BarsTool convert folder <input_dir> <output_dir> [--wav|--bfwav]"); return 1; }
            string inputDir = args[1];
            string outputDir = args[2];
            string direction = "wav"; // default: bfwav -> wav

            for (int i = 3; i < args.Length; i++)
            {
                if (args[i] == "--wav") direction = "wav";
                else if (args[i] == "--bfwav") direction = "bfwav";
            }

            if (!Directory.Exists(inputDir)) { Console.Error.WriteLine($"Input directory not found: {inputDir}"); return 1; }
            Directory.CreateDirectory(outputDir);

            string srcExt = direction == "wav" ? ".bfwav" : ".wav";
            string dstExt = direction == "wav" ? ".wav" : ".bfwav";
            var files = Directory.GetFiles(inputDir, "*" + srcExt);

            if (files.Length == 0)
            {
                Console.Error.WriteLine($"No {srcExt} files found in '{inputDir}'");
                return 1;
            }

            int converted = 0;
            foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string outName = Path.GetFileNameWithoutExtension(file) + dstExt;
                string outPath = Path.Combine(outputDir, outName);
                try
                {
                    byte[] srcData = File.ReadAllBytes(file);
                    byte[] dstData = direction == "wav"
                        ? BfwavFile.ConvertToWav(srcData)
                        : BfwavFile.ConvertFromWav(srcData);
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

        default:
            return PrintConvertUsage();
    }
}

static int PrintUsage()
{
    Console.WriteLine("""
    BarsTool - Nintendo BARS/BARSLIST audio tool

    Usage: BarsTool <command> <subcommand> [options]

    Commands:
      barslist  Manage .barslist (ARSL) files
      bars      Manage .bars (BARS) files
      convert   Convert between audio formats

    Run 'BarsTool <command>' for subcommand help.
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
      add     <file.bars> <audio|folder> [...] [--name <n>]           Add audio assets
      remove  <file.bars> <asset_name>                                Remove an asset
      create  <output.bars> <audio|folder> [...] [--names n1,n2,...]  Create new
      extract <file.bars> [--output <dir>] [--wav]                    Extract assets

    Paths can be .wav files, .bfwav files, or folders containing them.
    WAV files are automatically converted to BFWAV (DSP ADPCM).
    """);
    return 1;
}

static int PrintConvertUsage()
{
    Console.WriteLine("""
    BarsTool convert - Convert between audio formats

    Subcommands:
      wav2bfwav  <input.wav>   <output.bfwav>                Convert WAV to BFWAV
      bfwav2wav  <input.bfwav> <output.wav>                  Convert BFWAV to WAV
      folder     <input_dir> <output_dir> [--wav|--bfwav]    Bulk convert folder
        --wav    Convert .bfwav -> .wav (default)
        --bfwav  Convert .wav -> .bfwav
    """);
    return 1;
}

static List<string> ResolveAudioPaths(List<string> inputs)
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
                    return ext == ".wav" || ext == ".bfwav";
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                Console.Error.WriteLine($"Warning: no .wav/.bfwav files found in '{input}'");

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
