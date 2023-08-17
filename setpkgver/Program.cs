using LTRLib.LTRGeneric;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SetFixedPackagesNumbers;

public static class Program
{
    private static readonly SourceCacheContext cache = new();

    public static async Task<int> Main(params string[] args)
    {
        try
        {
            return await UnsafeMain(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(ex.Message);
            Console.ResetColor();
            return ex.HResult;
        }
    }

    public static async Task<int> UnsafeMain(params string[] args)
    {
        var cmds = StringSupport.ParseCommandLine(args, StringComparer.Ordinal);

        var setExplicit = false;
        var upgradeAll = false;
        var execute = false;
        var listAll = false;
        Regex[] packageFilters = Array.Empty<Regex>();

        foreach (var cmd in cmds)
        {
            switch (cmd.Key)
            {
                // Only change * versions to currently latest version explicitly
                case "explicit":
                    Debug.Assert(cmd.Value.Length == 0, "--explicit option cannot have values");
                    Debug.Assert(!upgradeAll, "Cannot set both --upgrade and --explicit");
                    setExplicit = true;
                    break;

                    // Only upgrade packages with an explicit version number set
                case "upgrade":
                    Debug.Assert(cmd.Value.Length == 0, "--upgrade option cannot have values");
                    Debug.Assert(!setExplicit, "Cannot set both --upgrade and --explicit");
                    upgradeAll = true;
                    break;

                    // Save changes
                case "exec":
                    Debug.Assert(cmd.Value.Length == 0, "--exec option cannot have values");
                    execute = true;
                    break;

                    // Regex package name filter
                case "name":
                    Debug.Assert(cmd.Value.Length >= 1, "--name= option requires regex values");
                    packageFilters = Array.ConvertAll(cmd.Value, val => new Regex(val, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                    break;

                    // List all found package names
                case "list":
                    Debug.Assert(cmd.Value.Length == 0, "--list option cannot have values");
                    listAll = true;
                    break;

                case "":
                    break;

                default:
                    throw new InvalidOperationException($"Unknown option: {cmd.Key}");
            }
        }

        if (!cmds.TryGetValue("", out var files))
        {
            throw new InvalidOperationException("Missing file names");
        }

        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>().ConfigureAwait(false);
        var packageVersions = new ConcurrentDictionary<string, NuGetVersion?>(StringComparer.Ordinal);

        foreach (var arg in files)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Loading '{arg}'...");
            Console.ResetColor();

            var xmlDoc = XElement.Load(arg);

            var packageReferences = xmlDoc.Descendants("ItemGroup")
                .Elements("PackageReference")
                .Where(e => e.Attribute("Include") is not null);

            foreach (var packageFilter in packageFilters)
            {
                packageReferences = packageReferences.
                    Where(e => packageFilter.IsMatch(e.Attribute("Include")!.Value));
            }

            if (setExplicit)
            {
                packageReferences = packageReferences.
                    Where(e => e.Attribute("Version")?.Value == "*");
            }

            if (upgradeAll)
            {
                packageReferences = packageReferences.
                    Where(e => e.Attribute("Version")?.Value != "*");
            }

            var modified = false;

            foreach (var node in packageReferences)
            {
                var packageName = node.Attribute("Include")!.Value;

                var version = node.Attribute("Version")?.Value;

                var latestVersion = packageVersions.GetOrAdd(packageName,
                    packageName =>
                    {
                        var versions = resource.GetAllVersionsAsync(packageName, cache, NullLogger.Instance, CancellationToken.None).Result;

                        var latestVersion = versions.OrderByDescending(v => v.IsPrerelease).ThenBy(v => v).LastOrDefault();

                        return latestVersion;
                    });

                if (latestVersion is null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    if (version is null)
                    {
                        Console.WriteLine($"{packageName}: Currently unspecified version, not found on server. Setting as *");

                        node.SetAttributeValue("Version", "*");

                        modified = true;
                    }
                    else
                    {
                        Console.WriteLine($"{packageName}: Currently {version}, not found on server");
                    }
                    Console.ResetColor();
                }
                else if (!NuGetVersion.TryParse(version, out var existingVer) || existingVer != latestVersion)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{packageName}: Currently {version}, setting {latestVersion}");
                    Console.ResetColor();

                    node.SetAttributeValue("Version", latestVersion);

                    modified = true;
                }
                else if (listAll)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"{packageName}: {version}");
                    Console.ResetColor();
                }
            }

            if (!modified)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"No chages to '{arg}'.");
                Console.ResetColor();
            }
            else if (execute)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"Saving '{arg}'...");
                xmlDoc.Save(arg);
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"Chages to '{arg}' not saved. Run again with --exec to save chages.");
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        return 0;
    }
}
