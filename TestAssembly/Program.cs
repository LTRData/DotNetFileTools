using LTRData.Extensions.CommandLine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TestAssembly;

public static class Program
{
    private static void WriteConstantValue(this Action<string> writer, FieldInfo field)
    {
        var value = field.GetRawConstantValue();

        if (value is null)
        {
            writer("null");
        }
        else if (value is string str)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            writer($@"""{str.Replace(@"\", @"\\").Replace("'", @"\'")}""");
        }
        else if (field.FieldType.IsEnum &&
            field.FieldType != field.DeclaringType)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            writer("(");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            writer(field.FieldType.FormatTypeName());
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            writer(")");
            Console.ForegroundColor = ConsoleColor.Cyan;
            writer(Enum.ToObject(field.FieldType, value).ToString() ?? "null");
        }
        else if (value is IFormattable fmt)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            writer(fmt.ToString(format: null, CultureInfo.InvariantCulture));
        }
        else if (value.ToString() is { } str2)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            writer(str2);
        }
        else
        {
            writer("null");
        }
    }

    private static string FormatTypeName(this Type type)
    {
        var fullName = type.FullName;

        if (fullName is null
            || fullName.StartsWith("System.", StringComparison.Ordinal)
            || fullName.StartsWith("Microsoft.", StringComparison.Ordinal))
        {
            fullName = type.Name;
        }

        var name = new StringBuilder();

        var endpos = fullName.IndexOf('`');

        if (endpos >= 1)
        {
            name.Append(fullName, 0, endpos);
        }
        else
        {
            name.Append(fullName);
        }

        if (type.IsGenericType &&
            type.GetGenericArguments() is Type[] gentypes &&
            gentypes.Length > 0)
        {
            name.Append('<');
            name.Append(string.Join(", ", gentypes.Select(FormatTypeName)));
            name.Append('>');
        }

        return name.ToString();
    }

    public static int Main(params string[] cmdLine)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        var continueOnFailure = false;
        var searchOption = SearchOption.TopDirectoryOnly;
        var quiet = false;
        var result = 0;

        var cmd = CommandLineParser.ParseCommandLine(cmdLine, StringComparer.Ordinal);

        foreach (var arg in cmd)
        {
            if ("c" == arg.Key)
            {
                continueOnFailure = true;
            }
            else if ("r" == arg.Key)
            {
                searchOption = SearchOption.AllDirectories;
            }
            else if ("q" == arg.Key)
            {
                quiet = true;
            }
            else if (arg.Key != "")
            {
                Console.Error.WriteLine(@"Syntax:
testassembly [-c] [-r] [-q] file1 [file2 ...]

Prints out metadata information for classes and members of classes in a .NET
assembly. If any member cannot be resolved, the application exits with an
error code which makes it useful in scripts to verify assemblies after
build/merge/edit operations.

-c          Continue on errors

-r          Search subdirectories

-q          Quiet, no output but exits with zero or non-zero depending on
            success or failure

");

                return -1;
            }
        }

        if (!cmd.TryGetValue("", out var args) || args.Length == 0)
        {
            Console.Error.WriteLine("Missing file path.");
            return 0;
        }

        Action<string>? Write = null;
        Action<string>? WriteLine = null;

        if (!quiet)
        {
            Write = Console.Write;
            WriteLine = Console.WriteLine;
        }

        foreach (var arg in args.SelectMany(name =>
        {
            var dir = Path.GetDirectoryName(name);
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = ".";
            }

            return Directory.EnumerateFiles(dir, Path.GetFileName(name), searchOption);
        }))
        {
            var fullpath = Path.GetFullPath(arg);

            try
            {
                var asm = Assembly.LoadFrom(fullpath);

                fullpath = asm.Location;

                Console.ForegroundColor = ConsoleColor.Green;
                WriteLine?.Invoke(fullpath);
                Console.ResetColor();

                ListAssembly(Write, WriteLine, asm, continueOnFailure);

                continue;
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                foreach (var tlex in ex.LoaderExceptions)
                {
                    Console.Error.WriteLine($"{fullpath}: {tlex}");
                }

                Console.ResetColor();

                result = ex.LoaderExceptions.FirstOrDefault()?.HResult ?? ex.HResult;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"{fullpath}: {ex}");
                Console.ResetColor();

                result = ex.HResult;
            }

            if (result != 0 && !continueOnFailure)
            {
                return result;
            }
        }

        return result;
    }

    private static void ListAssembly(Action<string>? Write, Action<string>? WriteLine, Assembly asm, bool continueOnFailure)
    {
        foreach (var t in asm.GetTypes())
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Write?.Invoke("  ");

            if (t.IsPublic)
            {
                Write?.Invoke("public ");
            }

            if (!t.IsVisible)
            {
                Write?.Invoke("invisible ");
            }

            if (t.IsNestedAssembly)
            {
                Write?.Invoke("internal ");
            }

            if (t.IsNestedFamANDAssem)
            {
                Write?.Invoke("internal protected ");
            }

            if (t.IsNestedFamily)
            {
                Write?.Invoke("protected ");
            }

            if (t.IsNestedFamORAssem)
            {
                Write?.Invoke("protected internal ");
            }

            if (t.IsNestedPrivate)
            {
                Write?.Invoke("private ");
            }

            if (t.IsNestedPublic)
            {
                Write?.Invoke("public ");
            }

            if (t.IsAbstract && t.IsSealed)
            {
                Write?.Invoke("static ");
            }
            else if (t.IsAbstract && !t.IsInterface)
            {
                Write?.Invoke("abstract ");
            }
            else if (t.IsSealed && t.IsClass)
            {
                Write?.Invoke("sealed ");
            }

            if (t.IsArray)
            {
                Write?.Invoke("array ");
            }

            if (t.IsAutoClass)
            {
                Write?.Invoke("auto ");
            }

            if (t.IsByRef)
            {
                Write?.Invoke("byref ");
            }

            if (t.IsCOMObject)
            {
                Write?.Invoke("com ");
            }

            if (t.IsContextful)
            {
                Write?.Invoke("contextful ");
            }

            if (t.IsExplicitLayout)
            {
                Write?.Invoke("explicit ");
            }

            if (t.IsImport)
            {
                Write?.Invoke("import ");
            }

            if (t.IsInterface)
            {
                Write?.Invoke("interface ");
            }
            else if (t.IsClass)
            {
                Write?.Invoke("class ");
            }
            else if (t.IsEnum)
            {
                if (t.IsDefined(typeof(FlagsAttribute), inherit: false))
                {
                    Write?.Invoke("flags ");
                }

                Write?.Invoke("enum ");
            }
            else if (t.IsValueType)
            {
                Write?.Invoke("struct ");
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Write?.Invoke(t.FormatTypeName());
            Console.ForegroundColor = ConsoleColor.DarkYellow;

            var baseTypes = new List<Type>();

            if (t.BaseType is Type baseType &&
                baseType != typeof(object) &&
                baseType != typeof(ValueType))
            {
                if (t.IsEnum)
                {
                    baseTypes.Add(t.GetEnumUnderlyingType());
                }
                else
                {
                    baseTypes.Add(baseType);
                }
            }

            InterfaceMapping[]? interfaceMappings;

            if (!t.IsInterface &&
                t.GetInterfaces() is Type[] interfaces &&
                interfaces.Length > 0)
            {
                baseTypes.AddRange(interfaces);
                interfaceMappings = Array.ConvertAll(interfaces, t.GetInterfaceMap);
            }
            else
            {
                interfaceMappings = null;
            }

            if (baseTypes.Count > 0)
            {
                Write?.Invoke($" : {string.Join(", ", baseTypes.Select(bt => bt.FormatTypeName()))}");
            }

            WriteLine?.Invoke(" {");

            foreach (var m in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;

                Write?.Invoke("    ");
                
                if (m.IsAssembly)
                {
                    Write?.Invoke("internal ");
                }

                if (m.IsFamily)
                {
                    Write?.Invoke("protected ");
                }

                if (m.IsFamilyAndAssembly)
                {
                    Write?.Invoke("internal protected ");
                }

                if (m.IsFamilyOrAssembly)
                {
                    Write?.Invoke("protected internal ");
                }

                if (m.IsPrivate)
                {
                    Write?.Invoke("private ");
                }

                if (m.IsPublic)
                {
                    Write?.Invoke("public ");
                }

                if (m.IsLiteral)
                {
                    Write?.Invoke("const ");
                }
                else if (m.IsStatic)
                {
                    Write?.Invoke("static ");
                }

                if (m.IsInitOnly)
                {
                    Write?.Invoke("readonly ");
                }

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                    
                try
                {
                    Write?.Invoke(m.FieldType.FormatTypeName() + " ");
                }
                catch (Exception ex)
                when (continueOnFailure)
                {
                    Write?.Invoke("@@unknown ");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Write?.Invoke($"/* Failed to load type ({ex.Message}) */ ");
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;

                Console.ForegroundColor = ConsoleColor.Gray;
                Write?.Invoke(m.Name);

                if (m.IsLiteral)
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Write?.Invoke(" = ");
                    Write?.WriteConstantValue(m);
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;

                WriteLine?.Invoke(";");

                Console.ResetColor();
            }

            foreach (var m in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;

                Write?.Invoke("    ");

                if (m.IsAssembly)
                {
                    Write?.Invoke("internal ");
                }

                if (m.IsFamily)
                {
                    Write?.Invoke("protected ");
                }

                if (m.IsFamilyAndAssembly)
                {
                    Write?.Invoke("internal protected ");
                }

                if (m.IsFamilyOrAssembly)
                {
                    Write?.Invoke("protected internal ");
                }

                if (m.IsPrivate)
                {
                    Write?.Invoke("private ");
                }

                if (m.IsPublic)
                {
                    Write?.Invoke("public ");
                }

                if (m.IsStatic)
                {
                    Write?.Invoke("static ");
                }

                if (m.IsAbstract)
                {
                    Write?.Invoke("abstract ");
                }

                if (m.IsVirtual)
                {
                    Write?.Invoke("virtual ");
                }

                if (m.IsFinal)
                {
                    Write?.Invoke("final ");
                }

                Console.ForegroundColor = ConsoleColor.Gray;
                Write?.Invoke(m.Name);
                Console.ForegroundColor = ConsoleColor.DarkCyan;

                var param = m.GetParameters().Select(p =>
                {
                    var sb = new StringBuilder();
                    if (p.Position == 0 &&
                        p.Member.IsDefined(typeof(ExtensionAttribute), inherit: false))
                    {
                        sb.Append("this ");
                    }

                    if (p.IsRetval)
                    {
                        sb.Append("retval ");
                    }

                    if (p.IsIn)
                    {
                        sb.Append("in ");
                    }

                    if (p.IsOut)
                    {
                        sb.Append("out ");
                    }

                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    sb.Append(p.ParameterType.FormatTypeName());
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    sb.Append(' ');
                    sb.Append(p.Name);
                    if (p.HasDefaultValue)
                    {
                        sb.Append(" = ");
                        sb.Append(p.DefaultValue ?? "null");
                    }

                    return sb.ToString();
                });

                WriteLine?.Invoke($"({string.Join(", ", param)});");

                Console.ResetColor();
            }

            List<(string library, string entryPoint, CharSet charSet)>? imports = null;

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;

                Write?.Invoke("    ");

                if (m.IsAssembly)
                {
                    Write?.Invoke("internal ");
                }

                if (m.IsFamily)
                {
                    Write?.Invoke("protected ");
                }

                if (m.IsFamilyAndAssembly)
                {
                    Write?.Invoke("internal protected ");
                }

                if (m.IsFamilyOrAssembly)
                {
                    Write?.Invoke("protected internal ");
                }

                if (m.IsPrivate)
                {
                    Write?.Invoke("private ");
                }

                if (m.IsPublic)
                {
                    Write?.Invoke("public ");
                }

                if (m.IsStatic)
                {
                    Write?.Invoke("static ");
                }

                if (m.IsAbstract)
                {
                    Write?.Invoke("abstract ");
                }

                if (m.IsVirtual)
                {
                    Write?.Invoke("virtual ");
                }

                if (m.IsFinal)
                {
                    Write?.Invoke("final ");
                }

                var dllimport = m.GetCustomAttribute<DllImportAttribute>();
                if (dllimport is not null && !m.IsDefined(typeof(ObsoleteAttribute)))
                {
                    imports ??= [];
                    Write?.Invoke("import(");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Write?.Invoke(@$"""{dllimport.Value}""");
                    if (dllimport.EntryPoint is not null)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Write?.Invoke(", ");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Write?.Invoke(@$"""{dllimport.EntryPoint}""");
                        imports.Add((dllimport.Value, dllimport.EntryPoint, dllimport.CharSet));
                    }
                    else
                    {
                        imports.Add((dllimport.Value, m.Name, dllimport.CharSet));
                    }

                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Write?.Invoke(") ");
                }

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                
                try
                {
                    Write?.Invoke(m.ReturnType.FormatTypeName() + " ");
                }
                catch (Exception ex)
                when (continueOnFailure)
                {
                    Write?.Invoke("@@unknown ");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Write?.Invoke($"/* Failed to load type ({ex.Message}) */ ");
                }

                Console.ForegroundColor = ConsoleColor.Gray;
                Write?.Invoke(m.Name);
                Console.ForegroundColor = ConsoleColor.DarkCyan;

                var param = m.GetParameters().Select(p =>
                {
                    var sb = new StringBuilder();
                    if (p.Position == 0 &&
                        p.Member.IsDefined(typeof(ExtensionAttribute)))
                    {
                        sb.Append("this ");
                    }

                    if (p.IsRetval)
                    {
                        sb.Append("retval ");
                    }

                    if (p.IsOptional)
                    {
                        sb.Append("optional ");
                    }

                    if (p.IsIn)
                    {
                        sb.Append("in ");
                    }

                    if (p.IsOut)
                    {
                        sb.Append("out ");
                    }

                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    sb.Append(p.ParameterType.FormatTypeName());
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    sb.Append(' ');
                    sb.Append(p.Name);

                    if (p.IsOptional || p.HasDefaultValue)
                    {
                        sb.Append(" = ");

                        object? defaultValue = null;
                        try
                        {
                            defaultValue = p.DefaultValue;
                        }
                        catch (Exception ex)
                        when (continueOnFailure)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Error.WriteLine($@"
// Unresolved DefaultValue for '{p.Name}': {ex.GetType().Name}: {ex.Message}");
                            Console.ResetColor();

                            defaultValue = "unknown";
                        }

                        sb.Append(defaultValue ?? "default");
                    }

                    return sb.ToString();
                });

                Write?.Invoke($"({string.Join(", ", param)})");

                if (interfaceMappings is not null)
                {
                    var ifimpl = interfaceMappings
                        .Select(ifm => ifm.TargetMethods
                        .Select((tm, i) => new { tm, im = ifm.InterfaceMethods[i] })
                        .Where(o => o.tm == m))
                        .SelectMany(o => o)
                        .Select(o => $"{o.im.DeclaringType?.FormatTypeName()}.{o.im.Name}")
                        .ToArray();

                    if (ifimpl.Length > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Write?.Invoke($" = {string.Join(", ", ifimpl)}");
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                    }
                }

                WriteLine?.Invoke(";");

                Console.ResetColor();
            }

#if NETCOREAPP
            if (imports is not null)
            {
                foreach (var (library, entryPoint, charSet) in imports)
                {
                    if (!NativeLibrary.TryLoad(library, out var lib))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine($"// Unresolved DllImport: Error loading library '{library}'");
                        continue;
                    }

                    if (NativeLibrary.TryGetExport(lib, entryPoint, out _))
                    {
                        continue;
                    }

                    switch (charSet)
                    {
                        case CharSet.None:
                        case CharSet.Ansi:
                            if (NativeLibrary.TryGetExport(lib, $"{entryPoint}A", out _))
                            {
                                continue;
                            }

                            break;

                        case CharSet.Unicode:
                        case CharSet.Auto:
                            if (NativeLibrary.TryGetExport(lib, $"{entryPoint}W", out _))
                            {
                                continue;
                            }

                            break;

                        default:
                            break;
                    }

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"// Unresolved DllImport: Cannot find function '{entryPoint}' in library '{library}'");
                }
            }
#endif

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            WriteLine?.Invoke("  }");
            Console.ResetColor();

            WriteLine?.Invoke("");
        }
    }
}
