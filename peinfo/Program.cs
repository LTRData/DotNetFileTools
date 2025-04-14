using Arsenal.ImageMounter.IO.Native;
using DiscUtils;
using DiscUtils.Complete;
using DiscUtils.Wim;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System.Reflection.PortableExecutable;

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

        foreach (var path in files)
        {
            try
            {
                using Stream file = fs is not null
                    ? fs.OpenFile(path, FileMode.Open, FileAccess.Read)
                    : File.OpenRead(path);

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
        if (!file.CanSeek)
        {
            var bufferStream = new MemoryStream((int)(file.Length - file.Position));
            file.CopyTo(bufferStream);
            bufferStream.Position = 0;

            file = bufferStream;
        }

        using var reader = new PEReader(file);

        var coffHeader = reader.PEHeaders.CoffHeader;

        Console.WriteLine();
        Console.WriteLine("MZ header:");
        Console.WriteLine($"{"Machine",-24}" + coffHeader.Machine);
        Console.WriteLine($"{"Characteristics",-24}" + coffHeader.Characteristics);

        if (reader.PEHeaders.IsCoffOnly)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("PE header:");

        if (reader.PEHeaders.IsDll)
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
            var image = reader.GetEntireImage();
            var fileData = new ReadOnlySpan<byte>(image.Pointer, image.Length);
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
    }
}
