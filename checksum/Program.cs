using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LTRData.Extensions.CommandLine;

namespace checksum;

public static class Program
{
    public static void Main(params string[] args)
    {
        try
        {
            UnsafeMain(args);
        }
        catch (Exception? ex)
        {
#if DEBUG
            Trace.WriteLine(ex.ToString());
#endif

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Exception:");
            while (ex is not null)
            {
                Console.WriteLine(ex.Message);
                ex = ex.InnerException;
            }

            Console.ResetColor();
        }
    }

    public static void UnsafeMain(params string[] cmdLine)
    {
        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.OrdinalIgnoreCase);

        var alg = "md5";
        string? key = null;
        var search_option = SearchOption.TopDirectoryOnly;
        var output_code = false;
        var value = false;

        foreach (var arg in cmd)
        {
            if (arg.Key.Equals("x", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var asmfile in arg.Value)
                {
                    var asmname = AssemblyName.GetAssemblyName(asmfile);
                    Assembly.Load(asmname);
                }
            }
            else if (arg.Key.Equals("l", StringComparison.OrdinalIgnoreCase))
            {
                ListHashProviders();
            }
            else if (arg.Key.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                alg = arg.Value.Single();
            }
            else if (arg.Key.Equals("k", StringComparison.OrdinalIgnoreCase))
            {
                key = arg.Value.SingleOrDefault();
            }
            else if (arg.Key.Equals("s", StringComparison.OrdinalIgnoreCase))
            {
                search_option = SearchOption.AllDirectories;
            }
            else if (arg.Key.Equals("c", StringComparison.OrdinalIgnoreCase))
            {
                output_code = true;
            }
            else if (arg.Key.Equals("v", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                foreach (var valuestr in arg.Value)
                {
                    var valuebytes = Encoding.UTF8.GetBytes(valuestr);
                    PrintCheckSumForData(alg, key, output_code, valuebytes);
                }
            }
            else if (arg.Key == "")
            {
                if (value)
                {
                    foreach (var valuestr in arg.Value)
                    {
                        var valuebytes = Encoding.UTF8.GetBytes(valuestr);
                        PrintCheckSumForData(alg, key, output_code, valuebytes);
                    }
                }
                else
                {
                    var files = arg.Value;

                    if (files.Length == 0)
                    {
                        files = ["-"];
                    }

                    if (files.Length == 1)
                    {
                        PrintCheckSumForFiles(alg, key, files[0], output_code, search_option);
                    }
                    else
                    {
                        Parallel.ForEach(files,
                            file => PrintCheckSumForFiles(alg, key, file, output_code, search_option));
                    }
                }
            }
            else
            {
                Console.WriteLine(@"Generic .NET checksum calculation tool.
Copyright (c) 2012-2024, Olof Lagerkvist, LTR Data.
http://ltr-data.se/opencode.html

Syntax for calculating hash of file data:
checksum [-x:assembly] [-s] [-a:algorithm] [-k:key] file1 [file2 ...]

Syntax for calculating hash of UTF8 bytes of a string:
checksum [-x:assembly] [-s] [-a:algorithm] [-k:key] -v:string

List available hash algorithms:
checksum [-x:assembly] -l

-x      Specify name and path to assembly file to search for hash algorithms.

-s      Search subdirectories for files to hash.

-a      Specifies algorithm. Can be any .NET supported hashing algorithm, such
        as MD5, SHA1 or RIPEMD160.

-k      For HMAC shared-key hash providers, specifies secret key for checksum.

-c      Output in C/C++/C# code format.

-l      Lists available hash algorithms.
");

                return;
            }
        }
    }

    public static void ListHashProviders()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

#if NETCOREAPP
        if (Array.IndexOf(assemblies, typeof(SHA256).Assembly) < 0)
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies();
        }

        if (Array.IndexOf(assemblies, typeof(MD5).Assembly) < 0)
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies();
        }

        if (Array.IndexOf(assemblies, typeof(TripleDES).Assembly) < 0)
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies();
        }
