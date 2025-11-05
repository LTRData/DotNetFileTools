using LTRData.Extensions.Buffers;
using LTRData.Extensions.Native.Memory;
using LTRData.Extensions.Split;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace peinfo;

public class ApiSetResolver(ImmutableDictionary<string, string>? apiSetLookup)
{
    public static ApiSetResolver Default
    {
        get => field ??= GetApiSetTranslations();
        set => field = value;
    }

    public static ApiSetResolver Empty => field ??= new(null);

    public bool HasTranslations => apiSetLookup is not null && !apiSetLookup.IsEmpty;

    public bool TryLookupApiSet(string? moduleNameImport, [NotNullWhen(true)] out string? moduleName)
    {
        moduleName = null;

        if (apiSetLookup is null
            || apiSetLookup.IsEmpty
            || moduleNameImport is null
            || !moduleNameImport.Contains("-l", StringComparison.OrdinalIgnoreCase)
            || moduleNameImport.Contains('.') && !moduleNameImport.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lastDelimited = moduleNameImport.LastIndexOf('-');

        if (lastDelimited >= 0)
        {
            moduleNameImport = moduleNameImport.Substring(0, lastDelimited);
        }

        return apiSetLookup.TryGetValue(moduleNameImport, out moduleName);
    }

    public static ApiSetResolver GetApiSetTranslations(string file)
        => new(GetApiSetTranslations(File.OpenRead(file), PEStreamOptions.Default));

    public static ImmutableDictionary<string, string>? GetApiSetTranslations(Stream file, PEStreamOptions options)
    {
        using var reader = new PEReader(file, options);

        return GetApiSetTranslations(reader);
    }

    public static ImmutableDictionary<string, string>? GetApiSetTranslations(PEReader reader)
    {
        var section = reader.GetSectionData(".apiset").AsSpan();

        if (section.IsEmpty)
        {
            return null;
        }

        return ParseTranslations(section);
    }

    private static ApiSetResolver GetApiSetTranslations()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && Environment.OSVersion.Version >= new Version(6, 1))
        {
            // On Windows, we can get contents of active system apisetschema.dll directly
            // mapped in process address space

            [DllImport("ntdll.dll")]
            extern static nint RtlGetCurrentPeb();

            var peb = RtlGetCurrentPeb();

            var apiSetMapPtr = Marshal.ReadIntPtr(peb + (Environment.Is64BitProcess ? 0x68 : 0x38));

            if (VmQuery.TryGetAllocationRange(apiSetMapPtr, out var range))
            {
                var offset = (int)(apiSetMapPtr - range.AllocationBase);

                var apiSetMapSpan = new ReadOnlyNativeMemory<byte>(apiSetMapPtr, (int)(range.Size - offset));

                var parsedFromMem = new ApiSetResolver(ParseTranslations(apiSetMapSpan.Span));

                if (parsedFromMem.HasTranslations)
                {
                    return parsedFromMem;
                }
            }
        }

