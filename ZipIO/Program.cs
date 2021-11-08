using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZipIO
{
    public static class Program
    {
        public static int Main(params string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.Error.WriteLine("Syntax:");
                Console.Error.WriteLine("ZipIO add|list|del|fromdir|todir|freshen|time [switches] zipfile [args ...]");
                return -1;
            }

            var sub_args = new List<string>(args.Skip(1));

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
            }

            Console.Error.WriteLine($"Unknown command: {args[0]}");
            return -1;
        }

        static int ZipList(IEnumerable<string> args)
        {
            var longlisting = false;
            if ("/L".Equals(args.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
            {
                args = args.Skip(1);
                longlisting = true;
            }
            
            foreach (var arg in args.SelectMany(path =>
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = ".";
                }
                return Directory.EnumerateFiles(dir, Path.GetFileName(path));
            }))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(arg);
                    
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
                    Console.Error.WriteLine($"{arg}: {ex.GetBaseException().Message}");
                }
            }

            return 0;
        }

        internal static IEnumerable<string> ResolveWildcards(string path, SearchOption searchOption)
        {
            var dir = Path.GetDirectoryName(path);
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

        public static int ZipAdd(IReadOnlyList<string> args)
        {
            try
            {
                string zip_path;
                IEnumerable<string> files_to_add;
                SearchOption searchOption;
                if (args[0].Equals("/S", StringComparison.OrdinalIgnoreCase))
                {
                    searchOption = SearchOption.AllDirectories;
                    zip_path = args[1];
                    files_to_add = args.Skip(2);
                }
                else
                {
                    searchOption = SearchOption.TopDirectoryOnly;
                    zip_path = args[0];
                    files_to_add = args.Skip(1);
                }

                using var archive = ZipFile.Open(zip_path, ZipArchiveMode.Update);
                
                foreach (var arg in files_to_add.SelectMany(path => ResolveWildcards(path, searchOption)))
                {
                    Console.WriteLine(arg);

                    using var source = File.OpenRead(arg);
                    var entry = archive
                        .Entries
                        .FirstOrDefault(e => e.FullName.Equals(arg, StringComparison.CurrentCultureIgnoreCase));

                    if (entry == null)
                    {
                        entry = archive.CreateEntry(arg, CompressionLevel.Optimal);
                    }

                    using var entryStream = entry.Open();
                    source.CopyTo(entryStream);
                    entryStream.SetLength(source.Length);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetBaseException().Message);
            }

            return 0;
        }

        public static int ZipDel(IReadOnlyList<string> args)
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetBaseException().Message);
            }

            return 0;
        }

        public static int ZipFromDir(IReadOnlyList<string> args)
        {
            if (args.Count != 2)
            {
                Console.Error.WriteLine("Syntax:");
                Console.Error.WriteLine("ZipIO fromdir zipfile directory");
                return -1;
            }

            try
            {
                ZipFile.CreateFromDirectory(args[1], args[0]);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetBaseException().Message);
            }

            return 0;
        }

        public static int ZipToDir(IReadOnlyList<string> args)
        {
            if (args.Count != 2)
            {
                Console.Error.WriteLine("Syntax:");
                Console.Error.WriteLine("ZipIO todir zipfile directory");
                return -1;
            }

            try
            {
                ZipFile.ExtractToDirectory(args[0], args[1]);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetBaseException().Message);
            }

            return 0;
        }

        internal static string JoinMessages(this Exception ex) =>
            string.Join(" -> ", ex.Enumerate().Select(e => e.Message));

        internal static IEnumerable<Exception> Enumerate(this Exception ex)
        {
            while (ex != null)
            {
                yield return ex;
                ex = ex.InnerException;
            }
        }
    }
}
