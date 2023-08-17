using System;
using System.Collections.Generic;
using System.IO;
#if NET35_OR_GREATER || NETSTANDARD || NETCOREAPP
using System.Linq;
#endif
using System.Reflection;
#if NET461_OR_GREATER || NETSTANDARD || NETCOREAPP
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
#endif
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace netcheck;

public static class Program
{
    public static int Main(params string[] args)
    {
        try
        {
            return UnsafeMain(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Exception: {ex}");
            Console.ResetColor();
            return Marshal.GetHRForException(ex);
        }
    }

    public static int UnsafeMain(params string[] args)
    {
        var errors = 0;
        var nodep = false;
        var depforall = false;
        foreach (var arg in args)
        {
            if (arg == "-l")
            {
                nodep = true;
                continue;
            }

            if (arg == "-a")
            {
                depforall = true;
                continue;
            }

            try
            {
                var path = Path.GetFullPath(arg);
                var asmname = AssemblyName.GetAssemblyName(path);
                DisplayDependencies(new(), Path.GetDirectoryName(path), asmname, "", nodep, depforall);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.Write(ex.ToString());
                Console.Error.Write(": ");
                Console.Error.WriteLine(arg);
                Console.ResetColor();
                errors += 1;
            }
        }

        return errors;
    }

    public static void DisplayDependencies(List<AssemblyName> asmlist, string? basepath, AssemblyName asmname, string indentlevel, bool nodep, bool depforall)
    {
        var existing = asmlist.Find(name => AssemblyName.ReferenceMatchesDefinition(name, asmname)) is not null;

        if (!depforall && existing)
        {
            return;
        }

        if (!existing)
        {
            asmlist.Add(asmname);
        }

        Assembly asm;
        try
        {
            if (asmname.CodeBase is not null)
            {
                asm = Assembly.LoadFrom(asmname.CodeBase);
            }
            else
            {
                asm = Assembly.Load(asmname);
            }
        }
        catch
        {
            try
            {
                var dllName = $"{asmname.Name}.dll";

                var path = Path.Combine(basepath ?? ".", dllName);
                
                if (!File.Exists(path))
                {
                    path = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), dllName);
                }

                asm = Assembly.LoadFrom(path);
                asmname = asm.GetName();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"Error loading {asmname}: {ex.GetBaseException().Message}");
                Console.ResetColor();
                return;
            }
        }

#if !NETCOREAPP

        if (existing && asm.GlobalAssemblyCache)
        {
            return;
        }

        Console.Write(indentlevel);

        if (asm.GlobalAssemblyCache)
        {
            Console.ForegroundColor = ConsoleColor.Green;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{asm.Location}: ");
        }

#else

        Console.Write(indentlevel);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{asm.Location}: ");

#endif

        Console.Write(asmname.FullName);

#if NET461_OR_GREATER || NETSTANDARD || NETCOREAPP

        try
        {
            using var reader = new PEReader(asm.GetFiles()[0]);

            var metadataversion = reader.GetMetadataReader().MetadataVersion;
            var target_framework = asm.GetCustomAttributes<TargetFrameworkAttribute>().FirstOrDefault()?.FrameworkName;
            var framework = GetTargetFramework(metadataversion, target_framework);
            if (!string.IsNullOrWhiteSpace(framework))
            {
                Console.Write(", ");
                Console.Write(framework);
            }
        }
        catch
        {
        }
#endif

        Console.ResetColor();
        Console.WriteLine();
        if (nodep || existing)
        {
            return;
        }

        var subindentlevel = $"{indentlevel} ";

        foreach (var refasm in asm.GetReferencedAssemblies())
        {
            DisplayDependencies(asmlist, basepath, refasm, subindentlevel, nodep, depforall);
        }
    }

#if NET461_OR_GREATER || NETSTANDARD || NETCOREAPP
    private static string? GetTargetFramework(string metadataversion, string? target_framework)
    {
        if (string.IsNullOrWhiteSpace(metadataversion))
        {
            return null;
        }

        if (target_framework is null
            || string.IsNullOrWhiteSpace(target_framework))
        {
            var netfx = metadataversion.Split(new[] { 'v', '.' });
            return $"net{netfx[1]}{netfx[2]}";
        }

        if (target_framework.StartsWith(".NETFramework,Version=v", StringComparison.Ordinal))
        {
            return $"net{target_framework.Substring(".NETFramework,Version=v".Length).Replace(".", "")}";
        }

        var sep = target_framework.IndexOf(",Version=v", StringComparison.Ordinal);
        var fx = target_framework.Remove(sep).TrimStart('.').ToLowerInvariant();
        var ver = target_framework.Substring(sep + ",Version=v".Length);
        if (fx == "netcoreapp" && ver[0] >= '5')
        {
            fx = "net";
        }

        return $"{fx}{ver}";
    }
#endif
}
