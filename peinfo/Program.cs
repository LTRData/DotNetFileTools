using DiscUtils;
using DiscUtils.Compression;
using DiscUtils.Streams;
using DiscUtils.Wim;
using K4os.Compression.LZ4.Streams;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System.IO.Compression;

#if !NET6_0_OR_GREATER
using ZLibStream = DiscUtils.Compression.ZlibStream;
#endif

namespace peinfo;

public static class Program
{
    public static int Main(params string[] args)
    {
        try
        {
            return UnsafeMain(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(ex.JoinMessages());
            Console.ResetColor();
            return ex.HResult;
        }
    }

    public static int UnsafeMain(params string[] args)
    {
        var errCode = 0;

        string? imagePath = null;
        var partNo = 0;
        string? wimPath = null;
        var wimIndex = 1;
        string[] files = [];
        var showDependencyTree = false;
        var includeDelayed = false;

        var cmds = CommandLineParser.ParseCommandLine(args, StringComparer.Ordinal);

        foreach (var cmd in cmds)
        {
            if (cmd.Key == "image"
                && cmd.Value.Length == 1)
            {
                imagePath = cmd.Value[0];
            }
            else if (cmd.Key == "part"
                && cmd.Value.Length == 1
                && int.TryParse(cmd.Value[0], out partNo)
                && cmds.ContainsKey("image"))
            {
            }
            else if (cmd.Key == "wim"
                && cmd.Value.Length == 1
                && !cmds.ContainsKey("image"))
            {
                wimPath = cmd.Value[0];
            }
            else if (cmd.Key == "index"
                && cmd.Value.Length == 1
                && int.TryParse(cmd.Value[0], out wimIndex)
                && cmds.ContainsKey("wim"))
            {
            }
            else if (cmd.Key == "dep"
                && cmd.Value.Length == 0)
            {
                showDependencyTree = true;
            }
            else if (cmd.Key == "delayed"
                && cmd.Value.Length == 0
                && cmds.ContainsKey("dep"))
            {
                includeDelayed = true;
            }
            else if (cmd.Key == ""
                && cmd.Value.Length >= 1)
            {
                files = cmd.Value;
            }
            else
            {
                Console.WriteLine(@"peinfo - Show EXE, DLL, ELF etc header information
Copyright (c) 2025 - LTR Data, Olof Lagerkvist
https://ltr-data.se

Syntax:
peinfo [--dep [--delayed]] filepath1 [filepath2 ...]
peinfo --image=imagefile [--part=partno] [--dep [--delayed]] filepath1 [filepath2 ...]
peinfo --wim=imagefile --index=wimindex [--dep [--delayed]] filepath1 [filepath2 ...]

Options:
    --image=imagefile         Path to disk image file containing the files to analyze.
    --part=partno             Partition number in the disk image to use (1-based).
    --wim=imagefile           Path to WIM image file containing the files to analyze.
    --index=wimindex          WIM image index number to use (1-based).
    --dep                     Show DLL dependency tree for specified files.
    --delayed                 Include delay-loaded DLLs in dependency tree.");

                return -1;
            }
        }

        if (imagePath is not null)
        {
            DiscUtils.Complete.SetupHelper.SetupComplete();
            DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(ExFat.DiscUtils.ExFatFileSystem).Assembly);

            Console.WriteLine();
            Console.WriteLine(imagePath);
        }

        using var vdisk = imagePath is not null
            ? (VirtualDisk.OpenDisk(imagePath, FileAccess.Read)
                ?? new DiscUtils.Raw.Disk(imagePath, FileAccess.Read))
            : null;

        using var wim = wimPath is not null
            ? File.OpenRead(wimPath)
            : null;

        using var part = vdisk is not null
            ? partNo != 0
            ? (vdisk.Partitions?.Partitions?.ElementAtOrDefault(partNo - 1)?.Open()
                ?? throw new DriveNotFoundException($"Partition {partNo} not found"))
            : vdisk.Content
            : null;

        using var fs = part is not null
            ? (FileSystemManager.DetectFileSystems(part).FirstOrDefault()?.Open(part)
                ?? throw new NotSupportedException($"No supported file systems detected in partition {partNo}"))
            : wim is not null
            ? new WimFile(wim).GetImage(wimIndex - 1)
            : null;

        if (fs is not null)
        {
            files = [.. files.SelectMany(f => fs.GetFiles(Path.GetDirectoryName(f) is { Length: > 0 } dir ? dir : "", Path.GetFileName(f)))];
        }
        else
        {
            files = [.. files.SelectMany(static f => Directory.EnumerateFiles(Path.GetDirectoryName(f) is { Length: > 0 } dir ? dir : ".", Path.GetFileName(f)))];
        }

        foreach (var path in files)
        {
            try
            {
                using Stream file = fs is not null
                    ? fs.OpenFile(path, FileMode.Open, FileAccess.Read)
                    : File.OpenRead(path);

                Console.WriteLine();
                Console.WriteLine(path);

                if (showDependencyTree)
                {
                    PEViewer.ProcessDependencyTree(file, includeDelayed);
                }
                else
                {
                    ProcessFile(file);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.JoinMessages());
                Console.ResetColor();
                errCode = ex.HResult;
            }
        }

        return errCode;
    }

    public static void ProcessFile(Stream file)
        => ProcessFile(file.ReadToEnd());

    internal static byte[] ReadToEnd(this Stream file)
    {
        if (file.CanSeek)
        {
            return file.ReadExactly((int)(file.Length - file.Position));
        }
        
        using var ms = new MemoryStream();
        file.CopyTo(ms);
        return ms.ToArray();
    }

    public static void ProcessFile(byte[] fileData)
    {
        fileData = DecompressData(fileData);

        if (fileData[0] == 'P' && fileData[1] == 'K')
        {
            Console.WriteLine();
            Console.WriteLine("ZIP archive detected, processing entries...");

            ProcessZipFile(fileData);
            return;
        }

        if (fileData[0] == 'M' && fileData[1] == 'Z')
        {
            PEViewer.ProcessPEFile(fileData);
            return;
        }

        if (fileData[0] == 127 && fileData[1] == 69 && fileData[2] == 76 && fileData[3] == 70)
        {
            ELFViewer.ProcessELFFile(fileData);
            return;
        }

        throw new InvalidDataException("Not a valid PE file or ELF file");
    }

    internal static byte[] DecompressData(byte[] fileData)
    {
        for (; ; )
        {
            if (fileData.Length < 256)
            {
                throw new InvalidDataException("File too small to be a valid PE file or ELF file");
            }

            if (fileData[0] == 0x1f && fileData[1] == 0x8b)
            {
                Console.WriteLine();
                Console.WriteLine("GZip compressed file detected, decompressing...");

                using var decompr = new GZipStream(new MemoryStream(fileData), CompressionMode.Decompress);
                fileData = decompr.ReadToEnd();

                continue;
            }

            if (fileData[0] == 0x78 && fileData[1] == 0x9c)
            {
                Console.WriteLine();
                Console.WriteLine("ZLib compressed file detected, decompressing...");

                using var decompr = new ZLibStream(new MemoryStream(fileData), CompressionMode.Decompress, leaveOpen: false);
                fileData = decompr.ReadToEnd();

                continue;
            }

            if (fileData[0] == 0x28 && fileData[1] == 0xb5 && fileData[2] == 0x2f && fileData[3] == 0xfd)
            {
                Console.WriteLine();
                Console.WriteLine("Zstandard compressed file detected, decompressing...");

                using var decompr = new ZstdSharp.Decompressor();
                fileData = decompr.Unwrap(fileData).ToArray();

                continue;
            }

            if (fileData[0] == 0x04 && fileData[1] == 0x22 && fileData[2] == 0x4d && fileData[3] == 0x18)
            {
                Console.WriteLine();
                Console.WriteLine("LZ4 compressed file detected, decompressing...");

                using var decompr = LZ4Stream.Decode(new MemoryStream(fileData), leaveOpen: false);
                fileData = decompr.ReadToEnd();

                continue;
            }

            if (fileData[0] == 'B' && fileData[1] == 'Z' && fileData[2] == 'h')
            {
                Console.WriteLine();
                Console.WriteLine("BZip2 compressed file detected, decompressing...");

                using var decompr = new BZip2DecoderStream(new MemoryStream(fileData), Ownership.Dispose);
                fileData = decompr.ReadToEnd();

                continue;
            }

            break;
        }

        return fileData;
    }

    private static void ProcessZipFile(byte[] fileData)
    {
        using var zip = new ZipArchive(new MemoryStream(fileData), ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in zip.Entries)
        {
            if (entry.Length == 0)
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine(entry.FullName);

            using var entryStream = entry.Open();

            var entryData = entryStream.ReadExactly((int)entry.Length);

            ProcessFile(entryData);
        }
    }

    private static Dictionary<int, string>? _indentCache;

    internal static string GetIndent(int indent)
    {
        _indentCache ??= [];

        if (!_indentCache.TryGetValue(indent, out var indentStr))
        {
            indentStr = new string(' ', indent);
            _indentCache[indent] = indentStr;
        }

        return indentStr;
    }
}
