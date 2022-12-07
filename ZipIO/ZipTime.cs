using LTRLib.LTRGeneric;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ZipIO;

public static class ZipTime
{
    private readonly record struct Entry(string FileName, DateTime? LastWriteTime, Exception? Exception);

    public static int Time(params string[] args)
        => Time((IEnumerable<string>)args);

    public static int Time(IEnumerable<string> cmdLine)
    {
        var cmd = StringSupport.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        var modify_file_timestamps = false;
        var search_options = SearchOption.TopDirectoryOnly;

        string[]? files = null;
        
        foreach (var arg in cmd)
        {
            if (arg.Key.Equals("s", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("r", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("recurse", StringComparison.OrdinalIgnoreCase))
            {
                search_options = SearchOption.AllDirectories;
            }
            else if (arg.Key.Equals("m", StringComparison.OrdinalIgnoreCase)
                || arg.Key.Equals("modify", StringComparison.OrdinalIgnoreCase))
            {
                modify_file_timestamps = true;
            }
            else if (arg.Key == "")
            {
                files = arg.Value;
            }
            else
            {
                Console.WriteLine(@"Syntax:
zipio time [-s] [--modify] file1 [file2]

Modifies timestamp on zip archive file to match newest file within archive.

-s
-r
--recurse   Search subdirectories

-m
--modify    Modify - Set timestamp of zip files to newest timestamp of entries within
            the zip file
");
                return -1;
            }
        }

        if (files is null || files.Length == 0)
        {
            Console.Error.WriteLine("Missing zip file paths");
            return 0;
        }

        var query = files
            .SelectMany(arg =>
            {
                var directoryName = Path.GetDirectoryName(arg);
                if (string.IsNullOrWhiteSpace(directoryName))
                {
                    directoryName = ".";
                }

                var directory = new DirectoryInfo(directoryName);
                return directory.EnumerateFiles(Path.GetFileName(arg), search_options);
            })
            .AsParallel()
            .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
            .WithDegreeOfParallelism(Math.Min(Environment.ProcessorCount * 2, 64))
            .Select(file =>
            {
                if (".zip".Equals(file.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        DateTimeOffset newestFileTime;

                        using (var zip = new ZipArchive(file.OpenRead(), ZipArchiveMode.Read))
                        {
                            newestFileTime = zip.Entries.Max(entry => entry.LastWriteTime);
                        }

                        if (modify_file_timestamps)
                        {
                            file.LastWriteTimeUtc = newestFileTime.UtcDateTime;
                        }
                        else
                        {
                            return new Entry
                            {
                                FileName = file.FullName,
                                LastWriteTime = newestFileTime.LocalDateTime
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        return new Entry
                        {
                            FileName = file.FullName,
                            Exception = ex
                        };
                    }
                }

                return new Entry
                {
                    FileName = file.FullName,
                    LastWriteTime = file.LastWriteTime
                };
            })
            .OrderByDescending(entry => entry.LastWriteTime);

        foreach (var entry in query)
        {
            Console.Write(entry.FileName);
            Console.Write(" - ");
            if (entry.Exception is not null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(entry.Exception.GetBaseException().Message);
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine(entry.LastWriteTime.ToString());
            }
        }

        return 0;
    }
}