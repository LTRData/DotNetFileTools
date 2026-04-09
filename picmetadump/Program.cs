using LTRData.Extensions.Buffers;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using LTRData.Geodesy.Positions;
using LTRLib.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
#if NET45_OR_GREATER || NETCOREAPP || NETSTANDARD
using System.Net.Http;
#endif
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
                return Directory.EnumerateFiles(dir, file);
#else
                return (IEnumerable<string>)Directory.GetFiles(dir, file);
#endif
            }))
        {
            try
            {
                using Stream pic = path.Contains("://") && Uri.TryCreate(path, UriKind.Absolute, out var uri)
                    ? DownloadData(uri) : File.OpenRead(path);

                var frame = BitmapFrame.Create(pic);

                if (frame.Metadata is not BitmapMetadata metaData)
                {
                    Console.Error.WriteLine($"Image {path} contains no metadata");
                    continue;
                }

                var date = metaData.DateTaken;

                if (!metaData.TryGetGeoLocation(out var location))
                {
                    location = null;
                }

                Console.WriteLine($"{path};{date};{location?.LatitudeToString(LatLonPosition.GeoFormat.Degrees)};{location?.LongitudeToString(LatLonPosition.GeoFormat.Degrees)};{metaData.CameraManufacturer};{metaData.CameraModel}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing image {path}: {ex.JoinMessages()}");
            }
        }
    }

#if NET45_OR_GREATER || NETCOREAPP || NETSTANDARD
    private static Stream DownloadData(Uri uri)
    {
        var client = new HttpClient();
        var response = client.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        return response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
    }
#else
    private static MemoryStream DownloadData(Uri uri)
    {
        var client = new WebClient();
        var response = client.DownloadData(uri);
        return new(response);
    }
#endif
}
