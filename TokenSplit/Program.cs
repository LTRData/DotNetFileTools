/// TokenSplit - Split files by token string
/// Copyright(c) 2022 Olof Lagerkvist, LTR Data. http://ltr-data.se

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LTR.TokenSplit;

public static class Program
{
    public static async Task<int> Main(params string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            Console.WriteLine(@"TokenSplit - Split files by token string
Copyright (c) 2022 Olof Lagerkvist, LTR Data. http://ltr-data.se

Syntax:
TokenSplit inFile tokenString [outFilePattern]
-- or, to read input from standard input --
TokenSplit - tokenString outFilePattern

Each output file will have .001, .002 etc added to the file name.
");

            return 1;
        }

        var inFile = args[0];

        var token = Encoding.UTF8.GetBytes(args[1]);

        string? outFilePattern = null;

        if (args.Length >= 3)
        {
            outFilePattern = args[2];
        }

        using var cancellationTokenSource = new CancellationTokenSource();

        try
        {
            Console.CancelKeyPress += (_, e) =>
            {
                cancellationTokenSource.Cancel();
                e.Cancel = true;
            };

            if (inFile == "-")
            {
                if (outFilePattern is null)
                {
                    throw new InvalidOperationException("Needs output file pattern when reading from stdin");
                }

                using var pipe = Console.OpenStandardInput();

                await ProcessFileAsync(pipe, token, outFilePattern, cancellationTokenSource.Token).ConfigureAwait(false);

                return 0;
            }

            outFilePattern ??= inFile;

            using var inStream = File.OpenRead(inFile);

            await ProcessFileAsync(inStream, token, outFilePattern, cancellationTokenSource.Token).ConfigureAwait(false);

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;

            for (var e = ex; e is not null; e = e.InnerException)
            {
                Console.WriteLine(e.Message);
            }

            Console.ResetColor();

            return ex.HResult;
        }
    }

    private static async Task ProcessFileAsync(Stream inStream, byte[] token, string outFilePattern, CancellationToken cancellationToken)
    {
        var pipeReader = PipeReader.Create(inStream);

        var fileNumber = 1;

        FileStream OpenNextFile()
        {
            var outFile = $"{outFilePattern}.{fileNumber++:000}";
            
            Console.WriteLine($"Writing {outFile}...");
            
            return new FileStream(outFile,
                                  FileMode.Create,
                                  FileAccess.Write,
                                  FileShare.None,
                                  bufferSize: 4096,
                                  FileOptions.Asynchronous);
        }

        var outStream = OpenNextFile();

        try
        {
            for (; ; )
            {
                var result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);

                var buffer = result.Buffer;

                if (buffer.Length == 0)
                {
                    break;
                }

                var tokenPosition = result.Buffer.PositionOf(token[0]);

                if (!tokenPosition.HasValue)
                {
                    var start = buffer.Start;

                    while (buffer.TryGet(ref start, out var memory, advance: true))
                    {
                        await outStream.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                    }

                    pipeReader.AdvanceTo(buffer.End);

                    continue;
                }

                var prefix = buffer.Slice(buffer.Start, tokenPosition.Value);

                var prefixStart = buffer.Start;

                while (prefix.Length > 0
                    && prefix.TryGet(ref prefixStart, out var memory, advance: true))
                {
                    await outStream.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                }

                var nextFileSlice = buffer.Slice(tokenPosition.Value);

                if (nextFileSlice.Length < token.Length && !result.IsCompleted)
                {
                    pipeReader.AdvanceTo(tokenPosition.Value, buffer.GetPosition(nextFileSlice.Length, tokenPosition.Value));

                    continue;
                }

                if (nextFileSlice.Length >= token.Length)
                {
                    var tokenSlice = nextFileSlice.Slice(0, token.Length);

                    if (!tokenSlice.SequenceEqual(token))
                    {
                        await outStream.WriteAsync(token.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);

                        pipeReader.AdvanceTo(buffer.GetPosition(1, tokenPosition.Value));

                        continue;
                    }

                    Console.WriteLine($"Wrote {outStream.Length} bytes.");
                    outStream.Close();
                    outStream = null;
                    outStream = OpenNextFile();

                    await outStream.WriteAsync(token, cancellationToken).ConfigureAwait(false);

                    pipeReader.AdvanceTo(buffer.GetPosition(token.Length, tokenPosition.Value));

                    continue;
                }

                var nextFileStart = nextFileSlice.Start;

                while (nextFileSlice.TryGet(ref nextFileStart, out var memory, advance: true))
                {
                    await outStream.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                }

                if (result.IsCompleted)
                {
                    break;
                }

                pipeReader.AdvanceTo(buffer.End);
            }

            Console.WriteLine(@$"Wrote {outStream.Length} bytes.
Done.");
        }
        finally
        {
            outStream?.Close();
        }
    }

    private static bool SequenceEqual(this ReadOnlySequence<byte> tokenSlice, byte[] token)
    {
        var offset = 0;

        foreach (var memory in tokenSlice)
        {
            foreach (var b in memory.Span)
            {
                if (b != token[offset++])
                {
                    return false;
                }
            }
        }

        return true;
    }

#if NETFRAMEWORK  // Legacy stuff for compatibility with .NET Framework 4.x
    private static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (MemoryMarshal.TryGetArray(buffer, out var arraySegment))
        {
            return new(stream.WriteAsync(arraySegment.Array!, arraySegment.Offset, arraySegment.Count, cancellationToken));
        }

        return WriteUsingTemporaryArrayAsync(stream, buffer, cancellationToken);
    }

    private static async ValueTask WriteUsingTemporaryArrayAsync(Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var bytes = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(bytes);
            await stream.WriteAsync(bytes, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }
#endif

}
