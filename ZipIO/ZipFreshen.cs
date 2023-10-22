using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0057 // Use range operator

namespace ZipIO;

public static class ZipFreshen
{
    [MethodImpl(MethodImplOptions.Synchronized)]
    static void WriteConsole(TextWriter writer, ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        writer.WriteLine(message);
        Console.ResetColor();
    }

    public enum FreshenOrReplaceOperation
    {
        Freshen,
        Replace
    }

    public static int FreshenOrReplace(FreshenOrReplaceOperation operation, params string[] args)
        => FreshenOrReplace(operation, (IEnumerable<string>)args);

    public static int FreshenOrReplace(FreshenOrReplaceOperation operation, IEnumerable<string> cmdLine)
    {
        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        string[]? files = null;
        var source_directory = string.Empty;
        var purge = false;

        foreach (var arg in cmd)
        {
            if (arg.Key.Equals("source", StringComparison.OrdinalIgnoreCase))
            {
                source_directory = arg.Value.FirstOrDefault();
            }
            else if (arg.Key.Equals("purge", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                purge = true;
            }
            else if (arg.Key == "")
            {
                files = arg.Value;
            }
            else
            {
                Console.WriteLine(@"Syntax:
zipio freshen --source=sourcedirectory [--purge] [zipfile1 [zipfile2 ...]]

Updates existing files in archive with newer files found on disk in directory
specified in --source switch. If no files are updated in an archive, the last
write time of the zip file is reset to original timestamp.

-p
--purge     Remove files in archive that are not found on disk.
");
                return -1;
            }
        }

        if (files is null || files.Length == 0)
        {
            Console.Error.WriteLine("Missing zip file paths");
            return 0;
        }

        files.SelectMany(arg =>
        {
            try
            {
                var dirname = Path.GetDirectoryName(arg);
                if (string.IsNullOrWhiteSpace(dirname))
                {
                    dirname = ".";
                }

                var directory = new DirectoryInfo(dirname);
                return directory.EnumerateFiles(Path.GetFileName(arg), SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                WriteConsole(Console.Error, ConsoleColor.Red, $"{arg}: {ex.GetBaseException().Message}");

                return Enumerable.Empty<FileInfo>();
            }
        })
        .AsParallel()
        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
        .WithDegreeOfParallelism(Math.Min(Environment.ProcessorCount * 2, 64))
        .ForAll(file =>
        {
            var modified = false;
            var oldZipTimeStamp = file.LastWriteTimeUtc;

            try
            {
                var to_be_removed = new List<ZipArchiveEntry>();

                using var zip = new ZipArchive(file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.Delete), ZipArchiveMode.Update);

                foreach (var entry in zip.Entries
                    .Select(zipEntry =>
                    {
                        var zipEntryFullName = zipEntry.FullName;
                        var sourcePath = new FileInfo(Path.Combine(source_directory, zipEntryFullName));

                        return new
                        {
                            zipEntry,
                            zipEntryFullName,
                            sourcePath,
                            sourceTimeStamp = sourcePath.LastWriteTimeUtc
                        };
                    })
                    .Where(entry =>
                        !entry.zipEntryFullName.EndsWith("/") &&
                        !entry.zipEntryFullName.EndsWith("\\") &&
                        (operation == FreshenOrReplaceOperation.Replace ||
                        (entry.sourceTimeStamp - entry.zipEntry.LastWriteTime.UtcDateTime).TotalSeconds > 2)))
                {
                    if (!entry.sourcePath.Exists)
                    {
                        if (purge)
                        {
                            to_be_removed.Add(entry.zipEntry);
                        }
                        else
                        {
                            WriteConsole(Console.Error, ConsoleColor.Red, $"Cannot find '{Path.Combine(file.FullName, entry.zipEntryFullName)}'");
                        }

                        continue;
                    }

                    WriteConsole(Console.Out, ConsoleColor.Cyan, Path.Combine(file.FullName, entry.zipEntryFullName));

                    try
                    {
                        using var fileStream = entry.sourcePath.OpenRead();
                        using var zipStream = entry.zipEntry.Open();
                        fileStream.CopyTo(zipStream);
                        zipStream.SetLength(zipStream.Position);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed refreshing file '{entry.sourcePath}'", ex);
                    }

                    entry.zipEntry.LastWriteTime = entry.sourceTimeStamp.ToLocalTime();

                    modified = true;
                }

                foreach (var entry in to_be_removed)
                {
                    WriteConsole(Console.Out, ConsoleColor.Magenta, $"Removing: {Path.Combine(file.FullName, entry.FullName)}");
                    entry.Delete();
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                WriteConsole(Console.Error, ConsoleColor.Red, $"{file.FullName}: {ex}");
#else
                WriteConsole(Console.Error, ConsoleColor.Red, $"{file.FullName}: {ex.JoinMessages()}");
#endif
            }

            if (!modified)
            {
                file.LastWriteTimeUtc = oldZipTimeStamp;
            }
        });

        return 0;
    }
}
