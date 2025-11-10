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
            Console.ForegroundColor = ConsoleColor.Yellow;
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

    private static void WriteConstantValue(this Action<string> writer, ParameterInfo param)
    {
        var value = param.DefaultValue;

        if (value is null)
        {
            writer("null");
        }
        else if (value is string str)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            writer($@"""{str.Replace(@"\", @"\\").Replace("'", @"\'")}""");
        }
        else if (param.ParameterType.IsEnum)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            writer("(");
            Console.ForegroundColor = ConsoleColor.Yellow;
            writer(param.ParameterType.FormatTypeName());
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            writer(")");
            Console.ForegroundColor = ConsoleColor.Cyan;
            writer(Enum.ToObject(param.ParameterType, value).ToString() ?? "null");
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
            if (type.Name is not null and not "&")
            {
                fullName = type.Name;
            }
            else
            {
                fullName = type.FullName?.Replace("System.", null)
                    ?? "<unknown>";
            }
        }

        var name = new StringBuilder();

#if NET7_0_OR_GREATER
        if (type.IsUnmanagedFunctionPointer)
        {
            name.Append("function*");
        }
#endif

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
            type.GetGenericArguments() is { } gentypes &&
            gentypes.Length > 0)
        {
            name.Append('<');
            name.Append(string.Join(", ", gentypes.Select(FormatTypeName)));
            name.Append('>');
        }

        if (name.Length == 0 || name[0] == '&')
        { }

        return name.ToString();
    }

    public static int Main(params string[] cmdLine)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        var continueOnFailure = false;
        var showInvisible = false;
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
            else if ("i" == arg.Key)
            {
                showInvisible = true;
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

-i          Show invisible types

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

        foreach (var arg in args.SelectMany(name => Directory.EnumerateFiles(Path.GetDirectoryName(name) is { Length: > 0 } dir ? dir : ".", Path.GetFileName(name), searchOption)))
        {
            var fullpath = Path.GetFullPath(arg);

            try
            {
                var asm = Assembly.LoadFrom(fullpath);

                fullpath = asm.Location;

                Console.ForegroundColor = ConsoleColor.Green;
                WriteLine?.Invoke(fullpath);
                Console.ResetColor();

                ListAssembly(Write, WriteLine, asm, showInvisible, continueOnFailure);

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

    private static void ListAssembly(Action<string>? Write, Action<string>? WriteLine, Assembly asm, bool showInvisible, bool continueOnFailure)
    {
        foreach (var t in asm.GetTypes())
        {
            if (!t.IsVisible && !showInvisible)
            {
                continue;
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
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
            Console.ForegroundColor = ConsoleColor.DarkCyan;

            var baseTypes = new List<Type>();

            if (t.BaseType is { } baseType &&
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
                t.GetInterfaces() is { } interfaces &&
                interfaces.Length > 0)
            {
                baseTypes.AddRange(interfaces);
                interfaceMappings = Array.ConvertAll(interfaces, t.GetInterfaceMap);
            }
            else
            {
                interfaceMappings = null;
            }

            foreach (var (b, i) in baseTypes.Select((b, i) => (b, i)))
            {
                if (i == 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Write?.Invoke(" : ");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Write?.Invoke(", ");
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Write?.Invoke(b.FormatTypeName());
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
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

                Console.ForegroundColor = ConsoleColor.Yellow;
                    
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

                Console.ForegroundColor = ConsoleColor.White;
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

            WriteLine?.Invoke("");

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

                Console.ForegroundColor = ConsoleColor.White;
                Write?.Invoke(m.Name);
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Write?.Invoke("(");

                foreach (var (p, i) in m.GetParameters().Select((param, i) => (param, i)))
                {
                    if (i > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Write?.Invoke(", ");
                    }

                    if (p.Position == 0 &&
                        p.Member.IsDefined(typeof(ExtensionAttribute), inherit: false))
                    {
                        Write?.Invoke("this ");
                    }

                    if (p.IsRetval)
                    {
                        Write?.Invoke("retval ");
                    }

                    if (p.IsIn)
                    {
                        Write?.Invoke("in ");
                    }

                    if (p.IsOut)
                    {
                        Write?.Invoke("out ");
                    }

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Write?.Invoke(p.ParameterType.FormatTypeName());
                    Console.ForegroundColor = ConsoleColor.White;
                    Write?.Invoke($" {p.Name}");
                    if (p.IsOptional || p.HasDefaultValue)
                    {
                        Write?.Invoke(" = ");

                        try
                        {
                            Write?.WriteConstantValue(p);
                        }
                        catch (Exception ex)
                        when (continueOnFailure)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Error.WriteLine($@"
default /* Unresolved DefaultValue for '{p.Name}': {ex.GetType().Name}: {ex.Message} */");
                            Console.ResetColor();
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                WriteLine?.Invoke(");");
                Console.ResetColor();
            }

            WriteLine?.Invoke("");

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

                Console.ForegroundColor = ConsoleColor.Yellow;
                
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

                Console.ForegroundColor = ConsoleColor.White;
                Write?.Invoke(m.Name);
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Write?.Invoke("(");

                foreach (var (p, i) in m.GetParameters().Select((p, i) => (p, i)))
                {
                    if (i > 0)
                    {
                        Write?.Invoke(", ");
                    }

                    if (p.Position == 0 &&
                        p.Member.IsDefined(typeof(ExtensionAttribute)))
                    {
                        Write?.Invoke("this ");
                    }

                    if (p.IsRetval)
                    {
                        Write?.Invoke("retval ");
                    }

                    if (p.IsOptional)
                    {
                        Write?.Invoke("optional ");
                    }

                    if (p.IsIn)
                    {
                        Write?.Invoke("in ");
                    }

                    if (p.IsOut)
                    {
                        Write?.Invoke("out ");
                    }

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Write?.Invoke(p.ParameterType.FormatTypeName());
                    Console.ForegroundColor = ConsoleColor.White;
                    Write?.Invoke($" {p.Name}");

                    if (p.IsOptional || p.HasDefaultValue)
                    {
                        Write?.Invoke(" = ");

                        try
                        {
                            Write?.WriteConstantValue(p);
                        }
                        catch (Exception ex)
                        when (continueOnFailure)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Error.WriteLine($@"
default /* Unresolved DefaultValue for '{p.Name}': {ex.GetType().Name}: {ex.Message} */");
                            Console.ResetColor();
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Write?.Invoke(")");

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
                        Console.ForegroundColor = ConsoleColor.Yellow;
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

            Console.ForegroundColor = ConsoleColor.Cyan;
            WriteLine?.Invoke("  }");
            Console.ResetColor();

            WriteLine?.Invoke("");
        }
    }
}
