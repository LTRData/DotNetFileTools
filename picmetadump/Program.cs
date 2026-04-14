using LTRData.Extensions.Buffers;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;

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

        foreach (var path in cmds[""]
            .SelectMany(path =>
            {
                if (path.Contains("://"))
                {
                    return SingleValueEnumerable.Get(path);
                }

                var dir = Path.GetDirectoryName(path) is { Length: > 0 } d ? d : ".";
                var file = Path.GetFileName(path);

#if NET40_OR_GREATER || NETCOREAPP || NETSTANDARD
                return System.IO.Directory.EnumerateFiles(dir, file);
#else
                return (IEnumerable<string>)System.IO.Directory.GetFiles(dir, file);
#endif
            }))
        {
            try
            {
                using Stream pic = path.Contains("://") && Uri.TryCreate(path, UriKind.Absolute, out var uri)
                    ? DownloadData(uri) : File.OpenRead(path);

                var directories = ImageMetadataReader.ReadMetadata(pic, path);

                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

                DateTime? date = null;

                if (subIfd is not null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                {
                    date = dt;
                }

                var gps = directories.OfType<GpsDirectory>().FirstOrDefault();

                GeoLocation? location = null;

                if (gps is not null && gps.TryGetGeoLocation(out var loc))
                {
                    location = loc;
                }

                var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

                Console.WriteLine($"{path};{date};{location?.Latitude.ToString("N7", NumberFormatInfo.InvariantInfo)};{location?.Longitude.ToString("N7", NumberFormatInfo.InvariantInfo)};{ifd0?.GetDescription(ExifDirectoryBase.TagMake)};{ifd0?.GetDescription(ExifDirectoryBase.TagModel)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing image {path}: {ex.JoinMessages()}");
            }
        }
    }

    private static Stream DownloadData(Uri uri)
    {
        var client = new HttpClient();
        var response = client.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        return response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
    }
}
