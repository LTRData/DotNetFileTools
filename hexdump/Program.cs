using LTRLib.Extensions;
using LTRLib.IO;
using LTRLib.LTRGeneric;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace hexdump;

public static class Program
{
    public static int BufferSize { get; set; } = 65536;

    public static void DumpStream(TextWriter writer, Stream stream, long offset, long? count)
    {
        if (offset != 0)
        {
            if (offset < 0)
            {
                offset = stream.Length + offset;
            }

            stream.Position = offset;
        }

        var bytes = new byte[BufferSize];

        for (; ; )
        {
            var length = bytes.Length;
            if (stream.CanSeek)
            {
                length = (int)Math.Min(length, stream.Length - stream.Position);
            }
            if (count.HasValue)
            {
                length = (int)Math.Min(length, count.Value);
            }
            if (length == 0)
            {
                break;
            }
            length = stream.Read(bytes, 0, length);
            if (length == 0)
            {
                break;
            }
            if (count.HasValue)
            {
                count -= length;
            }
            foreach (var line in bytes.Take(length).FormatHexLines())
            {
                writer.Write(((ushort)(offset >> 16)).ToString("X4"));
                writer.Write(' ');
                writer.Write(((ushort)offset).ToString("X4"));
                writer.Write("  ");
                writer.WriteLine(line);
                offset += 0x10;
            }
        }
    }

    public static int Main(params string[] command_line_args)
    {
        try
        {
            UnsafeMain(command_line_args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(ex.JoinMessages());
            Console.ResetColor();
            return Marshal.GetHRForException(ex);
        }
    }

    public static void UnsafeMain(params string[] command_line_args)
    {
        var commands = StringSupport.ParseCommandLine(command_line_args, StringComparer.OrdinalIgnoreCase);

        long offset = 0;
        long? count = null;

        foreach (var command in commands)
        {
            if (command.Key.Length == 0)
            {
            }
            else if (command.Key.Equals("offset", StringComparison.OrdinalIgnoreCase) &&
                command.Value.Length == 1)
            {
                offset = StringSupport.ParseSuffixedSize(command.Value[0]) ??
                    throw new FormatException($"The value '{command.Value[0]}' is not a valid size");
            }
            else if (command.Key.Equals("count", StringComparison.OrdinalIgnoreCase) &&
                command.Value.Length == 1)
            {
                count = StringSupport.ParseSuffixedSize(command.Value[0]) ??
                    throw new FormatException($"The value '{command.Value[0]}' is not a valid size");
            }
            else
            {
                if (!command.Key.Equals("help", StringComparison.OrdinalIgnoreCase) &&
                    !command.Key.Equals("?", StringComparison.Ordinal))
                {
                    Console.WriteLine($"Unsupported option '{command.Key}'");
                    Console.WriteLine();
                }
                Console.WriteLine("Hex dump tool by Olof Lagerkvist, LTR Data");
                Console.WriteLine("Copyright (c) LTR Data 2021, http://ltr-data.se");
                Console.WriteLine();
                Console.WriteLine("Syntax:");
                Console.WriteLine("hexdump [--offset:n] [--count:n] [file1 ...]");
                return;
            }
        }

        if (!commands.TryGetValue(string.Empty, out var paths))
        {
            var stream = Console.OpenStandardInput();
            DumpStream(Console.Out, stream, offset, count);
            return;
        }

        foreach (var path in paths
            .SelectMany(path =>
            {
                if (path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                    path.StartsWith(@"\\.\", StringComparison.Ordinal))
                {
                    return Enumerable.Repeat(path, 1);
                }
                return Directory.EnumerateFiles(GetDirectoryOrCurrent(path), Path.GetFileName(path));
            }))
        {
            Console.WriteLine();
            Console.WriteLine(path);
            using var stream = OpenStream(path);
            DumpStream(Console.Out, stream, offset, count);
        }
    }

    public static string GetDirectoryOrCurrent(string path)
    {
        path = Path.GetDirectoryName(path);
        return string.IsNullOrWhiteSpace(path) ? "." : path;
    }

    public static FileStream OpenStream(string path)
    {
#if NETCOREAPP
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
#endif

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return new DiskStream(path, FileAccess.Read);
        }
        else
        {
            return NativeFileIO.OpenFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
    }
}
