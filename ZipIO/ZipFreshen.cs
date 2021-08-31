using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ZipIO
{
    public static class ZipFreshen
    {
        [MethodImpl(MethodImplOptions.Synchronized)]
        static void WriteConsole(TextWriter writer, string message)
        {
            if (ReferenceEquals(writer, Console.Error))
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }

            writer.WriteLine(message);

            if (ReferenceEquals(writer, Console.Error))
            {
                Console.ResetColor();
            }
        }

        public enum FreshenOrReplaceOperation
        {
            Freshen,
            Replace
        }

        public static int FreshenOrReplace(FreshenOrReplaceOperation operation, IReadOnlyList<string> args)
        {
            var source_directory = string.Empty;

            if (args == null || args.Count == 0)
            {
                args = new[] { "/?" };
            }

            var files = new List<string>();

            foreach (var arg in args)
            {
                if (arg.StartsWith("/SOURCE=", StringComparison.OrdinalIgnoreCase))
                {
                    source_directory = arg.Substring("/SOURCE=".Length);
                }
                else if (arg.StartsWith("/", StringComparison.Ordinal))
                {
                    Console.WriteLine("Syntax:");
                    Console.WriteLine("ZipIO freshen /SOURCE=sourcedirectory [zipfile1 [zipfile2 ...]]");
                    return -1;
                }
                else
                {
                    files.Add(arg);
                }
            }

            try
            {
                files
                    .SelectMany(arg =>
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
                            WriteConsole(Console.Error,
                                string.Concat(
                                arg,
                                ": ",
                                ex.GetBaseException().Message));

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
                            using var zip = new ZipArchive(file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.Delete), ZipArchiveMode.Update);

                            foreach (var entry in zip.Entries
                                .Select(zipEntry =>
                                {
                                    var zipEntryFullName = zipEntry.FullName;
                                    var sourcePath = Path.Combine(source_directory, zipEntryFullName);

                                    return new
                                    {
                                        zipEntry,
                                        zipEntryFullName,
                                        sourcePath,
                                        sourceTimeStamp = File.GetLastWriteTimeUtc(sourcePath)
                                    };
                                })
                                .Where(entry =>
                                    !entry.zipEntryFullName.EndsWith("/") &&
                                    !entry.zipEntryFullName.EndsWith("\\") &&
                                    (operation == FreshenOrReplaceOperation.Replace ||
                                    (entry.sourceTimeStamp -
                                    entry.zipEntry.LastWriteTime.UtcDateTime).TotalSeconds > 2)))
                            {
                                if (!File.Exists(entry.sourcePath))
                                {
                                    WriteConsole(Console.Error, $"Cannot find '{Path.Combine(file.FullName, entry.zipEntryFullName)}'");
                                }

                                WriteConsole(Console.Out, Path.Combine(file.FullName, entry.zipEntryFullName));

                                try
                                {
                                    using var fileStream = File.OpenRead(entry.sourcePath);
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
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            WriteConsole(Console.Error,
                                string.Concat(
                                file.FullName,
                                ": ",
                                ex.ToString()));
#else
                            WriteConsole(Console.Error,
                                string.Concat(
                                file.FullName,
                                ": ",
                                ex.JoinMessages()));
#endif
                        }

                        if (!modified)
                        {
                            file.LastWriteTimeUtc = oldZipTimeStamp;
                        }
                    });

                return 0;
            }
            catch (Exception ex)
            {
                WriteConsole(Console.Error, ex.GetBaseException().Message);
                return 1;
            }
        }
    }
}
