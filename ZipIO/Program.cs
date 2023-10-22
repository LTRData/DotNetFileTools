using LTRData.Extensions.Buffers;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0057 // Use range operator

namespace ZipIO;

public static class Program
{
    public static int Main(params string[] args)
    {
        if (args is null || args.Length < 2)
        {
            Console.Error.WriteLine(@"Syntax:
zipio add|list|cat|del|fromdir|todir|freshen|time [switches] zipfile [args ...]

More syntax help is available using 'zipio command --help', for example:
zipio add --help
");
            return -1;
        }

        try
        {
            var sub_args = args.Skip(1).ToList();

            switch (args[0].ToLowerInvariant())
            {
                case "add":
                    return ZipAdd(sub_args);
                case "list":
                    return ZipList(sub_args);
                case "del":
                    return ZipDel(sub_args);
                case "fromdir":
                    return ZipFromDir(sub_args);
                case "todir":
                    return ZipToDir(sub_args);
                case "replace":
                    return ZipFreshen.FreshenOrReplace(ZipFreshen.FreshenOrReplaceOperation.Replace, sub_args);
                case "freshen":
                    return ZipFreshen.FreshenOrReplace(ZipFreshen.FreshenOrReplaceOperation.Freshen, sub_args);
                case "time":
                    return ZipTime.Time(sub_args);
                case "cat":
                    return ZipCat(sub_args);
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Unknown command: {args[0]}");
            Console.ResetColor();
            return -1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(ex.JoinMessages());
            Console.ResetColor();
            return ex.HResult;
        }
    }

    public static int ZipCat(params string[] args)
    => ZipCat((IEnumerable<string>)args);

    public static int ZipCat(IEnumerable<string> cmdLine)
    {
        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        var isRegEx = false;

        foreach (var arg in cmd)
        {
            if (arg.Key == "e")
            {
                isRegEx = true;
            }
            else if (arg.Key != "")
            {
                Console.WriteLine(@"Syntax:
zipio cat [-e] zipfile [file1 file2...]

Show contents of files within zip archive.

-e          Use regular expressions to match file names
");

                return -1;
            }
        }

        if (!cmd.TryGetValue("", out var args) || args.Length == 0)
        {
            Console.WriteLine("Missing zip file path or file names.");
            return 0;
        }

        var stdOut = Console.OpenStandardOutput();

        using var archive = args[0] == "-"
            ? new ZipArchive(Console.OpenStandardInput(), ZipArchiveMode.Read)
            : ZipFile.OpenRead(args[0]);

        foreach (var arg in args.Length < 2 ? SingleValueEnumerable.Get("") : args.Skip(1))
        {
            Func<ZipArchiveEntry, bool> matchFunc;

            if (arg == "")
            {
                matchFunc = _ => true;
            }
            else if (!isRegEx)
            {
                var pattern = arg.Replace('\\', '/');
                
                if (pattern.Contains('/'))
                {
                    matchFunc = entry => entry.FullName == arg;
                }
                else
                {
                    matchFunc = entry => entry.Name == arg;
                }
            }
            else
            {
                var regex = new Regex(arg);

                matchFunc = entry => regex.IsMatch(entry.FullName);
            }

            foreach (var entry in archive.Entries.Where(matchFunc))
            {
                entry.Open().CopyTo(stdOut);
            }
        }

        return 0;
    }

    public static int ZipList(params string[] args)
        => ZipList((IEnumerable<string>)args);

    public static int ZipList(IEnumerable<string> cmdLine)
    {
        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        var longlisting = false;
        foreach (var arg in cmd)
        {
            if (arg.Key.Equals("l", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("long", StringComparison.OrdinalIgnoreCase))
            {
                longlisting = true;
            }
            else if (arg.Key == "")
            {
            }
            else
            {
                Console.WriteLine(@"Syntax:
zipio list [--long] zipfile1 [zipfile2 ...]

Show contents of zip archive.

-l
--long      Detailed file information
");

                return -1;
            }
        }

        if (!cmd.TryGetValue("", out var args) || args.Length == 0)
        {
            Console.WriteLine("Missing zip file paths.");
            return 0;
        }

        foreach (var arg in args.SelectMany(path =>
        {
            if (path == "-")
            {
                return SingleValueEnumerable.Get("-");
            }

            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = ".";
            }

            try
            {
                return Directory.EnumerateFiles(dir, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"{path}: {ex.GetBaseException().Message}");
                Console.ResetColor();
                return Enumerable.Empty<string>();
            }
        }))
        {
            try
            {
                using var archive = arg == "-"
                    ? new ZipArchive(Console.OpenStandardInput(), ZipArchiveMode.Read)
                    : ZipFile.OpenRead(arg);

                foreach (var entry in archive.Entries)
                {
                    if (longlisting)
                    {
                        Console.WriteLine($"{entry.LastWriteTime.LocalDateTime,19:g} {entry.Length,15:#,##0} ({entry.CompressedLength,15:#,##0}) {entry.FullName}");
                    }
                    else
                    {
                        Console.WriteLine(entry.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"{arg}: {ex.GetBaseException().Message}");
                Console.ResetColor();
            }
        }

        return 0;
    }

    internal static IEnumerable<string> ResolveWildcards(string path, SearchOption searchOption)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var searchdir = dir;
        
        if (string.IsNullOrWhiteSpace(searchdir))
        {
            searchdir = ".";
        }
        
        var file = Path.GetFileName(path);
        
        path = Path.Combine(searchdir, file);
        
        var prefix_length = path.Length - file.Length;
        
        return Directory
            .EnumerateFiles(searchdir, file, searchOption)
            .Select(entry => Path.Combine(dir, entry.Substring(prefix_length)));
    }

    public static int ZipAdd(params string[] args)
        => ZipAdd((IEnumerable<string>)args);

    public static int ZipAdd(IEnumerable<string> cmdLine)
    {
        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        string? zip_path = null;
        IEnumerable<string>? files_to_add = null;
        var searchOption = SearchOption.TopDirectoryOnly;
        var purge = false;
        var newer = false;
        var cont = false;

        foreach (var arg in cmd)
        {
            if (arg.Key.Equals("s", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("r", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("recurse", StringComparison.OrdinalIgnoreCase))
            {
                searchOption = SearchOption.AllDirectories;
            }
            else if (arg.Key.Equals("newer", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("n", StringComparison.OrdinalIgnoreCase))
            {
                newer = true;
            }
            else if (arg.Key.Equals("purge", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                purge = true;
            }
            else if (arg.Key.Equals("continue", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("c", StringComparison.OrdinalIgnoreCase))
            {
                cont = true;
            }
            else if (arg.Key == "")
            {
                zip_path = arg.Value.FirstOrDefault();
                files_to_add = arg.Value.Skip(1);
            }
            else
            {
                Console.Error.WriteLine(@"Syntax:
zipio add [-s] [--newer] [--purge] [--continue] file.zip [files ...]

Add files to zip archive.

-s
-r
--recurse   Recurse into subdirectories.

-n
--newer     Only files newer than existing files in archive, or files that do
            not already exist in archive.

-p
--purge     Remove existing files in archive that are not found on disk

-c
--continue  Ignore source file read errors
");

                return 0;
            }
        }

        if (zip_path is null || files_to_add is null)
        {
            Console.Error.WriteLine("Missing zip file path and source file names");
            return 0;
        }

        using var archive = ZipFile.Open(zip_path, ZipArchiveMode.Update);

        var existing_files = new List<string>();

        foreach (var file in files_to_add.SelectMany(path => ResolveWildcards(path, searchOption)))
        {
            try
            {
                var fileinfo = new FileInfo(file);

                using var source = fileinfo.OpenRead();

                var entry = archive
                    .Entries
                    .FirstOrDefault(e => e.FullName.Equals(file, StringComparison.CurrentCultureIgnoreCase));

                var lastWriteTimeUtc = fileinfo.LastWriteTimeUtc;

                if (entry is null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Adding: {file}");
                    Console.ResetColor();

                    entry = archive.CreateEntry(file, CompressionLevel.Optimal);

                    using var entryStream = entry.Open();
                    source.CopyTo(entryStream);
                    entryStream.SetLength(source.Length);

                    entry.LastWriteTime = lastWriteTimeUtc.ToLocalTime();
                }
                else if (!newer ||
                    (lastWriteTimeUtc - entry.LastWriteTime.UtcDateTime).TotalSeconds > 2)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Updating: {file}");
                    Console.ResetColor();

                    using var entryStream = entry.Open();
                    source.CopyTo(entryStream);
                    entryStream.SetLength(source.Length);

                    entry.LastWriteTime = lastWriteTimeUtc.ToLocalTime();
                }

                if (purge && entry is not null)
                {
                    existing_files.Add(entry.FullName);
                }
            }
            catch (Exception ex) when (cont)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Error.WriteLine($"File '{file}': {ex.JoinMessages()}");
                Console.ResetColor();
            }
        }

        if (purge)
        {
            var to_be_removed = archive.Entries
                .Where(entry => !existing_files.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var entry in to_be_removed)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"Removing '{entry.FullName}'");
                Console.ResetColor();
                entry.Delete();
            }
        }

        return 0;
    }

    public static int ZipDel(params string[] args)
        => ZipDel((IEnumerable<string>)args);

    public static int ZipDel(IEnumerable<string> cmdLine)
    {
        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        foreach (var arg in cmd)
        {
            if (arg.Key != "")
            {
                Console.WriteLine(@"Syntax:
zipio del zipfile file1 [file2 ...]

Deletes files from zip archive.
");

                return -1;
            }
        }

        if (!cmd.TryGetValue("", out var args) || args.Length < 2)
        {
            Console.WriteLine("Missing zip archive path or file names.");
            return 0;
        }

        using var archive = ZipFile.Open(args[0], ZipArchiveMode.Update);

        var entries = archive
            .Entries
            .Where(e => args.Skip(1).Any(a => Regex.IsMatch(e.FullName, a, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)))
            .ToArray();

        foreach (var entry in entries)
        {
            Console.WriteLine(entry.FullName);
            entry.Delete();
        }

        return 0;
    }

    public static int ZipFromDir(params string[] args)
        => ZipFromDir((IEnumerable<string>)args);

    public static int ZipFromDir(IEnumerable<string> cmdLine)
    {
        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        foreach (var arg in cmd)
        {
            if (arg.Key != "")
            {
                Console.Error.WriteLine(@"Syntax:
zipio fromdir zipfile directory

Create a zip archive from directory contents.
");

                return -1;
            }
        }

        if (!cmd.TryGetValue("", out var args) || args.Length != 2)
        {
            Console.WriteLine("Missing zip archive or directory path.");
            return 0;
        }

        ZipFile.CreateFromDirectory(args[1], args[0]);

        return 0;
    }

    public static int ZipToDir(params string[] args)
        => ZipToDir((IEnumerable<string>)args);

    public static int ZipToDir(IEnumerable<string> cmdLine)
    {
        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        var overwriteFiles = false;

        foreach (var arg in cmd)
        {
            if (arg.Key.Equals("o", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("overwrite", StringComparison.OrdinalIgnoreCase))
            {
                overwriteFiles = true;
            }
            else if (arg.Key != "")
            {
                Console.Error.WriteLine(@"Syntax:
zipio todir [--overwrite] zipfile directory

Extract contents of zip archive to a directory.

-o
--overwrite     Overwrite existing files
");

                return -1;
            }
        }

        if (!cmd.TryGetValue("", out var args) || args.Length != 2)
        {
            Console.WriteLine("Missing zip archive or directory path.");
            return 0;
        }

        if (overwriteFiles)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            ZipFile.ExtractToDirectory(args[0], args[1], overwriteFiles);
#else
            throw new PlatformNotSupportedException("Overwrite mode is not supported on .NET Framework 4.x or .NET Standard 2.0 or lower. Required: .NET Core or .NET 5.0 or later.");
#endif
        }
        else
        {
            ZipFile.ExtractToDirectory(args[0], args[1]);
        }

        return 0;
    }
}
