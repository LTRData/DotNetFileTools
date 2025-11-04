using Arsenal.ImageMounter.IO.Native;
using System.Runtime.InteropServices;

namespace peinfo;

public static class ELFViewer
{
    public static void ProcessELFFile(ReadOnlySpan<byte> fileData)
    {
        var elf = MemoryMarshal.Read<ElfHeader>(fileData);

        Console.WriteLine();
        Console.WriteLine("ELF header:");
        Console.WriteLine($"{"Machine",-35}{elf.Machine}");
        Console.WriteLine($"{"Type",-35}{elf.Type}");
        Console.WriteLine($"{"Class",-35}{elf.cls}");
        Console.WriteLine($"{"Data encoding",-35}{elf.data}");
        Console.WriteLine($"{"Version",-35}{elf.version}");
        Console.WriteLine($"{"OS/ABI",-35}{elf.osabi}");
        Console.WriteLine($"{"ABI version",-35}{elf.abiversion}");

        ElfHeaderEnd? headerEnd = null;

        if (elf.cls == ElfClass.ELFCLASS32)
        {
            var elf32 = MemoryMarshal.Read<ElfHeader32>(fileData);

            Console.WriteLine();
            Console.WriteLine($"{"Entry point",-35}0x{elf32.EntryPoint:x8}");
            Console.WriteLine($"{"Program header offset",-35}0x{elf32.ProgramHeaderOffset:x8}");
            Console.WriteLine($"{"Section header offset",-35}0x{elf32.SectionHeaderOffset:x8}");

            headerEnd = elf32.End;
        }
        else if (elf.cls == ElfClass.ELFCLASS64)
        {
            var elf64 = MemoryMarshal.Read<ElfHeader64>(fileData);

            Console.WriteLine();
            Console.WriteLine($"{"Entry point",-35}0x{elf64.EntryPoint:x16}");
            Console.WriteLine($"{"Program header offset",-35}0x{elf64.ProgramHeaderOffset:x16}");
            Console.WriteLine($"{"Section header offset",-35}0x{elf64.SectionHeaderOffset:x16}");

            headerEnd = elf64.End;
        }

        if (headerEnd is { } end)
        {
            Console.WriteLine();
            Console.WriteLine($"{"Flags",-35}0x{end.Flags:x8}");
            Console.WriteLine($"{"ELF header size",-35}{end.HeaderSize:N0} bytes");
            Console.WriteLine($"{"Program header entry size",-35}{end.ProgramHeaderEntrySize:N0} bytes");
            Console.WriteLine($"{"Number of program header entries",-35}{end.ProgramHeaderEntryCount:N0}");
            Console.WriteLine($"{"Section header entry size",-35}{end.SectionHeaderEntrySize:N0} bytes");
            Console.WriteLine($"{"Number of section header entries",-35}{end.SectionHeaderEntryCount:N0}");
            Console.WriteLine($"{"Section header string table index",-35}{end.SectionHeaderStringTableIndex}");
        }
    }
}
