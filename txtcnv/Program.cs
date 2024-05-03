using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System.Text;

namespace txtcnv;

public static class Program
{
    static Program()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static int Main(params string[] args)
    {
        try
        {
            return UnsafeMain(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(ex.JoinMessages());
            Console.ResetColor();
            return ex.HResult;
        }
    }

    public static int UnsafeMain(params string[] args)
    {
        var cmds = CommandLineParser.ParseCommandLine(args, StringComparer.Ordinal);

        Encoding? from = Encoding.UTF8;
        Encoding? to = Encoding.UTF8;
        var showhelp = false;

        foreach (var cmd in cmds)
        {
            if (cmd.Key is "d" or "decode" && cmd.Value.Length == 1)
            {
                if (int.TryParse(cmd.Value[0], out var codepage))
                {
                    from = Encoding.GetEncoding(codepage);
                }
                else
                {
                    from = Encoding.GetEncoding(cmd.Value[0]);
                }
            }
            else if (cmd.Key is "e" or "encode" && cmd.Value.Length == 1)
            {
                if (int.TryParse(cmd.Value[0], out var codepage))
                {
                    to = Encoding.GetEncoding(codepage);
                }
                else
                {
                    to = Encoding.GetEncoding(cmd.Value[0]);
                }
            }
            else
            {
                showhelp = true;
            }
        }

        if (showhelp || from is null || to is null)
        {
            Console.Error.WriteLine(@"Syntax:
txtcnv [-d:from] [-e:to] [files ...]

Arguments from and to can be either numeric codepages or named text encodings.
Default encoding is utf8 if not specified.

Supported encodings:
");

            foreach (var encoding in Encoding.GetEncodings().OrderBy(enc => enc.CodePage))
            {
                Console.Error.WriteLine($"{encoding.CodePage,-5}  {encoding.Name,-23}  {encoding.DisplayName}");
            }

            return 100;
        }

        using var destination = Console.OpenStandardOutput();

        if (!cmds.TryGetValue("", out var files) || files.Length == 0)
        {
            Encoding
                .CreateTranscodingStream(Console.OpenStandardInput(), from, to)
                .CopyTo(destination);

            return 0;
        }

        foreach (var file in files)
        {
            Encoding
                .CreateTranscodingStream(File.OpenRead(file), from, to)
                .CopyTo(destination);
        }

        return 0;
    }
}
