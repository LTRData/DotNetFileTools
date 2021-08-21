using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace TestAssembly
{
    public static class Program
    {
        private static string FormatConstantValue(this FieldInfo field)
        {
            var value = field.GetRawConstantValue();

            if (value == null)
            {
                return "null";
            }
            else if (value is string str)
            {
                return $"'{str}'";
            }
            else if (field.FieldType.IsEnum &&
                field.FieldType != field.DeclaringType)
            {
                return $"{field.FieldType.FormatTypeName()}::{Enum.ToObject(field.FieldType, value)}";
            }
            else
            {
                return value.ToString();
            }
        }

        private static string FormatTypeName(this Type type)
        {
            var fullName = type.FullName;

            if (fullName == null || fullName.StartsWith("System.", StringComparison.Ordinal))
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

        public static int Main(params string[] args)
        {
            var continueOnFailure = false;
            var searchOption = SearchOption.TopDirectoryOnly;
            var result = 0;

            foreach (var arg in args.SelectMany(name =>
            {
                if ("-c".Equals(name, StringComparison.Ordinal))
                {
                    continueOnFailure = true;
                    return Enumerable.Empty<string>();
                }
                if ("-r".Equals(name, StringComparison.Ordinal))
                {
                    searchOption = SearchOption.AllDirectories;
                    return Enumerable.Empty<string>();
                }

                var dir = Path.GetDirectoryName(name);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    dir = ".";
                }

                return Directory.EnumerateFiles(dir, Path.GetFileName(name), searchOption);
            }))
            {
                try
                {
                    var asm = Assembly.Load(AssemblyName.GetAssemblyName(arg));

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(asm.Location);
                    Console.ResetColor();

                    foreach (var t in asm.GetTypes())
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.Write("  ");
                        if (t.IsNestedAssembly)
                        {
                            Console.Write("internal ");
                        }
                        if (t.IsNestedFamANDAssem)
                        {
                            Console.Write("internal protected ");
                        }
                        if (t.IsNestedFamily)
                        {
                            Console.Write("protected ");
                        }
                        if (t.IsNestedFamORAssem)
                        {
                            Console.Write("protected internal ");
                        }
                        if (t.IsNestedPrivate)
                        {
                            Console.Write("private ");
                        }
                        if (t.IsNestedPublic)
                        {
                            Console.Write("public ");
                        }
                        if (t.IsAbstract && t.IsSealed)
                        {
                            Console.Write("static ");
                        }
                        else if (t.IsAbstract && !t.IsInterface)
                        {
                            Console.Write("abstract ");
                        }
                        else if (t.IsSealed && t.IsClass)
                        {
                            Console.Write("sealed ");
                        }
                        if (t.IsArray)
                        {
                            Console.Write("array ");
                        }
                        if (t.IsAutoClass)
                        {
                            Console.Write("auto ");
                        }
                        if (t.IsByRef)
                        {
                            Console.Write("byref ");
                        }
                        if (t.IsCOMObject)
                        {
                            Console.Write("com ");
                        }
                        if (t.IsContextful)
                        {
                            Console.Write("contextful ");
                        }
                        if (t.IsExplicitLayout)
                        {
                            Console.Write("explicit ");
                        }
                        if (t.IsImport)
                        {
                            Console.Write("import ");
                        }
                        if (t.IsInterface)
                        {
                            Console.Write("interface ");
                        }
                        else if (t.IsClass)
                        {
                            Console.Write("class ");
                        }
                        else if (t.IsEnum)
                        {
                            if (t.IsDefined(typeof(FlagsAttribute), inherit: false))
                            {
                                Console.Write("flags ");
                            }
                            Console.Write("enum ");
                        }
                        else if (t.IsValueType)
                        {
                            Console.Write("struct ");
                        }
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write(t.FormatTypeName());
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

                        InterfaceMapping[] interfaceMappings;

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
                            Console.Write($" : {string.Join(", ", baseTypes.Select(bt => bt.FormatTypeName()))}");
                        }

                        Console.WriteLine(" {");

                        foreach (var m in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;

                            Console.Write("    ");
                            if (m.IsAssembly)
                            {
                                Console.Write("internal ");
                            }
                            if (m.IsFamily)
                            {
                                Console.Write("protected ");
                            }
                            if (m.IsFamilyAndAssembly)
                            {
                                Console.Write("internal protected ");
                            }
                            if (m.IsFamilyOrAssembly)
                            {
                                Console.Write("protected internal ");
                            }
                            if (m.IsPrivate)
                            {
                                Console.Write("private ");
                            }
                            if (m.IsPublic)
                            {
                                Console.Write("public ");
                            }
                            if (m.IsLiteral)
                            {
                                Console.Write("const ");
                            }
                            else if (m.IsStatic)
                            {
                                Console.Write("static ");
                            }
                            if (m.IsInitOnly)
                            {
                                Console.Write("readonly ");
                            }
                            Console.Write(m.FieldType.FormatTypeName() + " ");
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write(m.Name);
                            
                            if (m.IsLiteral)
                            {
                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                Console.Write(" = ");
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.Write(m.FormatConstantValue());
                            }

                            Console.ForegroundColor = ConsoleColor.DarkCyan;

                            Console.WriteLine(";");

                            Console.ResetColor();
                        }

                        foreach (var m in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;

                            Console.Write("    ");
                            if (m.IsAssembly)
                            {
                                Console.Write("internal ");
                            }
                            if (m.IsFamily)
                            {
                                Console.Write("protected ");
                            }
                            if (m.IsFamilyAndAssembly)
                            {
                                Console.Write("internal protected ");
                            }
                            if (m.IsFamilyOrAssembly)
                            {
                                Console.Write("protected internal ");
                            }
                            if (m.IsPrivate)
                            {
                                Console.Write("private ");
                            }
                            if (m.IsPublic)
                            {
                                Console.Write("public ");
                            }
                            if (m.IsStatic)
                            {
                                Console.Write("static ");
                            }
                            if (m.IsAbstract)
                            {
                                Console.Write("abstract ");
                            }
                            if (m.IsVirtual)
                            {
                                Console.Write("virtual ");
                            }
                            if (m.IsFinal)
                            {
                                Console.Write("final ");
                            }
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write(m.Name);
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
                                sb.Append(p.ParameterType.FormatTypeName());
                                sb.Append(' ');
                                sb.Append(p.Name);
                                if (p.HasDefaultValue)
                                {
                                    sb.Append(" = ");
                                    sb.Append(p.DefaultValue ?? "null");
                                }
                                return sb.ToString();
                            });
                            
                            Console.WriteLine($"({string.Join(", ", param)});");

                            Console.ResetColor();
                        }

                        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;

                            Console.Write("    ");
                            if (m.IsAssembly)
                            {
                                Console.Write("internal ");
                            }
                            if (m.IsFamily)
                            {
                                Console.Write("protected ");
                            }
                            if (m.IsFamilyAndAssembly)
                            {
                                Console.Write("internal protected ");
                            }
                            if (m.IsFamilyOrAssembly)
                            {
                                Console.Write("protected internal ");
                            }
                            if (m.IsPrivate)
                            {
                                Console.Write("private ");
                            }
                            if (m.IsPublic)
                            {
                                Console.Write("public ");
                            }
                            if (m.IsStatic)
                            {
                                Console.Write("static ");
                            }
                            if (m.IsAbstract)
                            {
                                Console.Write("abstract ");
                            }
                            if (m.IsVirtual)
                            {
                                Console.Write("virtual ");
                            }
                            if (m.IsFinal)
                            {
                                Console.Write("final ");
                            }
                            Console.Write(m.ReturnType.FormatTypeName() + " ");
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write(m.Name);
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
                                if (p.IsIn)
                                {
                                    sb.Append("in ");
                                }
                                if (p.IsOut)
                                {
                                    sb.Append("out ");
                                }
                                sb.Append(p.ParameterType.FormatTypeName());
                                sb.Append(' ');
                                sb.Append(p.Name);
                                if (p.HasDefaultValue)
                                {
                                    sb.Append(" = ");
                                    sb.Append(p.DefaultValue ?? "null");
                                }
                                return sb.ToString();
                            });

                            Console.Write($"({string.Join(", ", param)})");

                            if (interfaceMappings != null)
                            {
                                var ifimpl = interfaceMappings
                                    .Select(ifm => ifm.TargetMethods
                                    .Select((tm, i) => new { tm, im = ifm.InterfaceMethods[i] })
                                    .Where(o => o.tm == m))
                                    .SelectMany(o => o)
                                    .Select(o => $"{o.im.DeclaringType.FormatTypeName()}::{o.im.Name}")
                                    .ToArray();

                                if (ifimpl.Length > 0)
                                {
                                    Console.Write($" = {string.Join(", ", ifimpl)}");
                                }
                            }

                            Console.WriteLine(";");

                            Console.ResetColor();
                        }

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("  }");
                        Console.ResetColor();

                        Console.WriteLine();
                    }

                    continue;
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    foreach (var tlex in ex.LoaderExceptions)
                    {
                        Console.Error.WriteLine($"{arg}: {tlex}");
                    }
                    Console.ResetColor();

                    result = ex.LoaderExceptions.FirstOrDefault()?.HResult ?? ex.HResult;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"{arg}: {ex}");
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
    }
}
