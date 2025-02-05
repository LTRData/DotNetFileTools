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
        string[]? files = null;
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
            else if (cmd.Key == "")
            {
                files = cmd.Value;
            }
            else
            {
                showhelp = true;
            }
        }

        if (showhelp || (from is null && to is null && files is null))
        {
            Console.Error.WriteLine(@$"Text encoding conversion tool
Copyright (c) 2024 - 2025 Olof Lagerkvist, LTR Data - https://ltr-data.se

Syntax:
txtcnv [-d:from] [-e:to] [files ...]

Arguments from and to can be either numeric codepages or named text encodings.
Default encoding is utf8 if not specified.

List of encodings currently supported:
Codepage {"Name",-24} Description");

            foreach (var encoding in Encoding.GetEncodings().OrderBy(enc => enc.CodePage))
            {
                Console.Error.WriteLine($"{encoding.CodePage,-8} {encoding.Name,-24} {encoding.DisplayName}");
            }

            return 100;
        }

        from ??= Encoding.UTF8;
        to ??= Encoding.UTF8;

        using var destination = Console.OpenStandardOutput();

        if (files is null || files.Length == 0)
        {
            files = ["-"];
        }

        foreach (var file in files)
        {
            using var source = file == "-"
                ? Console.OpenStandardInput()
                : File.OpenRead(file);

            Encoding
                .CreateTranscodingStream(source, from, to)
                .CopyTo(destination);
        }

        return 0;
    }
}
