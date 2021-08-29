using LTRLib.Extensions;
using LTRLib.IO;
using LTRLib.LTRGeneric;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace hexdump
{
    public static class Program
    {
        public static void DumpStream(TextWriter writer, Stream stream, long? count)
        {
            long i = 0;
            var bytes = new byte[65536];
            for (; ;)
            {
                var length = bytes.Length;
                if (count.HasValue)
                {
                    length = (int)Math.Min(length, count.Value - i);
                }
                length = stream.Read(bytes, 0, length);
                if (length == 0)
                {
                    break;
                }
                foreach (var line in bytes.Take(length).FormatHexLines())
                {
                    writer.Write(((ushort)(i >> 16)).ToString("X4"));
                    writer.Write(' ');
                    writer.Write(((ushort)i).ToString("X4"));
                    writer.Write("  ");
                    writer.WriteLine(line);
                    i += 0x10;
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
                if (offset > 0)
                {
                    stream.Seek(offset, SeekOrigin.Current);
                }
                DumpStream(Console.Out, stream, count);
                return;
            }

            foreach (var path in paths)
            {
                Console.WriteLine(path);
                using var stream = NativeFileIO.OpenFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (offset > 0)
                {
                    stream.Position = offset;
                }
                DumpStream(Console.Out, stream, count);
            }
        }
    }
}
