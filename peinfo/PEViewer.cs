using Arsenal.ImageMounter.IO.Native;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.Formatting;
using LTRData.Extensions.Native.Memory;
using LTRData.Extensions.Split;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace peinfo;

public static class PEViewer
{
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

            var apiSetLookup = ApiSetResolver.GetApiSetTranslations();

            var importSection = peHeader.ImportTableDirectory;

            if (importSection.Size > 0
                && reader.PEHeaders.TryGetDirectoryOffset(importSection, out var importSectionAddress))
            {
                Console.WriteLine();
                Console.WriteLine("Imported DLLs:");

                var descriptors = MemoryMarshal.Cast<byte, ImageImportDescriptor>(fileData.AsSpan(importSectionAddress, importSection.Size));

                ProcessImportTable(reader, descriptors, apiSetLookup);
            }

            var delayImportSection = peHeader.DelayImportTableDirectory;

            if (delayImportSection.Size > 0
                && reader.PEHeaders.TryGetDirectoryOffset(delayImportSection, out var delayImportSectionAddress))
            {
                Console.WriteLine();
                Console.WriteLine("Delay Imported DLLs:");

                var descriptors = MemoryMarshal.Cast<byte, ImageDelayImportDescriptor>(fileData.AsSpan(delayImportSectionAddress, delayImportSection.Size));

                ProcessDelayImportTable(reader, descriptors, apiSetLookup);
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
                    var functionPointers = MemoryMarshal.Cast<byte, int>(reader.GetSectionData(exportDir.AddressOfFunctions).AsSpan()).Slice(0, exportDir.NumberOfFunctions);

                    for (var i = 0; i < exportDir.NumberOfNames; i++)
                    {
                        var nameRVA = namePointers[i];
                        var name = reader.GetSectionData((int)nameRVA).AsSpan().ReadNullTerminatedAsciiString();
                        var ordinal = ordinalPointers[i];
                        var functionRVA = functionPointers[ordinal];

                        if (functionRVA >= exportSection.RelativeVirtualAddress && functionRVA < exportSection.RelativeVirtualAddress + exportSection.Size)
                        {
                            // Forwarder
                            var forwarderString = reader.GetSectionData(functionRVA).AsSpan().ReadNullTerminatedAsciiString();

                            var delimiter = forwarderString.LastIndexOf('.');

                            var module = delimiter >= 0 ? forwarderString.Substring(0, delimiter) : null;

                            if (LookupApiSet(apiSetLookup, module, out var apiSetTarget))
                            {
                                forwarderString = $"{module}[{apiSetTarget}].{forwarderString.Substring(delimiter + 1)}";
                            }

                            Console.WriteLine($"    (Ordinal: 0x{exportDir.Base + ordinal:X4}, Forwarded to: {forwarderString})  {name}");

                            continue;
                        }

                        Console.WriteLine($"    (Ordinal: 0x{exportDir.Base + ordinal:X4}, RVA: 0x{functionRVA:X8})  {name}");
                    }
                }

