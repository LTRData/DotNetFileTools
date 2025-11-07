using Aspose.Zip;
using Aspose.Zip.Cab;
using Aspose.Zip.SevenZip;
using Aspose.Zip.Xz;
using DiscUtils;
using DiscUtils.Archives;
using DiscUtils.Compression;
using DiscUtils.Streams;
using DiscUtils.Wim;
using K4os.Compression.LZ4.Streams;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using LTRData.Extensions.Native;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

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
        string? apiSetFile = null;
        var searchOption = SearchOption.TopDirectoryOnly;
        var options = Options.None;

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
            else if (cmd.Key is "q" or "nohdr"
                && cmd.Value.Length == 0)
            {
                options |= Options.SuppressHeaders;
            }
            else if (cmd.Key is "d" or "dep" or "dependents"
                && cmd.Value.Length == 0)
            {
                options |= Options.ShowDependencies;
            }
            else if (cmd.Key is "t" or "tree"
                && cmd.Value.Length == 0)
            {
                options |= Options.ShowDependencyTree;
            }
            else if (cmd.Key is "z" or "delayed"
                && cmd.Value.Length == 0)
            {
                options |= Options.IncludeDelayedImports;
            }
            else if (cmd.Key is "i" or "imports"
                && cmd.Value.Length == 0)
            {
                options |= Options.ShowImports;
            }
            else if (cmd.Key is "x" or "exports"
                && cmd.Value.Length == 0)
            {
                options |= Options.ShowExports;
            }
            else if (cmd.Key == "apiset"
                && cmd.Value.Length == 1)
            {
                apiSetFile = cmd.Value[0];
            }
            else if (cmd.Key == "apiset"
                && cmd.Value.Length == 0)
            {
                ApiSetResolver.Default = ApiSetResolver.Empty;
            }
            else if (cmd.Key is "r" or "recurse"
                && cmd.Value.Length == 0)
            {
                searchOption = SearchOption.AllDirectories;
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
peinfo [options] filepath1 [filepath2 ...]
peinfo --image=imagefile [--part=partno] [options] filepath1 [filepath2 ...]
peinfo --wim=imagefile --index=wimindex [options] filepath1 [filepath2 ...]

Use - as path to specify standard input.

Image files:
    --image=imagefile       Path to disk image file containing the files to analyze.
    --part=partno           Partition number in the disk image to use (1-based).

    --wim=imagefile         Path to WIM image file containing the files to analyze.
    --index=wimindex        WIM image index number to use (1-based).

Options:
    --recurse               Recurse into subdirectories.
    -r

    --nohdr                 Do not display header information for ELF or PE files.
    -q

    --dependents            Show direct module dependencies for specified files.
    --dep
    -d

    --tree                  Show full module dependency tree for specified files.
    -t

    --delayed               Include delay-loaded DLLs.
    -z

    --imports               Show imported symbols.
    -i

    --exports               Show exported symbols.
    -x

    --apiset=path           Specify path to apisetschema.dll used to resolve API sets
                            to DLL names. If an image file is specified, this file
                            is read from detected file system in that image file.

                            By default, if --apiset is not specified, default mappings
                            are used from in-memory OS provided set on Windows, or
                            first found apisetschema.dll in PATH on other platforms.
                            To disable API set translations, specify --apiset without
                            a file path.");

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
            ? (new VolumeManager(vdisk).GetLogicalVolumes()?.ElementAtOrDefault(partNo - 1)?.Open()
                ?? throw new DriveNotFoundException($"Partition {partNo} not found"))
            : vdisk.Content
            : null;

        using var fs = part is not null
            ? (FileSystemManager.DetectFileSystems(part).FirstOrDefault()?.Open(part)
                ?? throw new NotSupportedException($"No supported file systems detected in partition {partNo}"))
            : wim is not null
            ? (new WimFile(wim).TryGetImage(wimIndex - 1, out var wimfs) ? wimfs
                : throw new DriveNotFoundException($"Index {wimIndex} not found in WIM file"))
            : null;

        Func<string, bool> fileExistsFunc;
        Func<string, Stream> openFileFunc;
        Func<string, byte[]> readAllBytesFunc;

        if (files is null || files.Length == 0)
        {
            files = [""];
        }

        if (fs is not null)
        {
            fileExistsFunc = fs.FileExists;
            openFileFunc = path => fs.OpenFile(path, FileMode.Open, FileAccess.Read);
            readAllBytesFunc = fs.ReadAllBytes;

            files = [.. files.SelectMany(f
                => searchOption == SearchOption.TopDirectoryOnly && f.IndexOfAny('*', '?') < 0
                ? [f]
                : fs.GetFiles(Path.GetDirectoryName(f) is { Length: > 0 } dir ? dir : "", Path.GetFileName(f), searchOption))];
        }
        else
        {
            fileExistsFunc = File.Exists;
            openFileFunc = path => path is "-" or "" ? Console.OpenStandardInput() : File.OpenRead(path);
            readAllBytesFunc = File.ReadAllBytes;

            files = [.. files.SelectMany(f
                => searchOption == SearchOption.TopDirectoryOnly && f.IndexOfAny('*', '?') < 0
                ? [f]
                : Directory.EnumerateFiles(Path.GetDirectoryName(f) is { Length: > 0 } dir ? dir : ".", Path.GetFileName(f), searchOption))];
        }

        if (apiSetFile is not null)
        {
            var apiset = ApiSetResolver.GetApiSetTranslations(openFileFunc(apiSetFile), PEStreamOptions.Default);

            if (!apiset.HasTranslations)
            {
                Console.WriteLine($"No supported API set translations found in '{apiSetFile}'");
            }

            ApiSetResolver.Default = apiset;
        }

        if (files.Length == 0)
        {
            Console.WriteLine("File not found.");

            return new FileNotFoundException().HResult;
        }

        foreach (var path in files)
        {
            try
            {
                using var file = openFileFunc(path);

                Console.WriteLine();
                Console.WriteLine(path);

                ProcessFile(file,
                            path,
                            fileExistsFunc,
                            readAllBytesFunc,
                            options);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.JoinMessages());
                Console.ResetColor();
                errCode = ex.HResult;
            }
        }

        return errCode;
    }

    public static void ProcessFile(Stream file,
                                   string filePath,
                                   Func<string, bool> fileExistsFunc,
                                   Func<string, byte[]> readAllBytesFunc,
                                   Options options)
    {
        if (file is FileStream { Name: { Length: > 0 } fileName })
        {
            filePath = fileName;
        }

        ProcessFile(file.ReadToEnd(),
                    filePath,
                    fileExistsFunc,
                    readAllBytesFunc,
                    options);
    }

    public static void ProcessFile(byte[] fileData,
                                   string filePath,
                                   Func<string, bool> fileExistsFunc,
                                   Func<string, byte[]> readAllBytesFunc,
                                   Options options)
    {
        fileData = DecompressData(fileData);

        if (fileData.Length < 512)
        {
            Console.WriteLine();
            Console.WriteLine("File too small to be a valid PE file or ELF file.");
        }
        else if (fileData[0] == 'P' && fileData[1] == 'K' && fileData[2] == 0x03 && fileData[3] == 0x04)
        {
            Console.WriteLine();
            Console.WriteLine("ZIP archive detected, processing entries...");

            ProcessZipFile(fileData, filePath, fileExistsFunc, readAllBytesFunc, options);
            return;
        }
        else if (fileData[0] == 0x37 && fileData[1] == 0x7a && fileData[2] == 0xbc && fileData[3] == 0xaf && fileData[4] == 0x27 && fileData[5] == 0x1c && fileData[6] == 0x00)
        {
            Console.WriteLine();
            Console.WriteLine("7zip archive detected, processing entries...");

            ProcessArchive(new SevenZipArchive(new MemoryStream(fileData)), filePath, fileExistsFunc, readAllBytesFunc, options);
            return;
        }
        else if (fileData[0x100] == 0 && fileData.AsSpan(0x101, 5).SequenceEqual("ustar"u8))
        {
            Console.WriteLine();
            Console.WriteLine("TAR archive detected, processing entries...");

            ProcessTarFile(fileData, filePath, fileExistsFunc, readAllBytesFunc, options);
            return;
        }
        else if (fileData.AsSpan(0, 4).SequenceEqual("MSCF"u8) && fileData.AsSpan(4, 4).IsBufferZero())
        {
            Console.WriteLine();
            Console.WriteLine("CAB archive detected, processing entries...");

            ProcessArchive(new CabArchive(new MemoryStream(fileData)), filePath, fileExistsFunc, readAllBytesFunc, options);
            return;
        }
        else if (fileData.AsSpan(0, 2).SequenceEqual("MZ"u8))
        {
            PEViewer.ProcessPEFile(fileData, filePath, fileExistsFunc, readAllBytesFunc, options);
            return;
        }
        else if (fileData[0] == 127 && fileData.AsSpan(1, 3).SequenceEqual("ELF"u8))
        {
            ELFViewer.ProcessELFFile(fileData, options);
            return;
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Not a valid PE or ELF file.");
        }

        if (!options.HasFlag(Options.SuppressHeaders))
        {
            Console.WriteLine("Initial file data:");
            HexExtensions.WriteHex(Console.Out, fileData.Take(512));
        }
    }

    internal static byte[] DecompressData(byte[] fileData)
    {
        for (; ; )
        {
            if (fileData.Length < 8)
            {
                return fileData;
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

            if (fileData.AsSpan(0, 3).SequenceEqual("BZh"u8))
            {
                Console.WriteLine();
                Console.WriteLine("BZip2 compressed file detected, decompressing...");

                using var decompr = new BZip2DecoderStream(new MemoryStream(fileData), Ownership.Dispose);
                fileData = decompr.ReadToEnd();

                continue;
            }

            if (fileData[0] == 0xfd && fileData.AsSpan(1, 4).SequenceEqual("7zXZ"u8))
            {
                Console.WriteLine();
                Console.WriteLine("XZ compressed file detected, decompressing...");

                using var xz = new XzArchive(new MemoryStream(fileData));
                var buffer = new byte[((IArchive)xz).FileEntries.First().Length!.Value];
                using var decompr = new MemoryStream(buffer);
                xz.Extract(decompr);
                
                continue;
            }

            if (fileData.AsSpan(0, 4).SequenceEqual("KWAJ"u8) && fileData[4] == 0x88 && fileData[5] == 0xf0 && fileData[6] == 0x27 && fileData[7] == 0xd1)
            {
                Console.WriteLine();
                Console.WriteLine("KWAJ compressed detected, decompressing...");

                fileData = DecompressKwaj(fileData);
            }

            if (fileData.AsSpan(0, 4).SequenceEqual("SZDD"u8) && fileData[4] == 0x88 && fileData[5] == 0xf0 && fileData[6] == 0x27 && fileData[7] == 0x33)
            {
                Console.WriteLine();
                Console.WriteLine("SZDD compressed detected, decompressing...");

                fileData = DecompressSzdd(fileData);
            }

            break;
        }

        return fileData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct KwajHeader
    {
        public enum CompressionMethod : ushort
        {
            None = 0x00,
            Not = 0x01,
            Lzss = 0x02,
            Lzh = 0x03,
            MsZip = 0x04
        }

        public readonly ulong Magic;
        public readonly CompressionMethod CompressionMode;
        public readonly ushort DataOffset;
        public readonly ushort ExtensionFlags;
        public readonly ushort Length;
    }

    public static byte[] DecompressKwaj(byte[] fileData)
    {
        ref readonly var header = ref fileData.CastRef<KwajHeader>();

        if ((header.ExtensionFlags & 0x1) == 0)
        {
            throw new NotSupportedException($"KWAJ variant not supported");
        }

        switch (header.CompressionMode)
        {
            case KwajHeader.CompressionMethod.None:
                return fileData.AsSpan(header.DataOffset, header.Length).ToArray();

            case KwajHeader.CompressionMethod.Not:
                var buffer = fileData.AsSpan(header.DataOffset, header.Length).ToArray();

                for (var i = 0; i < buffer.Length; i++)
                {
                    buffer[i] ^= 0xff;
                }

                return buffer;

            case KwajHeader.CompressionMethod.MsZip:
                var offset = (int)header.DataOffset;

                var result = new byte[header.Length];

                var outStream = new MemoryStream(result);

                while (offset + 4 < fileData.Length)
                {
                    var blockLength = MemoryMarshal.Read<ushort>(fileData.AsSpan(offset));
                    using var deflate = new DeflateStream(new MemoryStream(fileData, offset + 4, blockLength - 2), CompressionMode.Decompress);
                    deflate.CopyTo(outStream);

                    offset += 2 + blockLength;
                }

                return result;

            default:
                throw new NotSupportedException($"Compression mode '{header.CompressionMode}' not supported");
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct SzddHeader
    {
        public readonly ulong Magic;
        public readonly byte CompressionMode;
        public readonly byte MissingChar;
        public readonly ushort Length;
    }

    public static byte[] DecompressSzdd(byte[] fileData)
    {
        ref readonly var header = ref fileData.CastRef<SzddHeader>();

        var result = new byte[header.Length];

        Span<byte> window = stackalloc byte[4096];

        window.Clear();

        var pos = window.Length - Unsafe.SizeOf<SzddHeader>();
        var i = Unsafe.SizeOf<SzddHeader>();
        var o = 0;

        for (; i < fileData.Length; )
        {
            int control = fileData[i++];
            
            for (int cbit = 0x01; (cbit & 0xFF) != 0; cbit <<= 1)
            {
                if ((control & cbit) != 0)
                {
                    /* literal */
                    result[o++] = (window[pos++] = fileData[i++]);
                }
                else
                {
                    /* match */
                    int matchpos = fileData[i++];
                    int matchlen = fileData[i++];
                    matchpos |= (matchlen & 0xF0) << 4;
                    matchlen = (matchlen & 0x0F) + 3;

                    while (matchlen-- > 0)
                    {
                        result[o++] = (window[pos++] = window[matchpos++]);
                        pos &= 4095;
                        matchpos &= 4095;
                    }
                }
            }
        }

        return result;
    }

    private static void ProcessZipFile(byte[] fileData,
                                       string filePath,
                                       Func<string, bool> fileExistsFunc,
                                       Func<string, byte[]> readAllBytesFunc,
                                       Options options)
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

            try
            {
                using var entryStream = entry.Open();

                var entryData = entryStream.ReadExactly((int)entry.Length);

                ProcessFile(entryData,
                            filePath,
                            fileExistsFunc,
                            readAllBytesFunc,
                            options);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.JoinMessages());
                Console.ResetColor();
            }
        }
    }

    private static void ProcessTarFile(byte[] fileData,
                                       string filePath,
                                       Func<string, bool> fileExistsFunc,
                                       Func<string, byte[]> readAllBytesFunc,
                                       Options options)
    {
        foreach (var entry in TarFile.EnumerateFiles(new MemoryStream(fileData)))
        {
            if (entry.Length == 0)
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine(entry.Name);

            try
            {
                using var entryStream = entry.GetStream();

                if (entryStream is null)
                {
                    continue;
                }

                var entryData = entryStream.ReadToEnd();

                ProcessFile(entryData,
                            filePath,
                            fileExistsFunc,
                            readAllBytesFunc,
                            options);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.JoinMessages());
                Console.ResetColor();
            }
        }
    }

    private static void ProcessArchive(IArchive archive,
                                       string filePath,
                                       Func<string, bool> fileExistsFunc,
                                       Func<string, byte[]> readAllBytesFunc,
                                       Options options)
    {
        foreach (var entry in archive.FileEntries)
        {
            Console.WriteLine();
            Console.WriteLine(entry.Name);

            try
            {
                using var entryStream = entry.Length is { } length ? new MemoryStream((int)length) : new MemoryStream();
                entry.Extract(entryStream);

                if (entryStream.Length == 0)
                {
                    continue;
                }

                byte[] entryData;

                if (entryStream.TryGetBuffer(out var buffer) && buffer.Array!.Length == entryStream.Length)
                {
                    entryData = buffer.Array!;
                }
                else
                {
                    entryStream.Position = 0;
                    entryData = entryStream.ReadToEnd();
                }

                ProcessFile(entryData,
                            filePath,
                            fileExistsFunc,
                            readAllBytesFunc,
                            options);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.JoinMessages());
                Console.ResetColor();
            }
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

[Flags]
public enum Options
{
    None = 0x0000,
    ShowDependencies = 0x0001,
    ShowDependencyTree = 0x0002,
    ShowImports = 0x0004,
    IncludeDelayedImports = 0x0008,
    ShowExports = 0x0010,
    SuppressHeaders = 0x0020,
}
