using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ReplaceText;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0056 // Use index operator

public static class Program
{
    public static int Main(params string[] args)
    {
        var searchOption = SearchOption.TopDirectoryOnly;

        if (args is not null && args.Length >= 1 && args[0] == "-r")
        {
            searchOption = SearchOption.AllDirectories;
            
            args = [.. args.Skip(1)];
        }

        if (args is null || args.Length < 2 ||
            ((args.Length & 1) == 0))
        {
            Console.Error.WriteLine(@"Syntax:

ReplaceText [-r] ""fromtext"" ""totext"" [""fromtext2"" ""totext2"" ...] filepattern");

            return -1;
        }

        var dict = new List<KeyValuePair<string, string>>();

        for (int i = 0; i <= args.Length - 3; i += 2)
        {
            dict.Add(new(args[i], args[i + 1]));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var namepattern = args[args.Length - 1];

        IEnumerable<FileInfo> files;

        try
        {
            var dir = Path.GetDirectoryName(namepattern);
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = ".";
            }
            
            var dirinfo = new DirectoryInfo(dir);

            files = dirinfo.EnumerateFiles(Path.GetFileName(namepattern), searchOption);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{namepattern}: {ex.GetBaseException().Message}");
            return -1;
        }

        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file.FullName, encoding);

                var newtext = text;

                foreach (var item in dict)
                {
                    newtext = newtext.Replace(item.Key, item.Value);
                }

                if (!ReferenceEquals(newtext, text))
                {
                    Console.WriteLine($"Modifying {file.FullName}");

                    using var outstream = new StreamWriter(new FileStream(
                        file.FullName, FileMode.Open, FileAccess.Write, FileShare.Delete), encoding);
                    outstream.Write(newtext);
                    outstream.Flush();
                    outstream.BaseStream.SetLength(outstream.BaseStream.Position);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{file}: {ex.GetBaseException().Message}");
            }
        }

        return 0;
    }
}
