using Arsenal.ImageMounter.Extensions;
using Arsenal.ImageMounter.IO.Native;
using DiscUtils;
using DiscUtils.Complete;
using DiscUtils.SquashFs;
using DiscUtils.Streams;
using DiscUtils.Wim;
using K4os.Compression.LZ4.Encoders;
using K4os.Compression.LZ4.Streams;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

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

        var cmds = CommandLineParser.ParseCommandLine(args, StringComparer.Ordinal);

        foreach (var cmd in cmds)
        {
            if (cmd.Key == "image"
                && cmd.Value.Length == 1)
            {
                SetupHelper.SetupComplete();
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
peinfo filepath1 [filepath2 ...]
peinfo --image=imagefile [--part=partno] filepath1 [filepath2 ...]
peinfo --wim=imagefile --index=wimindex filepath1 [filepath2 ...]

Use --dep switch for a DLL dependency tree.");

                return -1;
            }
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
                    ProcessDependencyTree(file);
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

    public static unsafe void ProcessFile(Stream file)
        => ProcessFile(file.ReadToEnd());

    private static unsafe byte[] ReadToEnd(this Stream file)
    {
        if (file.CanSeek)
        {
            return file.ReadExactly((int)(file.Length - file.Position));
        }
        
        using var ms = new MemoryStream();
        file.CopyTo(ms);
        return ms.ToArray();
    }

    private static unsafe void ProcessFile(byte[] fileData)
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
        }

        if (fileData[0] == 0x78 && fileData[1] == 0x9c)
        {
            Console.WriteLine();
            Console.WriteLine("ZLib compressed file detected, decompressing...");

            using var decompr = new ZLibStream(new MemoryStream(fileData), CompressionMode.Decompress, leaveOpen: false);
            fileData = decompr.ReadToEnd();
        }

        if (fileData[0] == 0x28 && fileData[1] == 0xb5 && fileData[2] == 0x2f && fileData[3] == 0xfd)
        {
            Console.WriteLine();
            Console.WriteLine("Zstandard compressed file detected, decompressing...");

            using var decompr = new ZstdSharp.Decompressor();
            fileData = decompr.Unwrap(fileData).ToArray();
        }

        if (fileData[0] == 0x04 && fileData[1] == 0x22 && fileData[2] == 0x4d && fileData[3] == 0x18)
        {
            Console.WriteLine();
            Console.WriteLine("LZ4 compressed file detected, decompressing...");

            using var decompr = LZ4Stream.Decode(new MemoryStream(fileData), leaveOpen: false);
            fileData = decompr.ReadToEnd();
        }

        if (fileData[0] == 'P' && fileData[1] == 'K')
        {
            Console.WriteLine();
            Console.WriteLine("ZIP archive detected, processing entries...");

            ProcessZipFile(fileData);
            return;
        }

        if (fileData[0] == 'M' && fileData[1] == 'Z')
        {
            ProcessPEFile(fileData);
            return;
        }

        if (fileData[0] == 127 && fileData[1] == 69 && fileData[2] == 76 && fileData[3] == 70)
        {
            ProcessELFFile(fileData);
            return;
        }

        throw new InvalidDataException("Not a valid PE file or ELF file");
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

    private static unsafe void ProcessELFFile(ReadOnlySpan<byte> fileData)
    {
        var elf = MemoryMarshal.Read<ElfHeader>(fileData);

        Console.WriteLine();
        Console.WriteLine("ELF header:");
        Console.WriteLine($"{"Machine",-35}{elf.Machine}");
        Console.WriteLine($"{"Type",-35}{elf.Type}");
        Console.WriteLine($"{"Class",-35}{elf.cls}");
        Console.WriteLine($"{"Data encoding",-35}{elf.data}");
        Console.WriteLine($"{"Version",-35}{elf.version}");
        Console.WriteLine($"{"OS/ABI",-35}{elf.osabi}");
        Console.WriteLine($"{"ABI version",-35}{elf.abiversion}");

        ElfHeaderEnd? headerEnd = null;

        if (elf.cls == ElfClass.ELFCLASS32)
        {
            var elf32 = MemoryMarshal.Read<ElfHeader32>(fileData);

            Console.WriteLine();
            Console.WriteLine($"{"Entry point",-35}0x{elf32.EntryPoint:x8}");
            Console.WriteLine($"{"Program header offset",-35}0x{elf32.ProgramHeaderOffset:x8}");
            Console.WriteLine($"{"Section header offset",-35}0x{elf32.SectionHeaderOffset:x8}");

            headerEnd = elf32.End;
        }
        else if (elf.cls == ElfClass.ELFCLASS64)
        {
            var elf64 = MemoryMarshal.Read<ElfHeader64>(fileData);

            Console.WriteLine();
            Console.WriteLine($"{"Entry point",-35}0x{elf64.EntryPoint:x16}");
            Console.WriteLine($"{"Program header offset",-35}0x{elf64.ProgramHeaderOffset:x16}");
            Console.WriteLine($"{"Section header offset",-35}0x{elf64.SectionHeaderOffset:x16}");

            headerEnd = elf64.End;
        }

        if (headerEnd is { } end)
        {
            Console.WriteLine();
            Console.WriteLine($"{"Flags",-35}0x{end.Flags:x8}");
            Console.WriteLine($"{"ELF header size",-35}{end.HeaderSize:N0} bytes");
            Console.WriteLine($"{"Program header entry size",-35}{end.ProgramHeaderEntrySize:N0} bytes");
            Console.WriteLine($"{"Number of program header entries",-35}{end.ProgramHeaderEntryCount:N0}");
            Console.WriteLine($"{"Section header entry size",-35}{end.SectionHeaderEntrySize:N0} bytes");
            Console.WriteLine($"{"Number of section header entries",-35}{end.SectionHeaderEntryCount:N0}");
            Console.WriteLine($"{"Section header string table index",-35}{end.SectionHeaderStringTableIndex}");
        }
    }

    public static void ProcessPEFile(byte[] fileData)
    {
        using var reader = new PEReader([.. fileData]);

        var coffHeader = reader.PEHeaders.CoffHeader;

        Console.WriteLine();
        Console.WriteLine("MZ header:");
        Console.WriteLine($"{"Machine",-24}{coffHeader.Machine}");
        Console.WriteLine($"{"Characteristics",-24}{coffHeader.Characteristics}");

        if (reader.PEHeaders.IsCoffOnly)
        {
            Console.WriteLine($"{"Type",-24}No executable sections");
        }
        else if (reader.PEHeaders.IsDll)
        {
            Console.WriteLine($"{"Type",-24}DLL");
        }
        else if (reader.PEHeaders.IsConsoleApplication)
        {
            Console.WriteLine($"{"Type",-24}Console application");
        }
        else if (reader.PEHeaders.IsExe)
        {
            Console.WriteLine($"{"Type",-24}Executable");
        }

        try
        {
            if (NativeFileVersion.TryGetVersion(fileData, out var fileVersion))
            {
                Console.WriteLine();
                Console.WriteLine("Version resource:");

                Console.WriteLine($"{"File version",-24}{fileVersion.FileVersion}");
                Console.WriteLine($"{"Product version",-24}{fileVersion.ProductVersion}");

                if (fileVersion.FileDate is { } fileDate)
                {
                    Console.WriteLine($"{"File date",-24}{fileDate}");
                }

                foreach (var item in fileVersion.Fields)
                {
                    Console.WriteLine($"{item.Key,-24}{item.Value}");
                }
            }
        }
        catch
        {
        }

        if (reader.PEHeaders.CorHeader is { } corHeader)
        {
            Console.WriteLine();
            Console.WriteLine("COR header:");

            Console.WriteLine($"{"Flags",-24}{corHeader.Flags}");
            Console.WriteLine($"{"Runtime version",-24}{corHeader.MajorRuntimeVersion}.{corHeader.MinorRuntimeVersion}");
        }

        if (reader.PEHeaders.PEHeader is { } peHeader)
        {
            Console.WriteLine();
            Console.WriteLine("PE optional header:");
            Console.WriteLine($"{"Subsystem",-24}{peHeader.Subsystem}");
            Console.WriteLine($"{"Entry point",-24}0x{peHeader.AddressOfEntryPoint:x8}");
            Console.WriteLine($"{"Image base",-24}0x{peHeader.ImageBase:x16}");
            Console.WriteLine($"{"Size of image",-24}{peHeader.SizeOfImage:N0} bytes");
            Console.WriteLine($"{"Size of headers",-24}{peHeader.SizeOfHeaders:N0} bytes");
            Console.WriteLine($"{"Base of code",-24}0x{peHeader.BaseOfCode:x8}");
            Console.WriteLine($"{"Base of data",-24}0x{peHeader.BaseOfData:x8}");
            Console.WriteLine($"{"Characteristics",-24}{peHeader.DllCharacteristics}");
            Console.WriteLine($"{"Linker version",-24}{peHeader.MajorLinkerVersion}.{peHeader.MinorLinkerVersion}");
            Console.WriteLine($"{"OS version",-24}{peHeader.MajorOperatingSystemVersion}.{peHeader.MinorOperatingSystemVersion}");
            Console.WriteLine($"{"Subsystem version",-24}{peHeader.MajorSubsystemVersion}.{peHeader.MinorSubsystemVersion}");

            var securitySectionLocation = peHeader.CertificateTableDirectory;

            if (securitySectionLocation.Size > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Authenticode signature:");

                var securitySection = fileData.AsSpan(securitySectionLocation.RelativeVirtualAddress, securitySectionLocation.Size);

                var header = MemoryMarshal.Read<NativePE.WinCertificateHeader>(securitySection);

                if (header.Revision == 0x200 && header.CertificateType == NativePE.CertificateType.PkcsSignedData)
                {
                    var blob = NativePE.GetCertificateBlob(securitySection);

                    var signed = new SignedCms();

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
                    signed.Decode(blob);
#else
                    signed.Decode(blob.ToArray());
#endif

                    for (; ; )
                    {
                        var signerInfo = signed.SignerInfos[0];

                        if (signerInfo.Certificate is not { } cert)
                        {
                            break;
                        }

                        var certSubjectName = cert.Subject;

                        Console.WriteLine($"{"Signed by",-24}{certSubjectName}");

                        if (NativePE.GetRawFileAuthenticodeHash(SHA256.Create, fileData, fileData.Length).AsSpan().SequenceEqual(signed.ContentInfo.Content.AsSpan(signed.ContentInfo.Content.Length - 32))
                            || NativePE.GetRawFileAuthenticodeHash(SHA1.Create, fileData, fileData.Length).AsSpan().SequenceEqual(signed.ContentInfo.Content.AsSpan(signed.ContentInfo.Content.Length - 20))
                            || NativePE.GetRawFileAuthenticodeHash(MD5.Create, fileData, fileData.Length).AsSpan().SequenceEqual(signed.ContentInfo.Content.AsSpan(signed.ContentInfo.Content.Length - 16)))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"File signature is valid.");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"File signature is not valid. File contents modified after signing.");
                            Console.ResetColor();
                        }

                        try
                        {
                            signed.CheckSignature(verifySignatureOnly: true);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"Certificate signature valid.");
                            Console.ResetColor();
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Certificate signature error: {ex.JoinMessages()}");
                            Console.ResetColor();
                        }

                        using var chain = new X509Chain();
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid | X509VerificationFlags.IgnoreCtlNotTimeValid | X509VerificationFlags.IgnoreNotTimeNested;

                        if (chain.Build(cert))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"Certificate is valid.");
                            Console.ResetColor();
                        }
                        else
                        {
                            foreach (var certChainStatus in chain.ChainStatus)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Certificate validation error: {certChainStatus.Status}: {certChainStatus.StatusInformation}");
                                Console.ResetColor();
                            }
                        }

                        if (signerInfo.UnsignedAttributes
                            .OfType<CryptographicAttributeObject>()
                            .Where(o => o.Oid.Value!.StartsWith("1.3.6.1.4.1.311.2.4.", StringComparison.Ordinal))
                            .SelectMany(o => o.Values.OfType<AsnEncodedData>())
                            .Select(o => o.RawData)
                            .FirstOrDefault() is not { } subData)
                        {
                            break;
                        }

                        signed.Decode(subData);
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Unsupported authenticode signature");
                    Console.ResetColor();
                }
            }

            var importSection = peHeader.ImportTableDirectory;

            if (importSection.Size > 0
                && reader.PEHeaders.TryGetDirectoryOffset(importSection, out var importSectionAddress))
            {
                Console.WriteLine();
                Console.WriteLine("Imported DLLs:");

                var descriptors = MemoryMarshal.Cast<byte, ImageImportDescriptor>(fileData.AsSpan(importSectionAddress, importSection.Size));

                ProcessImportTable(reader, descriptors);
            }

            var delayImportSection = peHeader.DelayImportTableDirectory;

            if (delayImportSection.Size > 0
                && reader.PEHeaders.TryGetDirectoryOffset(delayImportSection, out var delayImportSectionAddress))
            {
                Console.WriteLine();
                Console.WriteLine("Delay Imported DLLs:");

                var descriptors = MemoryMarshal.Cast<byte, ImageDelayImportDescriptor>(fileData.AsSpan(delayImportSectionAddress, delayImportSection.Size));

                ProcessDelayImportTable(reader, descriptors);
            }

            var exportSection = peHeader.ExportTableDirectory;

            if (exportSection.Size > 0
                && reader.PEHeaders.TryGetDirectoryOffset(exportSection, out var exportSectionAddress))
            {
                Console.WriteLine();
                Console.WriteLine("Exported functions:");

                var exportDir = MemoryMarshal.Read<ImageExportDirectory>(fileData.AsSpan(exportSectionAddress, exportSection.Size));

                var moduleName = reader.GetSectionData((int)exportDir.Name).AsSpan().ReadNullTerminatedAsciiString();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  ");
                Console.WriteLine(moduleName);
                Console.ResetColor();

                if (exportDir.NumberOfNames != 0)
                {
                    var namePointers = MemoryMarshal.Cast<byte, uint>(reader.GetSectionData(exportDir.AddressOfNames).AsSpan()).Slice(0, exportDir.NumberOfNames);
                    var ordinalPointers = MemoryMarshal.Cast<byte, ushort>(reader.GetSectionData(exportDir.AddressOfNameOrdinals).AsSpan()).Slice(0, exportDir.NumberOfNames);
                    var functionPointers = MemoryMarshal.Cast<byte, uint>(reader.GetSectionData(exportDir.AddressOfFunctions).AsSpan()).Slice(0, exportDir.NumberOfFunctions);

                    for (var i = 0; i < exportDir.NumberOfNames; i++)
                    {
                        var nameRVA = namePointers[i];
                        var name = reader.GetSectionData((int)nameRVA).AsSpan().ReadNullTerminatedAsciiString();
                        var ordinal = ordinalPointers[i];
                        var functionRVA = functionPointers[ordinal];

                        Console.WriteLine($"    (Ordinal: 0x{exportDir.Base + ordinal:X4}, RVA: 0x{functionRVA:X8})  {name}");
                    }
                }

                if (exportDir.NumberOfFunctions != 0)
                {
                    var functionPointers = MemoryMarshal.Cast<byte, uint>(reader.GetSectionData(exportDir.AddressOfFunctions).AsSpan()).Slice(0, exportDir.NumberOfFunctions);

                    for (var i = 0; i < exportDir.NumberOfFunctions; i++)
                    {
                        var functionRVA = functionPointers[i];

                        Console.WriteLine($"    (Ordinal: 0x{exportDir.Base + i:X4}, RVA: 0x{functionRVA:X8})");
                    }
                }
            }
        }
    }

    private static void ProcessImportTable(PEReader reader, ReadOnlySpan<ImageImportDescriptor> descriptors)
    {
        foreach (var descr in descriptors)
        {
            if (descr.OriginalFirstThunk == 0)
            {
                break;
            }

            if (descr.DllNameRVA is 0 or 0xffff)
            {
                continue;
            }

            var dllNameAddress = reader.GetSectionData(descr.DllNameRVA).AsSpan();

            if (dllNameAddress.IsEmpty)
            {
                continue;
            }

            var moduleName = dllNameAddress.ReadNullTerminatedAsciiString();

            if (string.IsNullOrWhiteSpace(moduleName))
            {
                continue;
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("  ");
            Console.WriteLine(moduleName);
            Console.ResetColor();

            var thunksData = reader.GetSectionData((int)descr.OriginalFirstThunk).AsSpan();

            if (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32)
            {
                var thunks = MemoryMarshal.Cast<byte, uint>(thunksData);

                foreach (var func in thunks)
                {
                    if (func == 0)
                    {
                        break;
                    }

                    if ((func & IMAGE_ORDINAL_FLAG32) != 0)
                    {
                        Console.WriteLine($"    Ordinal: 0x{func & ~IMAGE_ORDINAL_FLAG32:X}");
                    }
                    else
                    {
                        var data = reader.GetSectionData((int)func).AsSpan();
                        WriteHintAndName(data);
                    }
                }
            }
            else if (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus)
            {
                var thunks = MemoryMarshal.Cast<byte, ulong>(thunksData);

                foreach (var func in thunks)
                {
                    if (func == 0)
                    {
                        break;
                    }

                    if ((func & IMAGE_ORDINAL_FLAG64) != 0)
                    {
                        Console.WriteLine($"    Ordinal: 0x{func & ~IMAGE_ORDINAL_FLAG64:X}");
                    }
                    else
                    {
                        var data = reader.GetSectionData((int)func).AsSpan();
                        WriteHintAndName(data);
                    }
                }
            }
        }
    }

    private static void ProcessDelayImportTable(PEReader reader, ReadOnlySpan<ImageDelayImportDescriptor> descriptors)
    {
        foreach (var descr in descriptors)
        {
            if (descr.NameTable == 0)
            {
                break;
            }

            var addressBase = (descr.Attributes & 1) == 0
                ? (int)reader.PEHeaders.PEHeader!.ImageBase : 0;

            if (descr.DllName is 0 or 0xffff)
            {
                continue;
            }

            var dllNameRVA = descr.DllName - addressBase;

            var dllNameAddress = reader.GetSectionData(dllNameRVA).AsSpan();

            if (dllNameAddress.IsEmpty)
            {
                continue;
            }

            var moduleName = dllNameAddress.ReadNullTerminatedAsciiString();

            if (string.IsNullOrWhiteSpace(moduleName))
            {
                continue;
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("  ");
            Console.WriteLine(moduleName);
            Console.ResetColor();

            var thunksData = reader.GetSectionData(descr.NameTable - addressBase).AsSpan();

            if (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32)
            {
                var thunks = MemoryMarshal.Cast<byte, int>(thunksData);

                foreach (var func in thunks)
                {
                    if (func == 0)
                    {
                        break;
                    }

                    if ((func & IMAGE_ORDINAL_FLAG32) != 0)
                    {
                        Console.WriteLine($"    Ordinal: 0x{func & ~IMAGE_ORDINAL_FLAG32:X}");
                    }
                    else
                    {
                        var data = reader.GetSectionData(func - addressBase).AsSpan();
                        WriteHintAndName(data);
                    }
                }
            }
            else if (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus)
            {
                var thunks = MemoryMarshal.Cast<byte, ulong>(thunksData);

                foreach (var func in thunks)
                {
                    if (func == 0)
                    {
                        break;
                    }

                    if ((func & IMAGE_ORDINAL_FLAG64) != 0)
                    {
                        Console.WriteLine($"    Ordinal: 0x{func & ~IMAGE_ORDINAL_FLAG64:X}");
                    }
                    else
                    {
                        var data = reader.GetSectionData((int)(func - (ulong)addressBase)).AsSpan();
                        WriteHintAndName(data);
                    }
                }
            }
        }
    }

    private static void WriteHintAndName(ReadOnlySpan<byte> data)
    {
        var hint = MemoryMarshal.Read<ushort>(data);
        var name = BufferExtensions.ReadNullTerminatedAsciiString(data.Slice(2));

        if (hint == 0)
        {
            Console.WriteLine($"                    {name}");
        }
        else
        {
            Console.WriteLine($"    (Hint: 0x{hint:X4})  {name}");
        }
    }

    public static void ProcessDependencyTree(Stream file)
    {
        Console.WriteLine("Dependency Tree:");

        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];

        if (file is FileStream { Name: { } fileName }
            && Path.GetDirectoryName(fileName) is { } dir)
        {
            paths = [dir, .. paths];
        }

        var fileData = file.ReadToEnd();

        ProcessDependencyTree(fileData,
                              modules: new(StringComparer.OrdinalIgnoreCase),
                              paths: paths,
                              indent: 2);
    }

    private static Dictionary<int, string>? _indentCache;

    private static string GetIndent(int indent)
    {
        _indentCache ??= [];

        if (!_indentCache.TryGetValue(indent, out var indentStr))
        {
            indentStr = new string(' ', indent);
            _indentCache[indent] = indentStr;
        }

        return indentStr;
    }

    private static void ProcessDependencyTree(byte[] fileData,
                                              Dictionary<string, Exports> modules,
                                              string[] paths,
                                              int indent)
    {
        foreach (var (moduleName, functions, delayed) in EnumerateDependencies(fileData))
        {
            if (!modules.TryGetValue(moduleName, out var exports))
            {
                foreach (var path in paths)
                {
                    exports = TryPath(Path.Combine(path, moduleName), modules, paths, indent)
                        ?? TryPath(Path.Combine(path, "downloevel", moduleName), modules, paths, indent)
                        ?? TryPath(Path.Combine(path, "drivers", moduleName), modules, paths, indent)
                        ?? TryPath(Path.Combine(path, "lib", moduleName), modules, paths, indent);

                    if (exports is not null)
                    {
                        exports.Delayed = delayed;

                        break;
                    }
                }

                if (exports is null)
                {
                    exports = new(FullPath: null, Functions: default)
                    {
                        Delayed = delayed
                    };

                    modules[moduleName] = exports;

                    var chars = GetIndent(indent);

                    if (delayed)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    }

                    Console.WriteLine($"{chars}  Could not find {(delayed ? "delay-loaded " : null)}dependency '{moduleName}'");
                    Console.ResetColor();
                }
            }

            if (exports.FullPath is null)
            {
                if (exports.Delayed && !delayed)
                {
                    var chars = GetIndent(indent);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{chars}  Dependency '{moduleName}' was previously reported as missing delay-loaded, now required as normal import");
                    Console.ResetColor();

                    exports.Delayed = false;
                }

                continue;
            }

            foreach (var (Ordinal, Hint, Name) in functions)
            {
                var match = exports.Functions.FirstOrDefault(f => (f.Ordinal != 0 && f.Ordinal == Ordinal) || f.Name == Name);

                if (match.Name is null && match.Ordinal == 0)
                {
                    var chars = GetIndent(indent);

                    if (delayed)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    }

                    Console.WriteLine($"{chars}  Could not find {(delayed ? "delay-loaded " : null)}function '{Name ?? $"Ordinal: 0x{Ordinal:X}"}' in dependency '{exports.FullPath}'");
                    Console.ResetColor();
                }
            }
        }
    }

    private static Exports? TryPath(string tryPath,
                                    Dictionary<string, Exports> modules,
                                    string[] paths,
                                    int indent)
    {
        if (!File.Exists(tryPath))
        {
            return null;
        }

        var chars = GetIndent(indent);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{chars}{tryPath}");
        Console.ResetColor();

        try
        {
            using var file = File.OpenRead(tryPath);

            var fileData = file.ReadExactly((int)file.Length);

            var exports = GetExports(fileData);

            var exportsRecord = new Exports(tryPath, exports);

            modules[Path.GetFileName(tryPath)] = exportsRecord;

            ProcessDependencyTree(fileData, modules, paths, indent + 2);

            return exportsRecord;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{chars}  Could not read dependency '{tryPath}': {ex.JoinMessages()}");
            Console.ResetColor();
        }

        return null;
    }

    private static ImmutableArray<(ulong Ordinal, string? Name)> GetExports(byte[] fileData)
    {
        using var reader = new PEReader([.. fileData]);

        if (reader.PEHeaders.PEHeader is not { } peHeader)
        {
            throw new InvalidDataException("Not a valid PE file with executable sections");
        }

        var functions = ImmutableArray.CreateBuilder<(ulong Ordinal, string? Name)>();

        var exportSection = peHeader.ExportTableDirectory;

        if (exportSection.Size > 0
            && reader.PEHeaders.TryGetDirectoryOffset(exportSection, out var exportSectionAddress))
        {
            var exportDir = MemoryMarshal.Read<ImageExportDirectory>(fileData.AsSpan(exportSectionAddress, exportSection.Size));

            if (exportDir.NumberOfNames != 0)
            {
                var namePointers = MemoryMarshal.Cast<byte, uint>(reader.GetSectionData(exportDir.AddressOfNames).AsSpan()).Slice(0, exportDir.NumberOfNames);
                var ordinalPointers = MemoryMarshal.Cast<byte, ushort>(reader.GetSectionData(exportDir.AddressOfNameOrdinals).AsSpan()).Slice(0, exportDir.NumberOfNames);

                for (var i = 0; i < exportDir.NumberOfNames; i++)
                {
                    var nameRVA = namePointers[i];
                    var name = reader.GetSectionData((int)nameRVA).AsSpan().ReadNullTerminatedAsciiString();
                    var ordinal = ordinalPointers[i];

                    functions.Add((exportDir.Base + ordinal, name));
                }
            }

            if (exportDir.NumberOfFunctions != 0)
            {
                var functionPointers = MemoryMarshal.Cast<byte, uint>(reader.GetSectionData(exportDir.AddressOfFunctions).AsSpan()).Slice(0, exportDir.NumberOfFunctions);

                for (var i = 0; i < exportDir.NumberOfFunctions; i++)
                {
                    var functionRVA = functionPointers[i];

                    functions.Add((exportDir.Base + (ulong)i, null));
                }
            }
        }

        return functions.ToImmutable();
    }

    public static IEnumerable<(string Module, ImmutableArray<(ulong Ordinal, ushort Hint, string? Name)> Functions, bool Delayed)> EnumerateDependencies(byte[] fileData)
    {
        using var reader = new PEReader([.. fileData]);

        if (reader.PEHeaders.PEHeader is not { } peHeader)
        {
            throw new InvalidDataException("Not a valid PE file with executable sections");
        }

        var importSection = peHeader.ImportTableDirectory;

        if (importSection.Size != 0
            && reader.PEHeaders.TryGetDirectoryOffset(importSection, out var importSectionAddress))
        {
            var descriptors = MemoryMarshal.Cast<byte, ImageImportDescriptor>(fileData.AsSpan(importSectionAddress, importSection.Size)).ToImmutableArray();

            foreach (var descr in descriptors)
            {
                if (descr.OriginalFirstThunk == 0)
                {
                    break;
                }

                if (descr.DllNameRVA is 0 or 0xffff)
                {
                    continue;
                }

                var moduleName = reader.GetSectionData(descr.DllNameRVA).AsSpan().ReadNullTerminatedAsciiString();

                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    continue;
                }

                var functions = ImmutableArray.CreateBuilder<(ulong Ordinal, ushort Hint, string? Name)>();

                var thunksData = reader.GetSectionData((int)descr.OriginalFirstThunk).AsSpan();

                if (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32)
                {
                    var thunks = MemoryMarshal.Cast<byte, uint>(thunksData);

                    foreach (var func in thunks)
                    {
                        if (func == 0)
                        {
                            break;
                        }

                        if ((func & IMAGE_ORDINAL_FLAG32) != 0)
                        {
                            functions.Add((Ordinal: func & ~IMAGE_ORDINAL_FLAG32, Hint: 0, Name: null));
                        }
                        else
                        {
                            var data = reader.GetSectionData((int)func).AsSpan();
                            functions.Add((Ordinal: 0, Hint: MemoryMarshal.Read<ushort>(data), Name: data.Slice(2).ReadNullTerminatedAsciiString()));
                        }
                    }
                }
                else if (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus)
                {
                    var thunks = MemoryMarshal.Cast<byte, ulong>(thunksData);

                    foreach (var func in thunks)
                    {
                        if (func == 0)
                        {
                            break;
                        }

                        if ((func & IMAGE_ORDINAL_FLAG64) != 0)
                        {
                            functions.Add((Ordinal: func & ~IMAGE_ORDINAL_FLAG64, Hint: 0, Name: null));
                        }
                        else
                        {
                            var data = reader.GetSectionData((int)func).AsSpan();
                            functions.Add((Ordinal: 0, Hint: MemoryMarshal.Read<ushort>(data), Name: data.Slice(2).ReadNullTerminatedAsciiString()));
                        }
                    }
                }

                yield return (moduleName, functions.ToImmutable(), Delayed: false);
            }
        }

        var delayImportSection = peHeader.DelayImportTableDirectory;

        if (delayImportSection.Size != 0
            && reader.PEHeaders.TryGetDirectoryOffset(delayImportSection, out var delayImportSectionAddress))
        {
            var descriptors = MemoryMarshal.Cast<byte, ImageDelayImportDescriptor>(fileData.AsSpan(delayImportSectionAddress, delayImportSection.Size)).ToImmutableArray();

            foreach (var descr in descriptors)
            {
                if (descr.NameTable == 0)
                {
                    break;
                }

                if (descr.DllName is 0 or 0xffff)
                {
                    continue;
                }

                var addressBase = (descr.Attributes & 1) == 0
                    ? (int)reader.PEHeaders.PEHeader!.ImageBase : 0;

                var moduleName = reader.GetSectionData(descr.DllName - addressBase).AsSpan().ReadNullTerminatedAsciiString();

                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    continue;
                }

                var functions = ImmutableArray.CreateBuilder<(ulong Ordinal, ushort Hint, string? Name)>();

                var thunksData = reader.GetSectionData(descr.NameTable - addressBase).AsSpan();

                if (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32)
                {
                    var thunks = MemoryMarshal.Cast<byte, int>(thunksData);

                    foreach (var func in thunks)
                    {
                        if (func == 0)
                        {
                            break;
                        }

                        if ((func & IMAGE_ORDINAL_FLAG32) != 0)
                        {
                            functions.Add((Ordinal: (uint)(func & ~IMAGE_ORDINAL_FLAG32), Hint: 0, Name: null));
                        }
                        else
                        {
                            var data = reader.GetSectionData(func - addressBase).AsSpan();
                            functions.Add((Ordinal: 0, Hint: MemoryMarshal.Read<ushort>(data), Name: data.Slice(2).ReadNullTerminatedAsciiString()));
                        }
                    }
                }
                else if (reader.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus)
                {
                    var thunks = MemoryMarshal.Cast<byte, ulong>(thunksData);

                    foreach (var func in thunks)
                    {
                        if (func == 0)
                        {
                            break;
                        }

                        if ((func & IMAGE_ORDINAL_FLAG64) != 0)
                        {
                            functions.Add((Ordinal: func & ~IMAGE_ORDINAL_FLAG64, Hint: 0, Name: null));
                        }
                        else
                        {
                            var data = reader.GetSectionData((int)(func - (ulong)addressBase)).AsSpan();
                            functions.Add((Ordinal: 0, Hint: MemoryMarshal.Read<ushort>(data), Name: data.Slice(2).ReadNullTerminatedAsciiString()));
                        }
                    }
                }

                yield return (moduleName, functions.ToImmutable(), Delayed: true);
            }
        }
    }

    public static unsafe ReadOnlySpan<byte> AsSpan(in this PEMemoryBlock memoryBlock)
        => new(memoryBlock.Pointer, memoryBlock.Length);

    public static unsafe nint GetAddress(in this PEMemoryBlock memoryBlock)
        => (nint)memoryBlock.Pointer;

    private const ulong IMAGE_ORDINAL_FLAG64 = 0x8000000000000000;
    private const uint IMAGE_ORDINAL_FLAG32 = 0x80000000;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct ImageImportDescriptor
{
    public readonly uint OriginalFirstThunk;
    public readonly uint TimeDateStamp;
    public readonly uint ForwarderChain;
    public readonly int DllNameRVA;
    public readonly uint FirstThunk;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct ImageDelayImportDescriptor
{
    public readonly uint Attributes;           // 0 = absolute virtual addresses, 1 = relative virtual addresses
    public readonly int DllName;               // The RVA of the name of the DLL to be loaded. The name resides in the read-only data section of the image.
    public readonly int ModuleHandle;          // The RVA of the module handle (in the data section of the image) of the DLL to be delay-loaded. It is used for storage by the routine that is supplied to manage delay-loading.
    public readonly int AddressTable;          // The RVA of the delay-load import address table.
    public readonly int NameTable;             // The RVA of the delay-load name table, which contains the names of the imports that might need to be loaded. This matches the layout of the import name table.
    public readonly int BoundImportTable;      // The RVA of the bound delay-load address table, if it exists.
    public readonly int UnloadImportTable;     // The RVA of the unload delay-load address table, if it exists. This is an exact copy of the delay import address table. If the caller unloads the DLL, this table should be copied back over the delay import address table so that subsequent calls to the DLL continue to use the thunking mechanism correctly.
    public readonly uint TimeStamp;            // The timestamp of the DLL to which this image has been bound.
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct ImageExportDirectory
{
    public readonly uint Characteristics;
    public readonly uint TimeDateStamp;
    public readonly ushort MajorVersion;
    public readonly ushort MinorVersion;
    public readonly uint Name;
    public readonly uint Base;
    public readonly int NumberOfFunctions;
    public readonly int NumberOfNames;
    public readonly int AddressOfFunctions;     // RVA from base of image
    public readonly int AddressOfNames;         // RVA from base of image
    public readonly int AddressOfNameOrdinals;  // RVA from base of image
}

internal record class Exports(string? FullPath, ImmutableArray<(ulong Ordinal, string? Name)> Functions)
{
    public bool Delayed { get; set; }
}