        return Environment.GetEnvironmentVariable("PATH").AsMemory()
                .TokenEnum(Path.PathSeparator)
                .Select(m => m.ToString())
                .Prepend(Environment.GetFolderPath(Environment.SpecialFolder.System))
                .Where(Directory.Exists)
                .Distinct()
                .Select(path => Path.Combine(path, "apisetschema.dll"))
                .Where(File.Exists)
                .Select(GetApiSetTranslations)
                .FirstOrDefault(dict => dict.HasTranslations)
                ?? Empty;
    }

    private static ImmutableDictionary<string, string>? ParseTranslations(ReadOnlySpan<byte> apisetSection)
    {
        var version = MemoryMarshal.Read<uint>(apisetSection);

        int offset;
    
        var dict = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);

        if (version < 3)
        {
            offset = Unsafe.SizeOf<ApiSetHeader2>();
            var header = MemoryMarshal.Read<ApiSetHeader2>(apisetSection);

            var array = MemoryMarshal.Cast<byte, ApiSetNamespaceHeader2>(apisetSection.Slice(offset)).Slice(0, header.Count);

            for (var i = 0; i < header.Count; i++)
            {
                var item = array[i];

                var name = apisetSection.Slice(item.NameOffset, item.NameLength).ReadNullTerminatedUnicode();

                var lastDelimiter = name.LastIndexOf('-');

                if (lastDelimiter > 0)
                {
                    name = name.Slice(0, lastDelimiter);
                }

#if NET6_0_OR_GREATER
                var namespaceName = $"api-{name}";
#else
                var namespaceName = $"api-{name.ToString()}";
#endif

                var arrayHeader = MemoryMarshal.Read<ApiSetValueArrayHeader2>(apisetSection.Slice(item.DataOffset));

                var values = MemoryMarshal.Cast<byte, ApiSetValueEntry2>(apisetSection.Slice(item.DataOffset + Unsafe.SizeOf<ApiSetValueArrayHeader2>())).Slice(0, arrayHeader.Count);

                for (var j = 0; j < values.Length; j++)
                {
                    var entry = values[j];

                    if (entry.NameLength == 0 || !dict.ContainsKey(namespaceName))
                    {
                        var valueName = apisetSection.Slice(entry.ValueOffset, entry.ValueLength).ReadNullTerminatedUnicodeString();

                        dict[namespaceName] = valueName;
                    }
                }
            }
        }
        else if (version < 6)
        {
            offset = Unsafe.SizeOf<ApiSetHeader3>();
            var header = MemoryMarshal.Read<ApiSetHeader3>(apisetSection);

            var array = MemoryMarshal.Cast<byte, ApiSetNamespaceHeader3>(apisetSection.Slice(offset)).Slice(0, header.Count);

            for (var i = 0; i < header.Count; i++)
            {
                var item = array[i];

                var name = apisetSection.Slice(item.NameOffset, item.NameLength).ReadNullTerminatedUnicode();

                var lastDelimiter = name.LastIndexOf('-');

                if (lastDelimiter > 0)
                {
                    name = name.Slice(0, lastDelimiter);
                }

#if NET6_0_OR_GREATER
                var namespaceName = $"api-{name}";
#else
                var namespaceName = $"api-{name.ToString()}";
#endif

                var arrayHeader = MemoryMarshal.Read<ApiSetValueArrayHeader3>(apisetSection.Slice(item.DataOffset));

                var values = MemoryMarshal.Cast<byte, ApiSetValueEntry3>(apisetSection.Slice(item.DataOffset + Unsafe.SizeOf<ApiSetValueArrayHeader3>())).Slice(0, arrayHeader.Count);

                for (var j = 0; j < values.Length; j++)
                {
                    var entry = values[j];

                    if (entry.NameLength == 0 || !dict.ContainsKey(namespaceName))
                    {
                        var valueName = apisetSection.Slice(entry.ValueOffset, entry.ValueLength).ReadNullTerminatedUnicodeString();

                        dict[namespaceName] = valueName;
                    }
                }
            }
        }
        else if (version < 8)
        {
            var header = MemoryMarshal.Read<ApiSetHeader6>(apisetSection);

            var array = MemoryMarshal.Cast<byte, ApiSetNamespaceHeader6>(apisetSection.Slice(header.entryOffset)).Slice(0, header.Count);

            for (var i = 0; i < header.Count; i++)
            {
                var item = array[i];

                var name = apisetSection.Slice(item.NameOffset, item.NameLength).ReadNullTerminatedUnicode();

                var lastDelimiter = name.LastIndexOf('-');

                if (lastDelimiter > 0)
                {
                    name = name.Slice(0, lastDelimiter);
                }

                var namespaceName = name.ToString();

                var values = MemoryMarshal.Cast<byte, ApiSetValueEntry3>(apisetSection.Slice(item.ValOffset)).Slice(0, item.ValueCount);

                for (var j = 0; j < item.ValueCount; j++)
                {
                    var entry = values[j];

                    if (entry.ValueLength == 0)
                    {
                        continue;
                    }

                    if (entry.NameLength == 0 || !dict.ContainsKey(namespaceName))
                    {
                        var valueName = apisetSection.Slice(entry.ValueOffset, entry.ValueLength).ReadNullTerminatedUnicodeString();

                        dict[namespaceName] = valueName;
                    }
                }
            }
        }

        if (dict.Count == 0)
        {
            return null;
        }

        return dict.ToImmutable();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetHeader2
    {
        public readonly int Version;
        public readonly int Count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetHeader3
    {
        public readonly int Version;
        public readonly int Size;
        public readonly int Flags;
        public readonly int Count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetHeader6
    {
        public readonly int Version;
        public readonly int Size;
        public readonly int Flags;
        public readonly int Count;
        public readonly int entryOffset;
        public readonly int hashOffset;
        public readonly int hashFactor;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetNamespaceHeader2
    {
        public readonly int NameOffset;
        public readonly int NameLength;
        public readonly int DataOffset;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetNamespaceHeader3
    {
        public readonly int Flags;
        public readonly int NameOffset;
        public readonly int NameLength;
        public readonly int AliasOffset;
        public readonly int AliasLength;
        public readonly int DataOffset;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetNamespaceHeader6
    {
        public readonly int Flags;
        public readonly int NameOffset;
        public readonly int NameLength;
        public readonly int HashedOffset;
        public readonly int ValOffset;
        public readonly int ValueCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetNamespaceEntry6
    {
        public readonly int Flags;
        public readonly int Offset;
        public readonly int NameLength;
        public readonly int NamePrefixLength;
        public readonly int ValOffset;
        public readonly int ValueCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetValueArrayHeader2
    {
        public readonly int Count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetValueArrayHeader3
    {
        public readonly int Flags;
        public readonly int Count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetValueEntry2
    {
        public readonly int NameOffset;
        public readonly int NameLength;
        public readonly int ValueOffset;
        public readonly int ValueLength;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct ApiSetValueEntry3
    {
        public readonly int Flags;
        public readonly int NameOffset;
        public readonly int NameLength;
        public readonly int ValueOffset;
        public readonly int ValueLength;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct HashEntry
    {
        public readonly uint Hash;
        public readonly int NamespaceIndex;
    }
}
