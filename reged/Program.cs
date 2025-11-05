using Arsenal.ImageMounter.IO.Devices;
using DiscUtils;
using DiscUtils.Registry;
using DiscUtils.Streams;
using DiscUtils.Wim;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace reged;

public static class Program
{
    public enum OpMode
    {
        None,
        Query,
        Add,
        Remove,
        Copy,
        Move
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
#if DEBUG
            Console.Error.WriteLine(ex);
#else
            Console.Error.WriteLine(ex.JoinMessages());
#endif
            Console.ResetColor();
            return ex.HResult;
        }
    }

    public static int UnsafeMain(params string[] args)
    {
        string? imageFile = null;
        var partition = 0;
        string? wimPath = null;
        var wimIndex = 1;
        string? hiveFile = null;
        string? keyPath = null;
        string? valueName = null;
        string? dataString = null;
        byte[]? dataBytes = null;
        var binaryOutput = false;
        var recursive = false;
        OpMode opMode = 0;
        RegistryValueType? type = null;
        var listUsers = false;

        var cmds = CommandLineParser.ParseCommandLine(args, StringComparer.Ordinal);

        foreach (var cmd in cmds)
        {
            if ((cmd.Key is "q" or "query") && cmd.Value.Length == 0 && opMode == 0)
            {
                opMode = OpMode.Query;
            }
            else if ((cmd.Key is "a" or "add") && cmd.Value.Length == 0 && opMode == 0)
            {
                opMode = OpMode.Add;
            }
            else if ((cmd.Key is "d" or "remove") && cmd.Value.Length == 0 && opMode == 0)
            {
                opMode = OpMode.Remove;
            }
            else if ((cmd.Key is "c" or "copy") && cmd.Value.Length == 0 && opMode == 0)
            {
                opMode = OpMode.Copy;
            }
            else if ((cmd.Key is "m" or "move") && cmd.Value.Length == 0 && opMode == 0)
            {
                opMode = OpMode.Move;
            }
        }

        foreach (var cmd in cmds)
        {
            if (cmd.Key == "hive" && cmd.Value.Length == 1)
            {
                hiveFile = cmd.Value[0];
            }
            else if (cmd.Key == "image" && cmd.Value.Length == 1)
            {
                imageFile = cmd.Value[0];
            }
            else if (cmd.Key == "part" && cmd.Value.Length == 1
                && cmds.ContainsKey("image") && int.TryParse(cmd.Value[0], out partition))
            {
            }
            else if (cmd.Key == "wim" && cmd.Value.Length == 1 && !cmds.ContainsKey("image"))
            {
                wimPath = cmd.Value[0];
            }
            else if (cmd.Key == "index" && cmd.Value.Length == 1
                && int.TryParse(cmd.Value[0], out wimIndex) && cmds.ContainsKey("wim"))
            {
            }
            else if ((cmd.Key is "k" or "key") && cmd.Value.Length == 1)
            {
                keyPath = cmd.Value[0];
            }
            else if ((cmd.Key is "v" or "value") && cmd.Value.Length is 0 or 1)
            {
                valueName = cmd.Value.FirstOrDefault() ?? "";
            }
            else if (cmd.Key == "" && cmd.Value.Length == 1
                && (cmds.ContainsKey("v") || cmds.ContainsKey("value"))
                && !cmds.ContainsKey("raw")
                && opMode is OpMode.Add or OpMode.Remove)
            {
                dataString = cmd.Value[0];
            }
            else if (cmd.Key == "binary" && cmd.Value.Length == 0
                && (cmds.ContainsKey("v") || cmds.ContainsKey("value"))
                && !cmds.ContainsKey("")
                && opMode is OpMode.Add)
            {
                dataBytes = ReadAllStdIn();
            }
            else if (cmd.Key == "binary" && cmd.Value.Length == 0
                && opMode is OpMode.Query)
            {
                binaryOutput = true;
            }
            else if ((cmd.Key is "r" or "subkeys") && cmd.Value.Length == 0
                && opMode is OpMode.Query or OpMode.Remove)
            {
                recursive = true;
            }
            else if ((cmd.Key is "t" or "type") && cmd.Value.Length == 1
                && opMode == OpMode.Add
                && TryParseRegistryType(cmd.Value[0], out var valueType))
            {
                type = valueType;
            }
            else if (cmd.Key is "listusers" && cmd.Value.Length == 0
                && opMode == OpMode.Query
                && !cmds.ContainsKey("key") && !cmds.ContainsKey("value") && !cmds.ContainsKey("binary"))
            {
                listUsers = true;
                keyPath = @"SAM\Domains\Account\Users";
            }
            else if (cmd.Key is "q" or "query" or "a" or "add" or "d" or "remove" or "c" or "copy" or "m" or "move")
            {
            }
            else
            {
                var msg = @"Registry hive editing tool.
Copyright (c) 2023 - 2025, LTR Data. https://ltr-data.se

Query syntax:
reged --query --hive=filepath [--key=keypath] [--subkeys] [--value=valuename] [--binary]

Add/update syntax:
reged --add --hive=filepath --key=keypath [--subkeys] [--value=valuename [--type=valuetype] data | --binary

Remove syntax:
reged --remove --hive=filepath [--key=keypath] [--subkeys] [--value=valuename [--type=valuetype] [data]]

Omitting --key or specifying an empty key name refers to root key in the hive.

Specifying --value without a value name in --query mode refers to the default (unnamed) value for the key.

In --add mode when adding or updating a value, the --type option can be ommitted. In that case, the same type as an existing value with the same name will be used, or REG_SZ if no value with the same name currently exists. Default value type is REG_BINARY instead of REG_SZ if --binary is specified and value data is read from standard input.

If data is specified along with --remove option, value is only removed if it matches specified data. If data is specified with --remove for a REG_MULTI_SZ (multi-string) value, the string specified by the data is removed from the multi-string value and remaining multi-string value is written back to registry hive.

If --type is specified along with --remove option, only values with specified type are removed.

For string values, you can specify some special characters by escaping them with '\'. That is, '\\' means a single '\', '\n' means newline character (delimiter for multi-string values), '\""' means '""'.

Binary data for REG_BINARY values, can be specified as hexadecimal string with two characters for each byte, optionally delimited by '-' or ':'. Numeric data for REG_DWORD and REG_QWORD can be specified as decimal number or, prefixed with '0x', as hexadecimal number.

The --binary option in --query mode suppresses all normal text output and only dumps value contents in binary format to standard output.

The --binary option in --add mode reads new value data to add from standard input.

Image files:
You can query and manipulate registry hives inside virtual machine disk image files such as vhd, vhdx, vdi and vmdk.
reged --image=path.vhd --part=partitionnumber ...

Where 'partitionnumber' is one-based number of the partition in the image file, zero to read entire image as one partition. File systems ntfs and fat32 are detected automatically.
";

                Console.WriteLine(StringFormatting.LineFormat(msg.AsSpan(), indentWidth: 2));

                return 1;
            }
        }

        if (opMode == 0 || hiveFile is null)
        {
            var msg = "Needs --hive=path and either of --query, --add or --remove. Try --help for syntax help.";

            Console.WriteLine(StringFormatting.LineFormat(msg.AsSpan(), indentWidth: 2));

            return 1;
        }

        var access = opMode switch
        {
            OpMode.Query => FileAccess.Read,
            OpMode.Add or OpMode.Remove or OpMode.Copy or OpMode.Move => FileAccess.ReadWrite,
            _ => throw new InvalidOperationException()
        };

        if (imageFile is not null || wimPath is not null)
        {
            DiscUtils.Containers.SetupHelper.SetupContainers();
            DiscUtils.FileSystems.SetupHelper.SetupFileSystems();
        }

        using var image = OpenDiskImage(imageFile, access);

        var partitions = image?.Partitions;

        if (image is not null && partition > 0
            && (partitions is null || partitions.Count < partition))
        {
            throw new DriveNotFoundException($"Partition {partition} not found in image '{imageFile}'");
        }

        var fileSystemStream = image is not null
            ? partition == 0
            ? image.Content
            : partitions?[partition - 1].Open()
            : null;

        using var wimFileStream = wimPath is not null ? File.OpenRead(wimPath) : null;

        var wimFile = wimFileStream is not null ? new WimFile(wimFileStream) : null;

        var fileSystem = fileSystemStream is not null
            && FileSystemManager.DetectFileSystems(fileSystemStream) is { } fsInfo
            && fsInfo.Count > 0
            ? fsInfo[0].Open(fileSystemStream) :
            wimFile?.GetImage(wimIndex - 1);

        if (imageFile is not null
            && fileSystem is null)
        {
            if (partition == 0 && partitions is not null)
            {
                throw new NotSupportedException($"Image file '{imageFile}' does not contain a supported file system. Use --part to specify a partition in the image file.");
            }
            else if (partition == 0)
            {
                throw new NotSupportedException($"Image file '{imageFile}' does not contain a supported file system.");
            }
            else
            {
                throw new NotSupportedException($"Partition {partition} ({partitions![partition - 1].TypeAsString}) in image file '{imageFile}' does not contain a supported file system.");
            }
        }

        using var hive = fileSystem is not null
            ? (opMode == OpMode.Add && !fileSystem.FileExists(hiveFile)
            ? RegistryHive.Create(fileSystem.OpenFile(hiveFile, FileMode.OpenOrCreate, access))
            : new RegistryHive(fileSystem.GetFileInfo(hiveFile), access))
            : (opMode == OpMode.Add && !File.Exists(hiveFile)
            ? RegistryHive.Create(hiveFile)
            : new RegistryHive(hiveFile, access));

        var key = keyPath is null
            ? hive.Root : hive.Root.OpenSubKey(keyPath);

        if (!binaryOutput && key is not null)
        {
            Console.WriteLine($@"\{key.Name}");
        }

        if (opMode == OpMode.Query)
        {
            if (key is null)
            {
                throw new IOException($"Key '{keyPath}' not found in hive '{hiveFile}'");
            }

            if (listUsers)
            {
                ListUsers(key!);
            }
            else
            {
                QueryKey(key!, valueName, binaryOutput, recursive);
            }
        }
        else if (opMode == OpMode.Remove)
        {
            if (valueName is null)
            {
                bool result;

                if (recursive)
                {
                    result = hive.Root.DeleteSubKeyTree(keyPath);
                }
                else
                {
                    result = hive.Root.DeleteSubKey(keyPath, throwOnMissingSubKey: false);
                }

                if (result)
                {
                    Console.WriteLine($"Deleted '{keyPath}'");
                }
                else
                {
                    Console.WriteLine("No keys found to delete");
                }

                return 0;
            }

            if (key is null)
            {
                throw new IOException($"Key '{keyPath}' not found in hive '{hiveFile}'");
            }

            var deleteCount = RemoveValueFromKey(key, valueName, dataString, type);

            Console.WriteLine($"Found and removed {deleteCount} values");

            return 0;
        }
        else if (opMode == OpMode.Add)
        {
            key ??= hive.Root.CreateSubKey(keyPath);

            if (key is null)
            {
                throw new IOException($"Failed to create key '{keyPath}' in hive '{hiveFile}'");
            }

            if (valueName is null)
            {
                return 0;
            }

            type ??= key.GetRegistryValue(valueName)?.DataType
                ?? (dataBytes is not null ? RegistryValueType.Binary : RegistryValueType.String);

            if (dataBytes is not null)
            {
                var value = key.GetRegistryValue(valueName)
                    ?? key.AddRegistryValue(valueName);

                value.SetRawData(dataBytes, type.Value);
            }
            else if (dataString is not null)
            {
                var data = ParseDataString(dataString, type.Value);

                key.SetValue(valueName, data, type.Value);
            }
            else
            {
                throw new InvalidOperationException("Missing value data");
            }

            return 0;
        }
        else
        {
            throw new NotImplementedException($"Operation mode {opMode} not implemented");
        }

        return 0;
    }

    private static long RemoveValueFromKey(RegistryKey key, string? valueName, string? dataString, RegistryValueType? type)
    {
        var deleteCount = 0L;

        if ((dataString is not null || type.HasValue)
            && key.GetRegistryValue(valueName) is { } value)
        {
            if ((!type.HasValue || type.Value == value.DataType)
                && (dataString is null
                || (value.DataType is RegistryValueType.String or RegistryValueType.Link or RegistryValueType.ExpandString or RegistryValueType.Dword or RegistryValueType.Qword or RegistryValueType.DwordBigEndian
                && ParseDataString(dataString, value.DataType) == value.Value)
                || (value.Value is string[] strings
                && strings.Contains((string)ParseDataString(dataString, RegistryValueType.String)))
                || (value.Value is byte[] bytes
                && bytes.SequenceEqual((byte[])ParseDataString(dataString, RegistryValueType.Binary)))))
            {
                if (dataString is not null
                    && value.DataType == RegistryValueType.MultiString)
                {
                    var deleteLine = (string)ParseDataString(dataString, RegistryValueType.String);

                    var lines = (string[])value.Value;
                    var newLines = lines
                        .Where(line => line != deleteLine)
                        .ToArray();

                    if (newLines.Length < lines.Length)
                    {
                        value.SetValue(newLines, RegistryValueType.MultiString);
                        deleteCount++;
                        Console.WriteLine($@"Removed matching lines from value '{valueName}' from '\{key.Name}'");
                    }
                }
                else
                {
                    key.DeleteValue(valueName);
                    deleteCount++;
                    Console.WriteLine($@"Removed value '{valueName}' from '\{key.Name}'");
                }
            }
        }
        else if (key.DeleteValue(valueName, throwOnMissingValue: false))
        {
            deleteCount++;
            Console.WriteLine($@"Removed value from '\{key.Name}'");
        }

        foreach (var subkey in key.SubKeys)
        {
            deleteCount += RemoveValueFromKey(subkey, valueName, dataString, type);
        }

        return deleteCount;
    }

    private static void ListUsers(RegistryKey key)
    {
        var namesKey = key.OpenSubKey("Names")
            ?? throw new DirectoryNotFoundException("Registry key 'Names' not found");

        foreach (var name in namesKey.SubKeys)
        {
            try
            {
                var uid = (int)name.GetValueType(null);

                var msg = $"{uid,-5} {name.KeyName}";

                Console.WriteLine(msg);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"User '{name.Name}' failed: {ex.JoinMessages()}");
                Console.ResetColor();
            }
        }

        Console.WriteLine();
    }

    private static void QueryKey(RegistryKey key, string? valueName, bool binaryOutput, bool recursive)
    {
        if (valueName is null)
        {
            foreach (var value in key.GetValueNames())
            {
                try
                {
                    if (binaryOutput)
                    {
                        if (key.GetRegistryValue(value) is { } valueObj)
                        {
                            var data = valueObj.RawValue;
                            Console.OpenStandardOutput().Write(data, 0, data.Length);
                        }
                    }
                    else
                    {
                        var data = key.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

                        var msg = $"    {value,-20}  {FormatRegistryValue(data, key.GetValueType(value))}";

                        Console.WriteLine(msg);
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Value '{value}' in key '{key.Name}' failed: {ex.JoinMessages()}");
                    Console.ResetColor();
                }
            }
        }
        else
        {
            if (binaryOutput)
            {
                if (key.GetRegistryValue(valueName) is { } valueObj)
                {
                    var data = valueObj.RawValue;
                    Console.OpenStandardOutput().Write(data, 0, data.Length);
                }
            }
            else
            {
                if (key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is { } data)
                {
                    var msg = $"    {valueName,-20}  {FormatRegistryValue(data, key.GetValueType(valueName))}";

                    Console.WriteLine(msg);
                }
            }
        }

        if (!binaryOutput)
        {
            Console.WriteLine();
        }

        if (recursive || valueName is null)
        {
            foreach (var subkey in key.SubKeys)
            {
                if (!binaryOutput)
                {
                    Console.WriteLine($@"\{subkey.Name}");
                }

                if (recursive)
                {
                    QueryKey(subkey, valueName, binaryOutput, recursive);
                }
            }
        }
    }

    private static byte[] ReadAllStdIn()
    {
        var stdin = Console.OpenStandardInput();

        if (stdin.CanSeek)
        {
            return stdin.ReadExactly((int)stdin.Length);
        }

        var buffer = new MemoryStream();
        stdin.CopyTo(buffer);
        if (buffer.Length == buffer.Capacity)
        {
            return buffer.GetBuffer();
        }
        else
        {
            return buffer.ToArray();
        }
    }

    public static object ParseDataString(string dataString, RegistryValueType type)
    {
        if (type is RegistryValueType.String or RegistryValueType.Link or RegistryValueType.ExpandString or RegistryValueType.MultiString)
        {
            Span<char> parsedDataString = stackalloc char[dataString.Length];

            var parsedLength = 0;

            for (int i = 0; i < dataString.Length; i++, parsedLength++)
            {
                if (dataString[i] == '\\'
                    && i != dataString.Length - 1)
                {
                    switch (dataString[i + 1])
                    {
                        case '\\':
                            i++;
                            parsedDataString[parsedLength] = '\\';
                            continue;

                        case '"':
                            i++;
                            parsedDataString[parsedLength] = '"';
                            continue;

                        case 'n':
                            i++;
                            parsedDataString[parsedLength] = '\n';
                            continue;

                        default:
                            break;
                    }
                }

                parsedDataString[parsedLength] = dataString[i];
            }

            if (parsedLength != dataString.Length)
            {
                dataString = parsedDataString.Slice(0, parsedLength).ToString();
            }

            if (type is RegistryValueType.MultiString)
            {
                return dataString.Split('\n');
            }
            else
            {
                return dataString;
            }
        }
        else if (type is RegistryValueType.Dword or RegistryValueType.DwordBigEndian)
        {
            if (int.TryParse(dataString, out var i))
            {
                return i;
            }

            if (uint.TryParse(dataString, out var ui))
            {
                return ui;
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            if (dataString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(dataString.AsSpan(2), NumberStyles.HexNumber, null, out ui))
#else
            if (dataString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(dataString.Substring(2), NumberStyles.HexNumber, null, out ui))
#endif
            {
                return ui;
            }

            throw new FormatException($"Invalid REG_DWORD: '{dataString}'");
        }
        else if (type is RegistryValueType.Qword)
        {
            if (long.TryParse(dataString, out var i))
            {
                return i;
            }

            if (ulong.TryParse(dataString, out var ui))
            {
                return ui;
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            if (dataString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && ulong.TryParse(dataString.AsSpan(2), NumberStyles.HexNumber, null, out ui))
#else
            if (dataString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && ulong.TryParse(dataString.Substring(2), NumberStyles.HexNumber, null, out ui))
#endif
            {
                return ui;
            }

            throw new FormatException($"Invalid REG_QWORD: '{dataString}'");
        }
        else
        {
            var dataSpan = dataString.Replace("-", null).Replace(":", null).AsSpan();

            if (dataSpan.StartsWith("0x".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                dataSpan = dataSpan.Slice(2);
            }

            if ((dataSpan.Length & 1) == 1)
            {
#if NET6_0_OR_GREATER
                throw new ArgumentException($"Invalid hexadecimal data string '{dataSpan}'");
#else
                throw new ArgumentException($"Invalid hexadecimal data string '{dataSpan.ToString()}'");
#endif
            }

            return HexExtensions.ParseHexString(dataSpan);
        }
    }

    private static bool TryParseRegistryType(string typeString, out RegistryValueType type)
    {
        if (Enum.TryParse(typeString, ignoreCase: true, out type))
        {
            return true;
        }

        if (uint.TryParse(typeString, out var result))
        {
            type = (RegistryValueType)result;
            return true;
        }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        if (typeString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(typeString.AsSpan(2), NumberStyles.HexNumber, null, out result))
#else
        if (typeString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(typeString.Substring(2), NumberStyles.HexNumber, null, out result))
#endif
        {
            type = (RegistryValueType)result;
            return true;
        }

        switch (typeString.ToUpperInvariant())
        {
            case "REG_SZ":
                type = RegistryValueType.String;
                return true;

            case "REG_EXPAND_SZ":
                type = RegistryValueType.ExpandString;
                return true;

            case "REG_MULTI_SZ":
                type = RegistryValueType.MultiString;
                return true;

            case "REG_QWORD":
                type = RegistryValueType.Qword;
                return true;

            case "REG_DWORD":
                type = RegistryValueType.Dword;
                return true;

            case "REG_DWORD_BIG_ENDIAN":
                type = RegistryValueType.DwordBigEndian;
                return true;

            case "REG_BINARY":
                type = RegistryValueType.Binary;
                return true;

            case "REG_NONE":
                type = RegistryValueType.None;
                return true;

            case "REG_LINK":
                type = RegistryValueType.Link;
                return true;

            default:
                return false;
        }
    }

    public static string FormatRegistryValue(object? value, RegistryValueType type)
    {
        if (value is null)
        {
            return "(null)";
        }

        switch (type)
        {
            case RegistryValueType.String:
                return @$"REG_SZ       : {FormatRegistryString((string)value)}";

            case RegistryValueType.ExpandString:
                return @$"REG_EXPAND_SZ: {FormatRegistryString((string)value)}";

            case RegistryValueType.Binary:
                return $@"REG_BINARY   : {FormatRegistryBytes((byte[])value)}";

            case RegistryValueType.None:
                return $@"REG_NONE     : {FormatRegistryBytes((byte[])value)}";

            case RegistryValueType.Dword:
                return $@"REG_DWORD    : {value} (0x{value:X})";

            case RegistryValueType.DwordBigEndian:
                return $@"REG_DWORD_BIG_ENDIAN: {value} (0x{value:X})";

            case RegistryValueType.Qword:
                return $@"REG_QWORD    : {value} (0x{value:X})";

            case RegistryValueType.Link:
                return $@"REG_LINK     : {FormatRegistryString((string)value)}";

            case RegistryValueType.MultiString:
                return $@"REG_MULTI_SZ :
{string.Join(Environment.NewLine, ((string[])value).Select(v => $@"{"",-28}{FormatRegistryString(v)}"))}";

            default:
                if (value is byte[] bytes)
                {
                    return $@"0x{type,-11:X}: {FormatRegistryBytes(bytes)}";
                }
                else
                {
                    return $@"0x{type,-11:X}: ""{value}""";
                }
        }
    }

    public static string FormatRegistryBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        return $@"
{string.Join(Environment.NewLine, HexExtensions.FormatHexLines(bytes).Select(v => $@"{"",-28}{v}"))}";
    }

    public static string FormatRegistryString(string data)
        => $@"""{data.Replace(@"\", @"\\").Replace(@"""", @"\""")}""";

    public static VirtualDisk? OpenDiskImage(string? path, FileAccess access)
    {
        if (path is null)
        {
            return null;
        }

        if (((path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal)) &&
            Path.GetExtension(path) == "") ||
            path.StartsWith("/dev/", StringComparison.Ordinal))
        {
            var physicalDiskStream = new DiskDevice(path, FileAccess.Read).GetRawDiskStream();
            try
            {
                return new DiscUtils.Raw.Disk(physicalDiskStream, Ownership.Dispose);
            }
            catch
            {
                physicalDiskStream.Dispose();
                throw;
            }
        }

        return VirtualDisk.OpenDisk(path, access)
            ?? new DiscUtils.Raw.Disk(path, access);
    }
}