                if (exportDir.NumberOfFunctions != 0)
                {
                    var functionPointers = MemoryMarshal.Cast<byte, int>(reader.GetSectionData(exportDir.AddressOfFunctions).AsSpan()).Slice(0, exportDir.NumberOfFunctions);

                    for (var i = 0; i < exportDir.NumberOfFunctions; i++)
                    {
                        var functionRVA = functionPointers[i];

                        if (functionRVA >= exportSection.RelativeVirtualAddress && functionRVA < exportSection.RelativeVirtualAddress + exportSection.Size)
                        {
                            // Forwarder
                            var forwarderString = reader.GetSectionData(functionRVA).AsSpan().ReadNullTerminatedAsciiString();

                            var delimiter = forwarderString.LastIndexOf('.');

                            var module = delimiter >= 0 ? forwarderString.Substring(0, delimiter) : null;

                            if (LookupApiSet(apiSetLookup, module, out var apiSetTarget))
                            {
                                forwarderString = $"{module}[{apiSetTarget}].{forwarderString.Substring(delimiter + 1)}";
                            }

                            Console.WriteLine($"    (Ordinal: 0x{exportDir.Base + i:X4}, Forwarded to: {forwarderString})");

                            continue;
                        }

                        Console.WriteLine($"    (Ordinal: 0x{exportDir.Base + i:X4}, RVA: 0x{functionRVA:X8})");
                    }
                }
            }
        }
    }

    private static void ProcessImportTable(PEReader reader, ReadOnlySpan<ImageImportDescriptor> descriptors, ImmutableDictionary<string, string>? apiSetLookup)
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
            Console.Write(moduleName);

            if (LookupApiSet(apiSetLookup, moduleName, out var apiSetTarget))
            {
                Console.Write(" (");
                Console.Write(apiSetTarget);
                Console.Write(')');
            }

            Console.WriteLine();
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

    private static void ProcessDelayImportTable(PEReader reader, ReadOnlySpan<ImageDelayImportDescriptor> descriptors, ImmutableDictionary<string, string>? apiSetLookup)
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
            Console.Write(moduleName);

            if (LookupApiSet(apiSetLookup, moduleName, out var apiSetTarget))
            {
                Console.Write(" (");
                Console.Write(apiSetTarget);
                Console.Write(')');
            }

            Console.WriteLine();
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

    public static void ProcessDependencyTree(Stream file, string filePath, bool includeDelayed)
    {
        var fileData = Program.DecompressData(file.ReadToEnd());

        var headers = NativePE.GetImageNtHeaders(fileData);

        Console.WriteLine("Dependency Tree:");

        var pathsEnum = Environment.GetEnvironmentVariable("PATH")
            .AsMemory()
            .TokenEnum(Path.PathSeparator)
            .Select(m => m.ToString());

        if (file is FileStream { Name: { } fileName }
            && Path.GetDirectoryName(fileName) is { Length: > 0 } dir)
        {
            filePath = fileName;

            pathsEnum = pathsEnum.Prepend(dir);
            pathsEnum = pathsEnum.Prepend(Path.Combine(dir, "lib"));
        }

        // If running as 64-bit process analyzing 32-bit file, add SysWOW64 first
        if (Environment.Is64BitProcess && headers.FileHeader.Machine == ImageFileMachine.I386
            && Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) is { Length: > 0 } sysDirX86)
        {
            pathsEnum = pathsEnum.Append(sysDirX86);
            pathsEnum = pathsEnum.Append(Path.Combine(sysDirX86, "drivers"));
        }

        if (Environment.GetFolderPath(Environment.SpecialFolder.System) is { Length: > 0 } sysDir)
        {
            pathsEnum = pathsEnum.Append(sysDir);
            pathsEnum = pathsEnum.Append(Path.Combine(sysDir, "drivers"));
        }

        pathsEnum = pathsEnum
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists);

        IReadOnlyList<string> paths = [.. pathsEnum];

        var apiSetLookup = ApiSetResolver.GetApiSetTranslations();

        ProcessDependencyTree(fileData,
                              filePath,
                              headers.FileHeader.Machine,
                              modules: new(StringComparer.OrdinalIgnoreCase),
                              apiSetLookup: apiSetLookup,
                              paths: paths,
                              lastFoundPathIndices: [],
                              indent: 2,
                              isDelayedTree: false,
                              includeDelayed: includeDelayed);
    }

    private static Exports ProcessDependencyTree(byte[] fileData,
                                                 string filePath,
                                                 ImageFileMachine machine,
                                                 Dictionary<string, Exports> modules,
                                                 ImmutableDictionary<string, string>? apiSetLookup,
                                                 IReadOnlyList<string> paths,
                                                 List<int> lastFoundPathIndices,
                                                 int indent,
                                                 bool isDelayedTree,
                                                 bool includeDelayed)
    {
        var ownExports = GetExports(fileData);

        var ownExportsRecord = new Exports(filePath, ownExports);

        var ownModuleNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

        modules[ownModuleNameWithoutExtension] = ownExportsRecord;

        foreach (var (moduleNameImport, functions, delayedImport) in EnumerateDependencies(fileData, includeDelayed))
        {
            if (!includeDelayed && delayedImport)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(moduleNameImport))
            {
                continue;
            }

            if (!LookupApiSet(apiSetLookup, moduleNameImport, out var moduleName))
            {
                moduleName = moduleNameImport;
            }

            var moduleNameWithoutExtension = Path.GetFileNameWithoutExtension(moduleName);

            var delayed = delayedImport || isDelayedTree;

            if (!modules.TryGetValue(moduleNameWithoutExtension, out var exports))
            {
                foreach (var i in lastFoundPathIndices.Concat(Enumerable.Range(0, paths.Count).Except(lastFoundPathIndices)))
                {
                    exports = TryPath(moduleName, modules, apiSetLookup, paths, i, indent, delayed, includeDelayed, machine);

                    if (exports is not null)
                    {
                        if (!lastFoundPathIndices.Contains(i))
                        {
                            lastFoundPathIndices.Add(i);
                        }

                        exports.Delayed = delayed;

                        break;
                    }
                }

                if (exports is null)
                {
                    if (ownExports.Length != 0
                        && functions.All(func => ownExports.Any(ownexp => (func.Name is not null && ownexp.Name == func.Name) || (func.Ordinal != 0 && ownexp.Ordinal == func.Ordinal))))
                    {
                        exports = new(filePath, ownExports);
                    }
                    else
                    {
                        exports = new(FullPath: null, Functions: default)
                        {
                            Delayed = delayed
                        };
                    }

                    modules[moduleNameWithoutExtension] = exports;

                    if (exports.FullPath is null)
                    {
                        var chars = Program.GetIndent(indent);

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
            }

            if (exports.FullPath is null)
            {
                if (exports.Delayed && !delayed)
                {
                    var chars = Program.GetIndent(indent);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{chars}  Dependency '{moduleName}' was previously reported as missing delay-loaded, now required as normal import");
                    Console.ResetColor();

                    exports.Delayed = false;
                }

                continue;
            }

            if (exports.Delayed && !delayed)
            {
                var chars = Program.GetIndent(indent);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"{chars}{exports.FullPath}");
                Console.ResetColor();

                exports.Delayed = false;
            }

            foreach (var (Ordinal, Hint, Name) in functions)
            {
                var match = exports.Functions.FirstOrDefault(f => (f.Ordinal != 0 && f.Ordinal == Ordinal) || f.Name == Name);

                if (match.Name is null && match.Ordinal == 0)
                {
                    var chars = Program.GetIndent(indent);

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

        return ownExportsRecord;
    }

    private static bool LookupApiSet(ImmutableDictionary<string, string>? apiSetLookup,
                                     string? moduleNameImport,
                                     [NotNullWhen(true)] out string? moduleName)
    {
        moduleName = null;

        if (apiSetLookup is null
            || moduleNameImport is null
            || !moduleNameImport.Contains("-l")
            || moduleNameImport.Contains('.') && !moduleNameImport.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lastDelimited = moduleNameImport.LastIndexOf('-');

        if (lastDelimited >= 0)
        {
            moduleNameImport = moduleNameImport.Substring(0, lastDelimited);
        }

        return apiSetLookup.TryGetValue(moduleNameImport, out moduleName);
    }

    private static readonly ImmutableArray<string> defaultExtensions = [".dll", ".sys", ".exe"];

    private static Exports? TryPath(string moduleName,
                                    Dictionary<string, Exports> modules,
                                    ImmutableDictionary<string, string>? apiSetLookup,
                                    IReadOnlyList<string> paths,
                                    int pathIndex,
                                    int indent,
                                    bool isDelayedTree,
                                    bool includeDelayed, ImageFileMachine expectedMachine)
    {
        var tryPath = Path.Combine(paths[pathIndex], moduleName);

        if (!File.Exists(tryPath))
        {
            if (moduleName.Contains('.'))
            {
                return null;
            }

            string? tryExtPath = null;

            foreach (var extension in defaultExtensions)
            {
                tryExtPath = tryPath + extension;

                if (File.Exists(tryExtPath))
                {
                    break;
                }

                tryExtPath = null;
            }

            if (tryExtPath is null)
            {
                return null;
            }

            tryPath = tryExtPath;
        }

        var chars = Program.GetIndent(indent);

        if (isDelayedTree)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
        }

        try
        {
            var fileData = File.ReadAllBytes(tryPath);

            var headers = NativePE.GetImageNtHeaders(fileData); // Validate PE file

            if (headers.FileHeader.Machine != expectedMachine)
            {
                return null;
            }

            Console.WriteLine($"{chars}{tryPath}");
            Console.ResetColor();

            var exportsRecord = ProcessDependencyTree(fileData,
                                                      tryPath,
                                                      expectedMachine,
                                                      modules,
                                                      apiSetLookup,
                                                      paths,
                                                      [pathIndex],
                                                      indent + 2,
                                                      isDelayedTree,
                                                      includeDelayed);

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

    private static string GetDefaultExtension(string moduleName)
    {
        if (".sys".Equals(Path.GetExtension(moduleName), StringComparison.OrdinalIgnoreCase))
        {
            return ".sys";
        }

        return ".dll";
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

    public static IEnumerable<(string Module, ImmutableArray<(ulong Ordinal, ushort Hint, string? Name)> Functions, bool Delayed)> EnumerateDependencies(byte[] fileData, bool includeDelayed)
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

        var exportSection = peHeader.ExportTableDirectory;

        if (exportSection.Size != 0
            && reader.PEHeaders.TryGetDirectoryOffset(exportSection, out var exportSectionAddress))
        {
            // Process any forwarders as dependencies

            var exportDir = MemoryMarshal.Read<ImageExportDirectory>(fileData.AsSpan(exportSectionAddress, exportSection.Size));
            var moduleName = reader.GetSectionData((int)exportDir.Name).AsSpan().ReadNullTerminatedAsciiString();

            List<(string forwarderModuleName, (ulong Ordinal, ushort Hint, string? Name) function)>? forwarders = null;
            
            if (exportDir.NumberOfNames != 0)
            {
                var namePointers = MemoryMarshal.Cast<byte, uint>(reader.GetSectionData(exportDir.AddressOfNames).AsSpan()).Slice(0, exportDir.NumberOfNames);
                var ordinalPointers = MemoryMarshal.Cast<byte, ushort>(reader.GetSectionData(exportDir.AddressOfNameOrdinals).AsSpan()).Slice(0, exportDir.NumberOfNames);
                var functionPointers = MemoryMarshal.Cast<byte, int>(reader.GetSectionData(exportDir.AddressOfFunctions).AsSpan()).Slice(0, exportDir.NumberOfFunctions);

                for (var i = 0; i < exportDir.NumberOfNames; i++)
                {
                    var nameRVA = namePointers[i];
                    var ordinal = ordinalPointers[i];
                    var functionRVA = functionPointers[ordinal];

                    var forwarder = ParseForwarder(reader, exportSection, functionRVA);
                    
                    if (forwarder.HasValue)
                    {
                        forwarders ??= new(1);

                        forwarders.Add(forwarder.Value);
                    }
                }
            }

            if (exportDir.NumberOfFunctions != 0)
            {
                var functionPointers = MemoryMarshal.Cast<byte, int>(reader.GetSectionData(exportDir.AddressOfFunctions).AsSpan()).Slice(0, exportDir.NumberOfFunctions);

                for (var i = 0; i < exportDir.NumberOfFunctions; i++)
                {
                    var functionRVA = functionPointers[i];

                    var forwarder = ParseForwarder(reader, exportSection, functionRVA);

                    if (forwarder.HasValue)
                    {
                        forwarders ??= new(1);

                        forwarders.Add(forwarder.Value);
                    }
                }
            }

            if (forwarders is not null)
            {
                foreach (var forwarder in forwarders.GroupBy(fw => fw.forwarderModuleName, fw => fw.function))
                {
                    yield return (forwarder.Key, forwarder.ToImmutableArray(), Delayed: false);
                }
            }
        }

        if (!includeDelayed)
        {
            yield break;
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

    private static (string forwarderModuleName, (ulong Ordinal, ushort Hint, string? Name) function)? ParseForwarder(PEReader reader, DirectoryEntry exportSection, int functionRVA)
    {
        if (functionRVA < exportSection.RelativeVirtualAddress
            || functionRVA >= exportSection.RelativeVirtualAddress + exportSection.Size)
        {
            return default;
        }

        var forwarderString = reader.GetSectionData(functionRVA).AsSpan();

        var length = forwarderString.IndexOf((byte)'\0');

        if (length >= 0)
        {
            forwarderString = forwarderString.Slice(0, length);
        }

        var moduleEndIndex = forwarderString.LastIndexOf((byte)'.');

        if (moduleEndIndex <= 0)
        {
            return default;
        }

        var forwarderModuleName = forwarderString.Slice(0, moduleEndIndex).ReadNullTerminatedAsciiString();

        var functionName = forwarderString.Slice(moduleEndIndex + 1).ReadNullTerminatedAsciiString();

        if (functionName.Length == 0)
        {
            return default;
        }

        if (functionName[0] == '#')
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
                        if (!ulong.TryParse(functionName.AsSpan(1), out var ordinalValue))
#else
            if (!ulong.TryParse(functionName.Substring(1), out var ordinalValue))
#endif
            {
                return default;
            }

            return (forwarderModuleName, (Ordinal: ordinalValue, Hint: 0, Name: null));
        }
        else
        {
            return (forwarderModuleName, (Ordinal: 0ul, Hint: 0, Name: functionName));
        }
    }

    public static unsafe ReadOnlySpan<byte> AsSpan(in this PEMemoryBlock memoryBlock)
        => new(memoryBlock.Pointer, memoryBlock.Length);

    public static unsafe ReadOnlySpan<byte> AsSpan<T>(in this PEMemoryBlock memoryBlock) where T : unmanaged
        => new(memoryBlock.Pointer, memoryBlock.Length / Unsafe.SizeOf<T>());

    public static unsafe nint GetAddress(in this PEMemoryBlock memoryBlock)
        => (nint)memoryBlock.Pointer;

    public static MemoryManager<byte> GetMemoryManager(in this PEMemoryBlock memoryBlock)
        => new NativeMemory<byte>(memoryBlock.GetAddress(), memoryBlock.Length).GetMemoryManager();

    public static MemoryManager<T> GetMemoryManager<T>(in this PEMemoryBlock memoryBlock) where T : unmanaged
        => new NativeMemory<T>(memoryBlock.GetAddress(), memoryBlock.Length / Unsafe.SizeOf<T>()).GetMemoryManager();

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
