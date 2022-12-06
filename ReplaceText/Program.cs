using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReplaceText;

public static class Program
{
    public static int Main(params string[] args)
    {
        if (args is null || args.Length < 2 ||
            ((args.Length & 1) == 0))
        {
            Console.Error.WriteLine(@"Syntax:

ReplaceText ""fromtext"" ""totext"" [file1 [ ... ]]");

            return -1;
        }

        var dict = new List<KeyValuePair<string, string>>();

        for (int i = 0; i <= args.Length - 3; i += 2)
        {
            dict.Add(new(args[i], args[i + 1]));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier:false);

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

            files = dirinfo.EnumerateFiles(Path.GetFileName(namepattern));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{namepattern}: {ex.GetBaseException().Message}");
            return -1;
        }

        foreach (var file in files)
        {
            Console.WriteLine($"Processing file {file}");

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
                    Console.WriteLine($"{file.FullName} matches.");

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
            
            Console.WriteLine();
        }

        return 0;
    }
}
