using Arsenal.ImageMounter.IO.Native;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace peinfo;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0056 // Use index operator

public static class ELFViewer
{
    public static void ProcessELFFile(ReadOnlySpan<byte> fileData, Options options)
    {
        var elf = MemoryMarshal.Read<ElfHeader>(fileData);

        if (!elf.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Invalid ELF file.");
            Console.ResetColor();

            return;
        }

        if (!options.HasFlag(Options.SuppressHeaders))
        {
            Console.WriteLine();
            Console.WriteLine("ELF identity:");
            Console.WriteLine($"{"Class",-35}{elf.Ident.Class}");
            Console.WriteLine($"{"Data encoding",-35}{elf.Ident.Data}");
            Console.WriteLine($"{"Version",-35}{elf.Ident.Version}");
            Console.WriteLine($"{"OS/ABI",-35}{elf.Ident.OsAbi}");
            Console.WriteLine($"{"ABI version",-35}{elf.Ident.AbiVersion}");
            Console.WriteLine();
            Console.WriteLine("ELF header:");
            Console.WriteLine($"{"Type",-35}{elf.Type}");
            Console.WriteLine($"{"Machine",-35}{elf.Machine}");
            Console.WriteLine($"{"Version",-35}{elf.Version}");
        }

        ElfHeaderEnd end;

        if (elf.Is64)
        {
            var elfHeader64 = MemoryMarshal.Read<ElfHeader64>(fileData);
            
            end = elfHeader64.End;

            if (!options.HasFlag(Options.SuppressHeaders))
            {
                Console.WriteLine();
                Console.WriteLine($"{"Entry point",-35}0x{end.EntryPoint:x16}");
                Console.WriteLine($"{"Program header offset",-35}0x{end.ProgramHeaderOffset:x16}");
                Console.WriteLine($"{"Section header offset",-35}0x{end.SectionHeaderOffset:x16}");
            }
        }
        else
        {
            var elfHeader64 = MemoryMarshal.Read<ElfHeader32>(fileData);

            end = elfHeader64.End;

            if (!options.HasFlag(Options.SuppressHeaders))
            {
                Console.WriteLine();
                Console.WriteLine($"{"Entry point",-35}0x{end.EntryPoint:x8}");
                Console.WriteLine($"{"Program header offset",-35}0x{end.ProgramHeaderOffset:x8}");
                Console.WriteLine($"{"Section header offset",-35}0x{end.SectionHeaderOffset:x8}");
            }
        }

        if (!options.HasFlag(Options.SuppressHeaders))
        {
            Console.WriteLine();
            Console.WriteLine($"{"Machine flags",-35}{end.Flags}");
            Console.WriteLine($"{"ELF header size",-35}{end.HeaderSize:N0} bytes");
            Console.WriteLine($"{"Program header entry size",-35}{end.ProgramHeaderEntrySize:N0} bytes");
            Console.WriteLine($"{"Number of program header entries",-35}{end.ProgramHeaderEntryCount:N0}");
            Console.WriteLine($"{"Section header entry size",-35}{end.SectionHeaderEntrySize:N0} bytes");
            Console.WriteLine($"{"Number of section header entries",-35}{end.SectionHeaderEntryCount:N0}");
            Console.WriteLine($"{"Section header string table index",-35}{end.SectionHeaderStringTableIndex}");
        }

        if ((options & (Options.ShowDependencies | Options.ShowDependencyTree | Options.ShowImports)) != 0)
        {
            ElfProgramHeader? dynamic = null;

            var programHeaders = new List<ElfProgramHeader>(end.ProgramHeaderEntryCount);

            static long VaToFile(ulong va, List<ElfProgramHeader> phs)
            {
                foreach (var ph in phs)
                {
                    ulong start = ph.Vaddr, end = ph.Vaddr + ph.Memsz;

                    if (va >= start && va < end)
                    {
                        return (long)(ph.Offset + (va - ph.Vaddr));
                    }
                }

                return -1;
            }

            for (ushort i = 0; i < end.ProgramHeaderEntryCount; i++)
            {
                var entryData = fileData.Slice((int)end.ProgramHeaderOffset + i * end.ProgramHeaderEntrySize, end.ProgramHeaderEntrySize);

                ElfProgramHeader programHeader;

                if (elf.Is64)
                {
                    var header = MemoryMarshal.Read<ElfProgramHeader64>(entryData);
                    programHeader = header.Parse(elf.IsLittleEndian);
                }
                else
                {
                    var header = MemoryMarshal.Read<ElfProgramHeader32>(entryData);
                    programHeader = header.Parse(elf.IsLittleEndian);
                }

                programHeaders.Add(programHeader);

                const int PT_DYNAMIC = 2;

                if (programHeader.Type == PT_DYNAMIC)
                {
                    dynamic = programHeader;
                }
            }

            if (dynamic is { } dynHeader)
            {
                var dynSpan = fileData.Slice((int)dynHeader.Offset, (int)dynHeader.Filesz);
                var neededOffsets = new List<ulong>();
                int step = elf.Is64 ? 16 : 8;

                for (int off = 0; off + step <= dynSpan.Length; off += step)
                {
                    long tag = elf.Is64
                        ? (long)ElfUInt64.Parse(dynSpan.Slice(off), elf.IsLittleEndian)
                        : (int)ElfUInt32.Parse(dynSpan.Slice(off), elf.IsLittleEndian);

                    ulong val = elf.Is64
                        ? ElfUInt64.Parse(dynSpan.Slice(off + 8), elf.IsLittleEndian)
                        : ElfUInt32.Parse(dynSpan.Slice(off + 4), elf.IsLittleEndian);

                    if (tag == 0)
                    {
                        break; // DT_NULL
                    }

                    switch (tag)
                    {
                        case 1: neededOffsets.Add(val); break;          // DT_NEEDED
                        case 5: break;                // DT_STRTAB (VA)
                        case 10: break;                  // DT_STRSZ
                        case 14: break;                 // DT_RPATH (deprecated)
                        case 29: break;               // DT_RUNPATH
                        case 14 + 2 /* DT_SONAME is 14? Actually DT_SONAME=14? */:
                        default:
                            if (tag == 14)
                            {
                            }

                            if (tag == 14 + 1)
                            {
                            }

                            break;
                    }
                }

                // Correct constants
                const long DT_NEEDED = 1;
                const long DT_STRTAB = 5;
                const long DT_STRSZ = 10;
                const long DT_SONAME = 14;
                const long DT_RPATH = 15;
                const long DT_RUNPATH = 29;

                // Re-scan with clean constants (some compilers prefer tidy switch)
                neededOffsets.Clear();
                ulong? runpathOff;
                ulong? sonameOff;
                ulong? rpathOff = runpathOff = sonameOff = null;
                ulong dtStrTabVA = 0;
                ulong dtStrSz = 0;
                for (int off = 0; off + step <= dynSpan.Length; off += step)
                {
                    long tag = elf.Is64 ? (long)MemoryMarshal.Read<ElfUInt64>(dynSpan.Slice(off)).Parse(elf.IsLittleEndian)
                                    : (int)MemoryMarshal.Read<ElfUInt32>(dynSpan.Slice(off)).Parse(elf.IsLittleEndian);
                    ulong val = elf.Is64 ? MemoryMarshal.Read<ElfUInt64>(dynSpan.Slice(off + (elf.Is64 ? 8 : 4))).Parse(elf.IsLittleEndian)
                                     : MemoryMarshal.Read<ElfUInt32>(dynSpan.Slice(off + 4)).Parse(elf.IsLittleEndian);

                    if (tag == 0)
                    {
                        break;
                    }

                    if (tag == DT_NEEDED)
                    {
                        neededOffsets.Add(val);
                    }
                    else if (tag == DT_STRTAB)
                    {
                        dtStrTabVA = val;
                    }
                    else if (tag == DT_STRSZ)
                    {
                        dtStrSz = val;
                    }
                    else if (tag == DT_SONAME)
                    {
                        sonameOff = val;
                    }
                    else if (tag == DT_RPATH)
                    {
                        rpathOff = val;
                    }
                    else if (tag == DT_RUNPATH)
                    {
                        runpathOff = val;
                    }
                }

                if (dtStrTabVA != 0 && dtStrSz != 0)
                {
                    // VA -> file offset for DT_STRTAB
                    long strtabFile = VaToFile(dtStrTabVA, programHeaders);

                    if (strtabFile >= 0)
                    {
                        string? soName = sonameOff.HasValue ? fileData.Slice((int)strtabFile + (int)sonameOff.Value).ReadNullTerminatedAsciiString() : null;
                        string? rpath = rpathOff.HasValue ? fileData.Slice((int)strtabFile + (int)rpathOff.Value).ReadNullTerminatedAsciiString() : null;
                        string? runpath = runpathOff.HasValue ? fileData.Slice((int)strtabFile + (int)runpathOff.Value).ReadNullTerminatedAsciiString() : null;

                        Console.WriteLine();
                        Console.WriteLine("Imported Modules:");

                        if (soName is not null)
                        {
                            Console.WriteLine($"{"soName",-35}{soName}");
                        }

                        if (rpath is not null)
                        {
                            Console.WriteLine($"{"rpath",-35}{rpath}");
                        }

                        if (runpath is not null)
                        {
                            Console.WriteLine($"{"runpath",-35}{runpath}");
                        }

                        Console.WriteLine();

                        Console.ForegroundColor = ConsoleColor.Cyan;

                        // Read needed strings
                        foreach (var off in neededOffsets)
                        {
                            var needed = fileData.Slice((int)strtabFile + (int)off).ReadNullTerminatedAsciiString();
                            Console.WriteLine(needed);
                        }

                        Console.ResetColor();

                        if (options.HasFlag(Options.ShowDependencyTree))
                        {
                            Console.WriteLine("Showing full dependency tree is not yet implemented for ELF files.");
                        }

                        if ((options & (Options.IncludeDelayedImports | Options.ShowImports)) != 0)
                        {
                            Console.WriteLine("Showing individual imported symbols is not yet implemented for ELF files.");
                        }
                    }
                }
            }
        }

        if (options.HasFlag(Options.ShowExports))
        {
            Console.WriteLine("Showing exported symbols is not yet implemented for ELF files.");
        }
    }
}

