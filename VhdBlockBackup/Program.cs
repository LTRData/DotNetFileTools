using DiscUtils;
using LTRData.Extensions.CommandLine;
using LTRData.Extensions.Formatting;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using System.Linq;

namespace VhdBlockBackup;

public static class Program
{
    private const int BlockSize = (int)(2 * Sizes.OneMiB);
    private const int HashSize = 16;

    public enum Operation
    {
        None,
        CreateMeta,
        Copy,
        Check
    };

    public static int Main(params string[] args)
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

        var cmds = CommandLineParser.ParseCommandLine(args, StringComparer.Ordinal);

        var operation = Operation.None;

        var copyWithCreateMeta = false;

        string? diffdir = null;

        if (!cmds.TryGetValue("", out var files))
        {
            return ShowHelp();
        }

        foreach (var cmd in cmds)
        {
            switch (cmd.Key)
            {
                case "createmeta":
                    if (cmds.ContainsKey("copy"))
                    {
                        copyWithCreateMeta = true;
                        break;
                    }

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

                case "diffdir":
                    if (cmd.Value.Length != 1
                        || !cmds.ContainsKey("copy"))
                    {
                        return ShowHelp();
                    }

                    diffdir = cmd.Value[0];
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
                    foreach (var file in files
                        .SelectMany(file =>
                        {
                            var path = Path.GetDirectoryName(file);

                            if (string.IsNullOrWhiteSpace(path))
                            {
                                path = ".";
                            }

                            return Directory.EnumerateFiles(path, Path.GetFileName(file));
                        }))
                    {
                        CreateMeta(file, blockCalulated: null, cancellationToken);
                    }

                    break;

                case Operation.Check:
                    CopyDiff(files[0], files[1], dryRun: true, diffdir: null, cancellationToken: cancellationToken);

                    break;

                case Operation.Copy:
                    if (copyWithCreateMeta)
                    {
                        CopyDiffWithCreateMeta(files[0], files[1], diffdir: diffdir, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        CopyDiff(files[0], files[1], dryRun: false, diffdir: diffdir, cancellationToken: cancellationToken);
                    }

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
#if DEBUG
            Console.WriteLine(ex);
#else
            Console.WriteLine(ex.JoinMessages());
#endif
            Console.ResetColor();
            return ex.HResult;
        }
    }

    private static void CopyDiff(string source, string target, bool dryRun, string? diffdir, CancellationToken cancellationToken)
    {
        var sourceBlockListFile = $"{source}.blocklist.bin";

        var targetBlockListFile = $"{target}.blocklist.bin";

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {sourceBlockListFile}...");

        var sourceMetafileTask = File.ReadAllBytesAsync(sourceBlockListFile, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {targetBlockListFile}...");

        var targetMetafile = File.ReadAllBytes(targetBlockListFile);

        var sourceMetafile = sourceMetafileTask.GetAwaiter().GetResult();

        if (sourceMetafile.Length != targetMetafile.Length)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(@$"Source and target meta files do not respresent the same image file size.
Indicated size of source image: {sourceMetafile.LongLength / HashSize * BlockSize}
Indicated size of target image: {targetMetafile.LongLength / HashSize * BlockSize}

If source size has changed, target size should also be changed to the exact
same size and target meta file regenerated before any --copy or --check
operation.");
            Console.ResetColor();

            return;
        }

        if (File.GetLastWriteTimeUtc(sourceBlockListFile) <
            File.GetLastWriteTimeUtc(source))
        {
            throw new InvalidOperationException($"File '{sourceBlockListFile}' is older than '{source}'");
        }

        if (File.GetLastWriteTimeUtc(targetBlockListFile) <
            File.GetLastWriteTimeUtc(target))
        {
            throw new InvalidOperationException($"File '{targetBlockListFile}' is older than '{target}'");
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

        Console.WriteLine($"Total allocated image size {totalSize} ({SizeFormatting.FormatBytes(totalSize)}), {diffBytes} bytes ({SizeFormatting.FormatBytes(diffBytes)}) to update, {100d * diffBytes / totalSize:0.0}% modified.");

        if (dryRun)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {source}...");

        using var sourceImage = VirtualDisk.OpenDisk(source, FileAccess.Read, useAsync: false);

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {target}...");

        using var targetImage = VirtualDisk.OpenDisk(target, FileAccess.Read, useAsync: false);

        var diff = Path.Join(diffdir ?? Path.GetDirectoryName(target), $"{Path.GetFileNameWithoutExtension(target.AsSpan())}_diff{Path.GetExtension(target.AsSpan())}");

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(diff))
        {
            Console.WriteLine($"Deleting existing {diff}...");
            File.Delete(diff);
        }

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Creating {diff}...");

        using var diffImage = targetImage.CreateDifferencingDisk(diff, useAsync: false);

        var sourceStream = sourceImage.Content;
        var targetStream = diffImage.Content;

        var buffer = new byte[BlockSize];

        var copiedBytes = 0L;

        var stopwatch = new Stopwatch();

        var messageUpdateTime = 0L;

        for (var i = 0; i < blocksTotal; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sourceMetafile.AsSpan(i * HashSize, HashSize)
                .SequenceEqual(targetMetafile.AsSpan(i * HashSize, HashSize)))
            {
                continue;
            }

            var messageUpdating = false;

            var position =
                sourceStream.Position =
                targetStream.Position = (long)i * BlockSize;

            if (stopwatch.IsRunning && copiedBytes > 0)
            {
                if (Environment.TickCount64 - messageUpdateTime > 400)
                {
                    var timeLeft = stopwatch.Elapsed * ((double)(diffBytes - copiedBytes) / copiedBytes);
                    var finishTime = DateTime.Now + timeLeft;
                    Console.Write($"Reading position {position}, {100d * copiedBytes / diffBytes:0.0}% done, estimated finish time {finishTime:yyyy-MM-dd HH:mm}...\r");
                    messageUpdateTime = Environment.TickCount64;
                    messageUpdating = true;
                }
            }
            else
            {
                stopwatch.Start();
                Console.Write($"Reading position {position}, {100d * copiedBytes / diffBytes:0.0}% done...                                                     \r");
                messageUpdating = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var read = sourceStream.Read(buffer, 0, buffer.Length);

            if (read == 0)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (messageUpdating)
            {
                Console.Write($"Writ\r");
            }

            targetStream.Write(buffer, 0, read);

            copiedBytes += read;
        }

        Console.WriteLine($@"
Finished, copied {copiedBytes} ({SizeFormatting.FormatBytes(copiedBytes)}) bytes of {diffBytes} ({SizeFormatting.FormatBytes(diffBytes)}) expected. Flushing target...");

        var flushTargetTask = targetStream.FlushAsync(cancellationToken);

        var diffMetafile = $"{diff}.blocklist.bin";

        Console.WriteLine($"Saving {diffMetafile}...");

        File.WriteAllBytes(diffMetafile, sourceMetafile);

        flushTargetTask.GetAwaiter().GetResult();
    }

    private static void CopyDiffWithCreateMeta(string source, string target, string? diffdir, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetBlockListFile = $"{target}.blocklist.bin";

        Console.WriteLine($"Opening {targetBlockListFile}...");

        var targetMetafile = File.ReadAllBytes(targetBlockListFile);

        if (File.GetLastWriteTimeUtc(targetBlockListFile) <
            File.GetLastWriteTimeUtc(target))
        {
            throw new InvalidOperationException($"File '{targetBlockListFile}' is older than '{target}'");
        }

        var totalSize = new FileInfo(source).Length;

	    var targetSize = targetMetafile.LongLength / HashSize * BlockSize;
	
        Console.WriteLine($"Total allocated image size {totalSize} ({SizeFormatting.FormatBytes(totalSize)}).");

        cancellationToken.ThrowIfCancellationRequested();

        using (var sourceDisk = VirtualDisk.OpenDisk(source, FileAccess.Read))
        {
            var sourceSize = sourceDisk.Content.Length;

            if (sourceSize != targetSize)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(@$"Target image file does not respresent the same virtual disk size as source image.
Virtual disk size of source image: {sourceSize}
Virtual disk size of target image: {targetSize}

If source size has changed, target size should also be changed to the exact
same size and target meta file regenerated before any --copy or --check
operation.");
                Console.ResetColor();

                return;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {target}...");

        using var targetImage = VirtualDisk.OpenDisk(target, FileAccess.Read, useAsync: false);

        var diff = Path.Join(diffdir ?? Path.GetDirectoryName(target), $"{Path.GetFileNameWithoutExtension(target.AsSpan())}_diff{Path.GetExtension(target.AsSpan())}");

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(diff))
        {
            Console.WriteLine($"Deleting existing {diff}...");
            File.Delete(diff);
        }

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Creating {diff}...");

        using var diffImage = targetImage.CreateDifferencingDisk(diff, useAsync: false);

        var targetStream = diffImage.Content;

        var copiedBytes = 0L;

        var sourceMetafile = CreateMeta(source, BlockCalculated, cancellationToken);

        void BlockCalculated(ReadOnlySpan<byte> checksum, ReadOnlySpan<byte> block, long position)
        {
            var targetMetaFilePosition = (int)(position / BlockSize * HashSize);

            var tagetChecksum = targetMetafile.AsSpan(targetMetaFilePosition, HashSize);

            if (checksum.SequenceEqual(tagetChecksum))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            Console.Write($"Writing position {position}\r");

            lock (targetStream)
            {
                targetStream.Position = position;
                targetStream.Write(block);
            }

            copiedBytes += block.Length;
        }

        Console.WriteLine($@"
Finished, copied {copiedBytes} ({SizeFormatting.FormatBytes(copiedBytes)}) bytes. Flushing target...");

        var flushTargetTask = targetStream.FlushAsync(cancellationToken);

        var diffMetafile = $"{diff}.blocklist.bin";

        Console.WriteLine($"Saving {diffMetafile}...");

        File.WriteAllBytes(diffMetafile, sourceMetafile);

        flushTargetTask.GetAwaiter().GetResult();
    }

    private delegate void BlockCalculatedDelegate(ReadOnlySpan<byte> checksum, ReadOnlySpan<byte> block, long position);

    private static byte[] CreateMeta(string file, BlockCalculatedDelegate? blockCalulated, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Opening {file}...");

        using var image = VirtualDisk.OpenDisk(file, FileAccess.Read, useAsync: false);

        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"Creating {file}.blocklist.bin...");

        using var metafile = File.Create($"{file}.blocklist.bin");

        var stream = image.Content;

        var parallelCount = Math.Max(Environment.ProcessorCount / 2, 1);

        Console.WriteLine($"Using {parallelCount} threads x {BlockSize} bytes block size.");

        var buffer = new byte[BlockSize * parallelCount];

        var blockCount = (int)((stream.Length + BlockSize - 1) / BlockSize);

        var checksums = new byte[HashSize * blockCount];

        var sizeTotal = stream.Length;

        var stopwatch = new Stopwatch();

        var messageUpdateTime = 0L;

        for (; ;)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var position = stream.Position;

            if (stopwatch.IsRunning && position > 0)
            {
                if (Environment.TickCount64 - messageUpdateTime > 400)
                {
                    var timeLeft = stopwatch.Elapsed * ((double)(sizeTotal - position) / position);
                    var finishTime = DateTime.Now + timeLeft;
                    Console.Write($"Reading position {position} of {sizeTotal}, {100d * position / sizeTotal:0.0}% done, estimated finish time {finishTime:yyyy-MM-dd HH:mm}...\r");
                    messageUpdateTime = Environment.TickCount64;
                }
            }
            else
            {
                stopwatch.Start();
                Console.Write($"Reading position {position} of {sizeTotal}, {100d * position / sizeTotal:0.0}% done...\r");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var read = stream.Read(buffer, 0, buffer.Length);

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
                Span<byte> checksum = checksums.AsSpan((blockNumber + i) * HashSize, HashSize);
                hash[..HashSize].CopyTo(checksum);

                blockCalulated?.Invoke(checksum, span, position + (i * BlockSize));
            });

            if (read < BlockSize)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        metafile.Write(checksums, 0, checksums.Length);

        Console.WriteLine($@"
Finished at {stream.Position} of {sizeTotal}, {100d * stream.Position / sizeTotal:0.0}%. Flushing output...");

        return checksums;
    }

    private static int ShowHelp()
    {
        Console.WriteLine(@$"Disk image block based backup tool, version 1.0
Copyright (c) Olof Lagerkvist, LTR Data, 2023 - 2025

This backup tool uses lists of block checksums for virtual machine disk image
files. A block checksum list file (called metafile) created at source location
is compared to corresponding file and target location to calculate amount of
data that differs since last backup. That data is then transferred to a
differencing image file with target image file as parent.

Syntax:

VhdBlockBackup --createmeta file1.vhdx [file2.vhdx ...]
    Creates metadata file with block checksums. This is first step to prepare
    for backup operations. With this operation, file names can contain
    wildcards.

VhdBlockBackup --check source.vhdx target.vhdx
    Displays information about how much data would be copied with a --copy
    operation. Also checks that meta data indicate compatible image file
    sizes.

VhdBlockBackup --copy [--createmeta] [--diffdir=directory] source.vhdx target.vhdx
    Copies modified blocks from source.vhdx file to a new target_diff.vhdx.
    The new diff image file will be created with target.vhdx as parent so that
    modifications can be easily merged into target.vhdx later.

    Note that meta block lists must already be present and up to date when
    running --copy. If combined with --createmeta, source image block list
    does not need to already exist. Instead, it will be created and blocks
    detected to be different from target image will be copied during the
    process. This could save time, particularly for images with large amounts
    of changes.

    Directory specified with --diffdir can be a separate directory where
    target_diff.vhdx will be created.");

        return 100;
    }
}
