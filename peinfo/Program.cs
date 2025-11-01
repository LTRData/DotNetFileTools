using Arsenal.ImageMounter.IO.Native;
using DiscUtils;
using DiscUtils.Complete;
using DiscUtils.Wim;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using DiscUtils.Streams;
using System.IO.Compression;
using LTRData.Extensions.Buffers;

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
            else if (cmd.Key == ""
                && cmd.Value.Length >= 1)
            {
                files = cmd.Value;
            }
            else
            {
                Console.WriteLine(@"peinfo - Show EXE or DLL file header information
Copyright (c) 2025 - LTR Data, Olof Lagerkvist
https://ltr-data.se

Syntax:
peinfo filepath1 [filepath2 ...]
peinfo --image=imagefile [--part=partno] filepath1 [filepath2 ...]
peinfo --wim=imagefile --index=wimindex filepath1 [filepath2 ...]");

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
            files = [.. files.SelectMany(f => fs.GetFiles(Path.GetDirectoryName(f) is { Length: > 0 } dir ? dir : ".", Path.GetFileName(f)))];
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

                ProcessFile(file);
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
    {
        var fileData = file.ReadExactly((int)(file.Length - file.Position));

        if (fileData[0] == 0x1f && fileData[1] == 0x8b)
        {
            using var stream = new MemoryStream(fileData);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            gzip.CopyTo(buffer);
            fileData = buffer.ToArray();
        }

        if (fileData.Length >= 2 && fileData[0] == 'M' && fileData[1] == 'Z')
        {
            ProcessPEFile(fileData);
            return;
        }

        var elf = MemoryMarshal.Read<ElfHeader>(fileData);

        if (elf.IsValidMagic)
        {
            ProcessELFFile(elf);
            return;
        }

        throw new InvalidDataException("Not a valid PE file or ELF file");
    }

    private static unsafe void ProcessELFFile(ElfHeader elf)
    {
        Console.WriteLine();
        Console.WriteLine("ELF header:");
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        Console.WriteLine($"{"Machine",-24}{elf.Machine}");
        Console.WriteLine($"{"Type",-24}{elf.Type}");
#endif
        Console.WriteLine($"{"Class",-24}{elf.cls}");
        Console.WriteLine($"{"Data encoding",-24}{elf.data}");
        Console.WriteLine($"{"Version",-24}{elf.version}");
        Console.WriteLine($"{"OS/ABI",-24}{elf.osabi}");
        Console.WriteLine($"{"ABI version",-24}{elf.abiversion}");
    }

    public static void ProcessPEFile(byte[] fileData)
    {
        using var reader = new PEReader([.. fileData]);

        var coffHeader = reader.PEHeaders.CoffHeader;

        Console.WriteLine();
        Console.WriteLine("MZ header:");
        Console.WriteLine($"{"Machine",-24}{coffHeader.Machine}");
        Console.WriteLine($"{"Characteristics",-24}{coffHeader.Characteristics}");

        Console.WriteLine();
        Console.WriteLine("PE header:");

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

        if (reader.PEHeaders.PEHeader is { } peHeader)
        {
            Console.WriteLine($"{"Subsystem",-24}{peHeader.Subsystem}");
            Console.WriteLine($"{"Entry point",-24}0x{peHeader.AddressOfEntryPoint:x8}");
            Console.WriteLine($"{"Base of code",-24}0x{peHeader.BaseOfCode:x8}");
            Console.WriteLine($"{"Base of data",-24}0x{peHeader.BaseOfData:x8}");
            Console.WriteLine($"{"Characteristics",-24}{peHeader.DllCharacteristics}");
            Console.WriteLine($"{"Linker version",-24}{peHeader.MajorLinkerVersion}.{peHeader.MinorLinkerVersion}");
            Console.WriteLine($"{"OS version",-24}{peHeader.MajorOperatingSystemVersion}.{peHeader.MinorOperatingSystemVersion}");
            Console.WriteLine($"{"Subsystem version",-24}{peHeader.MajorSubsystemVersion}.{peHeader.MinorSubsystemVersion}");
        }

        try
        {
            var fileVersion = new NativeFileVersion(fileData);

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

        var securitySection = NativePE.GetRawFileCertificateSection(fileData);

        if (!securitySection.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine("Authenticode signature:");

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
    }
}