#endif

        var List = new List<string>();

        foreach (var Assembly in assemblies)
        {
            foreach (var Type in Assembly.GetTypes())
            {
                if (Type.IsClass
                    && !Type.IsAbstract && typeof(HashAlgorithm).IsAssignableFrom(Type)
                    && Type.GetConstructor(System.Type.EmptyTypes) is not null)
                {
                    var name = Type.Name;
                    if (name == "Implementation" && Type.DeclaringType is not null)
                    {
                        name = Type.DeclaringType.Name;
                    }

                    foreach (var suffix in new[] { "CryptoServiceProvider", "Managed", "Cng" })
                    {
                        if (name.EndsWith(suffix))
                        {
                            name = name.Remove(name.Length - suffix.Length);
                            break;
                        }
                    }

                    if (!List.Contains(name))
                    {
                        List.Add(name);
                    }
                }
            }
        }

        List.ForEach(Console.WriteLine);
    }

    public static HashAlgorithm? CreateHashProvider(string alg)
    {
        var algorithm = HashAlgorithm.Create(alg);

        if (algorithm is not null)
        {
            return algorithm;
        }

        Type? algType = null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsClass
                    && !type.IsAbstract
                    && typeof(HashAlgorithm).IsAssignableFrom(type)
                    && type.GetConstructor(System.Type.EmptyTypes) is not null
                    && (type.Name.Equals(alg, StringComparison.OrdinalIgnoreCase)
                    || type.Name.Equals($"{alg}CryptoServiceProvider", StringComparison.OrdinalIgnoreCase)
                    || type.Name.Equals($"{alg}Managed", StringComparison.OrdinalIgnoreCase)
                    || type.Name.Equals($"{alg}Cng", StringComparison.OrdinalIgnoreCase)))
                {
                    algType = type;
                    break;
                }
            }

            if (algType is not null)
            {
                break;
            }
        }

        if (algType is null)
        {
            return null;
        }

        algorithm = (HashAlgorithm?)Activator.CreateInstance(algType);

        return algorithm;
    }

    public static void PrintCheckSumForFiles(string alg, string? key, string filename_pattern, bool output_code, SearchOption search_option)
    {
        try
        {
            if (string.IsNullOrEmpty(filename_pattern) || "-" == filename_pattern)
            {
                PrintCheckSumForFile(alg, key, "-", output_code);
            }
            else
            {
                var dir = Path.GetDirectoryName(filename_pattern);
                var filepart = Path.GetFileName(filename_pattern);

                if (string.IsNullOrEmpty(dir))
                {
                    dir = ".";
                }

                var found = false;

                foreach (var file in Directory.GetFiles(dir, filepart, search_option))
                {
                    var filename = file;
                    found = true;
                    if (filename.StartsWith(@".\", StringComparison.Ordinal))
                    {
                        filename = filename.Substring(2);
                    }

                    PrintCheckSumForFile(alg, key, filename, output_code);
                }

                if (!found)
                {
                    Console.Error.WriteLine($"File '{filename_pattern}' not found");
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"{filename_pattern}: {ex.Message}");
            Console.ResetColor();
        }
    }

    public static void PrintCheckSumForFile(string alg, string? key, string filename, bool output_code)
    {
        using var algorithm = CreateHashProvider(alg);

        if (algorithm is null)
        {
            Console.WriteLine($"Hash algorithm '{alg}' not supported.");
            return;
        }

        if (algorithm is KeyedHashAlgorithm keyedAlgorithm)
        {
            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine($"Hash algorithm '{alg}' requires key.");
                return;
            }
            
            keyedAlgorithm.Key = Encoding.UTF8.GetBytes(key);
        }
        else if (!string.IsNullOrEmpty(key))
        {
            Console.WriteLine($"Hash algorithm '{alg}' does not support keyed hashing.");
            return;
        }

        var buffersize = algorithm.HashSize * 8192;

        byte[] hash;

        try
        {
            using var fs = OpenFile(filename, buffersize);
            hash = algorithm.ComputeHash(fs);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error opening or reading file '{filename}': {ex.Message}");
            Console.ResetColor();
            return;
        }

        PrintChecksum(hash, filename, output_code);

    }

    private static Stream OpenFile(string filename, int buffersize)
    {
        if ("-" == filename)
        {
            return Console.OpenStandardInput(buffersize);
        }
        else
        {
            return new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, buffersize, FileOptions.SequentialScan);
        }
    }

    public static void PrintCheckSumForData(string alg, string? key, bool output_code, byte[] data)
    {
        using var algorithm = CreateHashProvider(alg);

        if (algorithm is null)
        {
            Console.WriteLine($"Hash algorithm '{alg}' not supported.");
            return;
        }

        if (algorithm is KeyedHashAlgorithm keyedAlgorithm)
        {
            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine($"Hash algorithm '{alg}' requires key.");
                return;
            }

            keyedAlgorithm.Key = Encoding.UTF8.GetBytes(key);
        }
        else if (!string.IsNullOrEmpty(key))
        {
            Console.WriteLine($"Hash algorithm '{alg}' does not support keyed hashing.");
            return;
        }

        var buffersize = algorithm.HashSize * 8192;

        byte[] hash;

        hash = algorithm.ComputeHash(data);

        PrintChecksum(hash, "", output_code);
    }

    public static void PrintChecksum(byte[] hash, string filename, bool output_code)
    {
        StringBuilder sb;

        if (output_code)
        {
            sb = new StringBuilder(hash.Length * 6 + 10 + filename.Length);

            sb.Append("{ ");

            sb.Append(string.Join(", ", Array.ConvertAll(hash, b => $"0x{b:x2}")));

            sb.Append(" };  // ").Append(filename);
        }
        else
        {
            sb = new StringBuilder(hash.Length * 2 + 2 + filename.Length);

            Array.ForEach(hash, b => sb.Append(b.ToString("x2")));

            sb.Append(" *").Append(filename);
        }

        Console.WriteLine(sb.ToString());
    }
}
