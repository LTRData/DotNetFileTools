using Arsenal.ImageMounter.IO.Native;
using LTRData.Extensions.Formatting;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace peinfo;

public static class Program
{
    public static int Main(params string[] args)
    {
        var errCode = 0;

        foreach (var path in args)
        {
            try
            {
                ProcessFile(path);
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

    public static unsafe void ProcessFile(string path)
    {
        using var file = File.OpenRead(path);

        using var reader = new PEReader(file);

        var coffHeader = reader.PEHeaders.CoffHeader;

        Console.WriteLine($"{"MZ machine",-24}" + coffHeader.Machine);
        Console.WriteLine($"{"MZ characteristics",-24}" + coffHeader.Characteristics);

        if (reader.PEHeaders.IsCoffOnly)
        {
            return;
        }

        if (reader.PEHeaders.IsExe)
        {
            Console.WriteLine($"{"PE type",-24}Executable");
        }

        if (reader.PEHeaders.IsDll)
        {
            Console.WriteLine($"{"PE type",-24}DLL");
        }

        if (reader.PEHeaders.PEHeader is { } peHeader)
        {
            Console.WriteLine($"{"PE subsystem",-24}{peHeader.Subsystem}");
            Console.WriteLine($"{"PE entry point",-24}{peHeader.AddressOfEntryPoint:x8}");
            Console.WriteLine($"{"PE characteristics",-24}{peHeader.DllCharacteristics}");
            Console.WriteLine($"{"PE linker version",-24}{peHeader.MajorLinkerVersion}.{peHeader.MinorLinkerVersion}");
            Console.WriteLine($"{"PE OS version",-24}{peHeader.MajorOperatingSystemVersion}.{peHeader.MinorOperatingSystemVersion}");
            Console.WriteLine($"{"PE subsystem version",-24}{peHeader.MajorSubsystemVersion}.{peHeader.MinorSubsystemVersion}");
        }

        try
        {
            var image = reader.GetEntireImage();
            var fileData = new ReadOnlySpan<byte>(image.Pointer, image.Length);
            var fileVersion = new NativeFileVersion(fileData);
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
    }
}
