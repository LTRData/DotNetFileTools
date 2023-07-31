using DiscUtils;
using LTRLib.Extensions;
using LTRLib.IO;
using LTRLib.LTRGeneric;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VhdBlockBackup;

public static class Program
{
    public enum Operation
    {
        None,
        CreateMeta,
        Copy,
        Check
    };

    public static async Task<int> Main(params string[] args)
    {
        using var tokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine(@"
Aborting...");

            tokenSource.Cancel();

            e.Cancel = true;
        };

        var cancellationToken = tokenSource.Token;

        var cmds = StringSupport.ParseCommandLine(args, StringComparer.Ordinal);

        var operation = Operation.None;

        if (!cmds.TryGetValue("", out var files))
        {
            return ShowHelp();
        }

        foreach (var cmd in cmds)
        {
            switch (cmd.Key)
            {
                case "createmeta":
                    if (operation != Operation.None ||
                        files.Length == 0)
                    {
                        return ShowHelp();
                    }
                    operation = Operation.CreateMeta;
                    break;

                case "check":
                    if (operation != Operation.None ||
                        files.Length != 2)
                    {
                        return ShowHelp();
                    }
                    operation = Operation.Check;
                    break;

                case "copy":
                    if (operation != Operation.None ||
                        files.Length != 2)
                    {
                        return ShowHelp();
                    }
                    operation = Operation.Copy;
                    break;

                case "":
                    break;

                default:
                    return ShowHelp();
            }
        }

        DiscUtils.Containers.SetupHelper.SetupContainers();

        try
        {
            switch (operation)
            {
                case Operation.CreateMeta:
                    foreach (var file in files)
                    {
                        await CreateMetaAsync(file, cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case Operation.Check:
                    await CopyDiffAsync(files[0], files[1], dryRun: true, cancellationToken).ConfigureAwait(false);

                    break;

                case Operation.Copy:
                    await CopyDiffAsync(files[0], files[1], dryRun: false, cancellationToken).ConfigureAwait(false);

                    break;

                default:
                    return ShowHelp();
            }

            Console.WriteLine("Done.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.JoinMessages());
            Console.ResetColor();
            return ex.HResult;
        }
    }

    private static async Task CopyDiffAsync(string source, string target, bool dryRun, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {source}.blocklist.bin...");

        var sourceMetafileTask = File.ReadAllBytesAsync($"{source}.blocklist.bin", cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {target}.blocklist.bin...");

        var targetMetafileTask = File.ReadAllBytesAsync($"{target}.blocklist.bin", cancellationToken);

        var sourceMetafile = await sourceMetafileTask.ConfigureAwait(false);
        var targetMetafile = await targetMetafileTask.ConfigureAwait(false);

        if (sourceMetafile.Length != targetMetafile.Length)
        {
            Console.WriteLine(@$"Source and target meta files do not respresent the same image file size.
Indicated size of source image: {sourceMetafile.LongLength / HashSize * BlockSize}
Indicated size of target image: {targetMetafile.LongLength / HashSize * BlockSize}");

            return;
        }

        var diffBytes = 0L;

        var blocksTotal = sourceMetafile.Length / HashSize;

        for (var i = 0; i < blocksTotal; i++)
        {
            if (!sourceMetafile.AsSpan(i * HashSize, HashSize)
                .SequenceEqual(targetMetafile.AsSpan(i * HashSize, HashSize)))
            {
                diffBytes += BlockSize;
            }
        }

        if (diffBytes == 0)
        {
            Console.WriteLine("No changes detected.");
            return;
        }

        var totalSize = new FileInfo(source).Length;

        Console.WriteLine($"{diffBytes} bytes ({StringSupport.FormatBytes(diffBytes)}) to update of {totalSize} ({StringSupport.FormatBytes(totalSize)}), {100d * diffBytes / totalSize:0.0}% modified.");

        if (dryRun)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {source}...");

        using var sourceImage = VirtualDisk.OpenDisk(source, FileAccess.Read, useAsync: true);

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {target}...");

        using var targetImage = VirtualDisk.OpenDisk(target, FileAccess.Read, useAsync: true);

        var diff = Path.Join(Path.GetDirectoryName(target).AsSpan(), $"{Path.GetFileNameWithoutExtension(target.AsSpan())}_diff{Path.GetExtension(target.AsSpan())}");

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Creating {diff}...");

        using var diffImage = targetImage.CreateDifferencingDisk(diff, useAsync: true);

        var sourceStream = sourceImage.Content;
        var targetStream = diffImage.Content;

        var buffer = new byte[BlockSize];

        var copiedBytes = 0L;

        var stopWatch = new Stopwatch();

        for (var i = 0; i < blocksTotal; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sourceMetafile.AsSpan(i * HashSize, HashSize).SequenceEqual(targetMetafile.AsSpan(i * HashSize, HashSize)))
            {
                continue;
            }

            var position =
                sourceStream.Position =
                targetStream.Position = (long)i * BlockSize;

            if (stopWatch.IsRunning && copiedBytes > 0)
            {
                var timeLeft = stopWatch.Elapsed * ((double)(diffBytes - copiedBytes) / copiedBytes);
                var finishTime = DateTime.Now + timeLeft;
                Console.Write($"Reading position {position}, {100d * copiedBytes / diffBytes:0.0}% done, estimated finish time {finishTime}...\r");
            }
            else
            {
                stopWatch.Start();
                Console.Write($"Reading position {position}, {100d * copiedBytes / diffBytes:0.0}% done...\r");
            }

            var read = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            Console.Write($"Writ\r");

            var span = buffer.AsMemory(0, read);

            await targetStream.WriteAsync(span, cancellationToken).ConfigureAwait(false);

            copiedBytes += span.Length;
        }

        Console.WriteLine($@"
Finished, copied {copiedBytes} ({StringSupport.FormatBytes(copiedBytes)}) bytes of {diffBytes} ({StringSupport.FormatBytes(diffBytes)}) expected. Flushing target...");

        var flushTargetTask = targetStream.FlushAsync(cancellationToken);

        var diffMetafile = $"{diff}.blocklist.bin";

        Console.WriteLine($"Saving {diffMetafile}...");

        var targetBlockListTask = File.WriteAllBytesAsync(diffMetafile, sourceMetafile, cancellationToken);

        await Task.WhenAll(flushTargetTask, targetBlockListTask).ConfigureAwait(false);
    }

    private const int BlockSize = 2 << 20;
    private const int HashSize = 16;

    private static async Task CreateMetaAsync(string file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {file}...");

        using var image = VirtualDisk.OpenDisk(file, FileAccess.Read, useAsync: true);

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Creating {file}.blocklist.bin...");

        using var metafile = File.Create($"{file}.blocklist.bin");

        var stream = image.Content;

        var parallelCount = Environment.ProcessorCount / 2;

        Console.WriteLine($"Using {parallelCount} threads x {BlockSize} bytes block size.");

        var buffer = new byte[BlockSize * parallelCount];

        var blockCount = (stream.Length + BlockSize - 1) / BlockSize;

        var checksums = new byte[HashSize * blockCount];

        var sizeTotal = stream.Length;

        var stopWatch = new Stopwatch();

        for (; ;)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var position = stream.Position;

            if (stopWatch.IsRunning && position > 0)
            {
                var timeLeft = stopWatch.Elapsed * ((double)(sizeTotal - position) / position);
                var finishTime = DateTime.Now + timeLeft;
                Console.Write($"Reading position {position} of {sizeTotal}, {100d * position / sizeTotal:0.0}% done, estimated finish time {finishTime}...\r");
            }
            else
            {
                stopWatch.Start();
                Console.Write($"Reading position {position} of {sizeTotal}, {100d * position / sizeTotal:0.0}% done...\r");
            }

            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            var iterations = (read + BlockSize - 1) / BlockSize;

            var blockNumber = (int)(position / BlockSize);

            Parallel.For(0, iterations, i =>
            {
                var start = i * BlockSize;
                var end = Math.Min((i + 1) * BlockSize, read);
                var span = buffer.AsSpan()[start..end];
                Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
                SHA1.HashData(span, hash);
                hash[..HashSize].CopyTo(checksums.AsSpan((blockNumber + i) * HashSize));
            });

            if (read < BlockSize)
            {
                break;
            }
        }

        await metafile.WriteAsync(checksums, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($@"Finished at {stream.Position} of {sizeTotal}, {100d * stream.Position / sizeTotal:0.0}%. Flushing output...");
    }

    private static int ShowHelp()
    {
        Console.WriteLine(@$"Syntax:
VhdBlockBackup --createmeta file1 [file2 ...]
    Creates metadata file with block checksums.

VhdBlockBackup --check source.vhdx target.vhdx
    Displays information about how much data would be copied with a --copy
    operation. Also checks that meta data indicate compatible image file
    sizes.

VhdBlockBackup --copy source.vhdx target.vhdx
    Copies modified blocks from one vhdx file to another. Meta block lists
    must already be present and up to date.");

        return 100;
    }
}
