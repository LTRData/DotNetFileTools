/// splitbystr - Split files by token string
/// Copyright(c) 2022 Olof Lagerkvist, LTR Data. http://ltr-data.se

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LTR.splitbystr;

public static class Program
{
    public static async Task<int> Main(params string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            Console.WriteLine(@"splitbystr - Split files by token string
Copyright (c) 2022 Olof Lagerkvist, LTR Data. http://ltr-data.se

Syntax:
splitbystr inFile tokenString [outFilePattern]
-- or, to read input from standard input --
splitbystr - tokenString outFilePattern

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
            // Cancel the canellation token when user press Ctrl+C in console,
            // or when termination signal is sent to process (Linux).
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

        // A local function that opens next output file
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
                // Read a block of data
                var result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);

                var buffer = result.Buffer;

                if (buffer.Length == 0)
                {
                    break;
                }

                // Does first character of token exist in the buffer?
                var tokenPosition = result.Buffer.PositionOf(token[0]);

                if (!tokenPosition.HasValue)
                {
                    // If not, we consume the entire buffer, write it to output file
                    // and continue from start with reading again
                    var start = buffer.Start;

                    while (buffer.TryGet(ref start, out var memory, advance: true))
                    {
                        await outStream.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                    }

                    pipeReader.AdvanceTo(buffer.End);

                    continue;
                }

                // First write out data found before the token character so that we
                // do not need to care about that piece of the buffer further down
                var prefix = buffer.Slice(buffer.Start, tokenPosition.Value);

                var prefixStart = buffer.Start;

                while (prefix.Length > 0
                    && prefix.TryGet(ref prefixStart, out var memory, advance: true))
                {
                    await outStream.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                }

                // The remainder of the buffer could potentially be for next output
                // file
                var nextFileSlice = buffer.Slice(tokenPosition.Value);

                if (nextFileSlice.Length < token.Length && !result.IsCompleted)
                {
                    // But if not enough data has been read to check whether this is a complete
                    // token, we need to continue from start again and read in some more
                    pipeReader.AdvanceTo(tokenPosition.Value, buffer.GetPosition(nextFileSlice.Length, tokenPosition.Value));

                    continue;
                }

                if (nextFileSlice.Length >= token.Length)
                {
                    var tokenSlice = nextFileSlice.Slice(0, token.Length);

                    // If it was not actually a token (just first character match), write it to output file,
                    // advance the buffer and continue from start to read more
                    if (!tokenSlice.SequenceEqual(token))
                    {
                        await outStream.WriteAsync(token.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);

                        pipeReader.AdvanceTo(buffer.GetPosition(1, tokenPosition.Value));

                        continue;
                    }

                    // A token was found, time to switch to next output file
                    Console.WriteLine($"Wrote {outStream.Length} bytes.");
                    outStream.Close();
                    outStream = null;
                    outStream = OpenNextFile();

                    // Write out remaining buffer to new file, advance buffer and continue from
                    // start to read more
                    await outStream.WriteAsync(token, cancellationToken).ConfigureAwait(false);

                    pipeReader.AdvanceTo(buffer.GetPosition(token.Length, tokenPosition.Value));

                    continue;
                }

                // So, no token characters found. Just write output data, advance buffer and go
                // read some more
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

    /// <summary>
    /// Efficient way to compare bytes in a ReadOnlySequence with an array
    /// </summary>
    /// <param name="tokenSlice">ReadOnlySequence of bytes</param>
    /// <param name="token">Byte array to compare with</param>
    /// <returns>True or false depending on whether sequences are equal</returns>
    /// <exception cref="IndexOutOfRangeException">The array is shorter than sequence</exception>
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

#if NETFRAMEWORK  // Legacy stuff for compatibility with .NET Framework 4.x. Not needed in .NET Core or .NET 5+
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
