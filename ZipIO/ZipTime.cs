using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ZipIO
{
    public static class ZipTime
    {
        private struct Entry
        {
            public string FileName;
            public DateTime? LastWriteTime;
            public Exception Exception;
        }

        public static int Time(IEnumerable<string> args)
        {
            var modify_file_timestamps = false;
            var search_options = SearchOption.TopDirectoryOnly;

            if (args is null ||
                !args.Any())
            {
                args = new[] { "/?" };
            }

            var files = new List<string>();
            foreach (var arg in args)
                if (arg.Equals("/S", StringComparison.OrdinalIgnoreCase))
                    search_options = SearchOption.AllDirectories;
                else if (arg.Equals("/M", StringComparison.OrdinalIgnoreCase))
                    modify_file_timestamps = true;
                else if (arg.StartsWith("/", StringComparison.Ordinal))
                {
                    Console.WriteLine("Syntax:");
                    Console.WriteLine("ZipIO time [/S] [/M] file1 [file2]");
                    Console.WriteLine();
                    Console.WriteLine("/S   Search subdirectories.");
                    Console.WriteLine("/M   Modify - Set timestamp of zip files to newest timestamp of entries within");
                    Console.WriteLine("     the zip file.");
                    return -1;
                }
                else
                    files.Add(arg);

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
                    Console.WriteLine(entry.Exception.GetBaseException().Message);
                }
                else
                {
                    Console.WriteLine(entry.LastWriteTime.ToString());
                }
            }

            return 0;
        }
    }
}
