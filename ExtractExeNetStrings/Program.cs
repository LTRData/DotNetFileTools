using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ExtractExeNetStrings;

public static class Program
{
    //static readonly string test_string = "beg ' \\ \u2028 \u2029 \0 \t \n \r \" end";

    public static int Main(params string[] args)
    {
        foreach (var exePath in args)
        {
            try
            {
                foreach (var UserString in ReadAllUserStrings(exePath))
                {
                    Console.WriteLine(UserString);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
                return -1;
            }
        }

        return 0;
    }

    static IEnumerable<string> ReadAllUserStrings(string exePath)
    {
        using var r = new BinaryReader(new FileStream(exePath, FileMode.Open, FileAccess.Read));

        for (; ; )
        {
            do
            {
                if (r.BaseStream.Position >= r.BaseStream.Length - 16)
                {
                    throw new BadImageFormatException("Unsupported file format");
                }
            }
            while (r.ReadUInt32() != 0x424A5342);        // seek to magic



            var pos = r.BaseStream.Position;

            using var en = ReadAllUserStringsFromMetadata(r).GetEnumerator();

            for (; ; )
            {
                try
                {
                    if (!en.MoveNext())
                    {
                        yield break;
                    }
                }
                catch
                {
                    break;
                }

                yield return en.Current;
            }

            r.BaseStream.Position = pos;
        }
    }

    static IEnumerable<string> ReadAllUserStringsFromMetadata(BinaryReader r)
    {
        var metadataRootPos = r.BaseStream.Position - 4;

        r.ReadUInt32();                     // Major, Minor Version

        if (r.ReadUInt32() != 0)            // Reserved
        {
            throw new BadImageFormatException("Unsupported file format");
        }

        var length = r.ReadUInt32();       // Length

        r.BaseStream.Position += length;    // skip Version string

        if (r.ReadUInt16() != 0)            // Flags, Reserved
        {
            throw new BadImageFormatException("Unsupported file format");
        }

        int streams = r.ReadUInt16();       // Streams

        while (streams > 0)                 // StreamHeaders
        {
            streams--;

            if (ReadStreamHeader(r, out var offset, out var size) == "#US")
            {
                r.BaseStream.Position = metadataRootPos + offset;
                var endPos = metadataRootPos + offset + size;

                if (ReadUserString(r) is not null)
                {
                    throw new BadImageFormatException("Unsupported file format");
                }

                while (r.BaseStream.Position < endPos)
                {
                    var str = ReadUserString(r);

                    if (str is not null)
                    {
                        yield return CSStringConverter.Convert(str);
                    }
                }

                yield break;
            }
        }

        throw new BadImageFormatException("Unsupported file format.");
    }

    static string ReadStreamHeader(BinaryReader r, out uint offset, out uint size)
    {
        offset = r.ReadUInt32();
        size = r.ReadUInt32();

        var cc = 0;
        var name = "";

        while (true)
        {
            var b = r.ReadByte();
            cc++;

            if (b == 0)
            {
                while (cc % 4 != 0)
                {
                    if (r.ReadByte() != 0)
                    {
                        throw new BadImageFormatException("Unsupported file format.");
                    }

                    cc++;
                }

                if (cc > 32)
                {
                    throw new BadImageFormatException("Unsupported file format.");
                }

                return name;
            }

            name += (char)b;
        }
    }

    static string ReadUserString(BinaryReader r)
    {
        int b = r.ReadByte();

        int size;

        if ((b & 0x80) == 0)
        {
            size = b;
        }
        else if ((b & 0xC0) == 0x80)
        {
            int x = r.ReadByte();

            size = ((b & ~0xC0) << 8) | x;
        }
        else if ((b & 0xE0) == 0xC0)
        {
            int x = r.ReadByte();
            int y = r.ReadByte();
            int z = r.ReadByte();

            size = ((b & ~0xE0) << 24) | (x << 16) | (y << 8) | z;
        }
        else
        {
            throw new BadImageFormatException("Unsupported file format");
        }

        if (size == 0)
        {
            return null;
        }

        if (size % 2 != 1)
        {
            throw new BadImageFormatException("Unsupported file format");
        }

        var charCnt = size / 2;

        var sb = new StringBuilder(charCnt);

        for (var i = 0; i < charCnt; i++)
        {
            sb.Append((char)r.ReadUInt16());
        }

        var finalByte = r.ReadByte();

        if (finalByte is not 0 and not 1)
        {
            throw new BadImageFormatException("Unsupported file format.");
        }

        return sb.ToString();
    }
}

static class CSStringConverter
{
    public static string Convert(string value)
    {
        var sb = new StringBuilder(value.Length + 10);

        sb.Append('"');

        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                //case '\'':
                //    b.Append("\\'");
                //    break;

                case '\\':
                    sb.Append("\\\\");
                    break;

                case '\x2028':
                case '\x2029':
                    sb.Append(EscapeChar(value[i]));
                    break;

                case char.MinValue:
                    sb.Append("\\0");
                    break;

                case '\t':
                    sb.Append("\\t");
                    break;

                case '\n':
                    sb.Append("\\n");
                    break;

                case '\r':
                    sb.Append("\\r");
                    break;

                case '"':
                    sb.Append("\\\"");
                    break;

                default:
                    sb.Append(value[i]);
                    break;
            }
        }

        sb.Append('"');

        return sb.ToString();
    }

    private static string EscapeChar(char value) => $"\\u{((int)value).ToString("X4", CultureInfo.InvariantCulture)}";
}
