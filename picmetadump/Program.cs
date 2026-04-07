using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using LTRData.Geodesy.Positions;
using LTRLib.Imaging;
using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace picmetadump;

public static class Program
{
    public static void Main(params string[] args)
    {
        var cmds = CommandLineParser.ParseCommandLine(args, StringComparer.Ordinal);

        if (cmds.Keys.Any(key => key != "")
            || !cmds.TryGetValue("", out var paths) || paths.Length == 0)
        {
            Console.Error.WriteLine("Usage: picmetadump <image_path> [<image_path> ...]");
            return;
        }

        Console.WriteLine("Image Path;Date Taken;Latitude;Longitude;Camera Manufacturer;Camera Model");

        foreach (var image_path in cmds[""]
            .SelectMany(path =>
            {
                var dir = Path.GetDirectoryName(path) is { Length: > 0 } d ? d : ".";
                var file = Path.GetFileName(path);

#if NET40_OR_GREATER || !NETFRAMEWORK
                return Directory.EnumerateFiles(dir, file);
#else
                return Directory.GetFiles(dir, file);
#endif
            }))
        {
            try
            {
                using var file = File.OpenRead(image_path);

                var frame = BitmapFrame.Create(file);

                if (frame.Metadata is not BitmapMetadata metaData)
                {
                    Console.Error.WriteLine($"Image {image_path} contains no metadata");
                    continue;
                }

                var date = metaData.DateTaken;

                if (!metaData.TryGetGeoLocation(out var location))
                {
                    location = null;
                }

                Console.WriteLine($"{image_path};{date};{location?.LatitudeToString(LatLonPosition.GeoFormat.Degrees)};{location?.LongitudeToString(LatLonPosition.GeoFormat.Degrees)};{metaData.CameraManufacturer};{metaData.CameraModel}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing image {image_path}: {ex.JoinMessages()}");
            }
        }
    }
}
