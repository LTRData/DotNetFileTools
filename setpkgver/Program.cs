using LTRLib.Extensions;
using LTRLib.LTRGeneric;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        var upgradeOnly = false;
        var saveChanges = false;
        var verbose = false;
        var recursive = false;
        Regex[] includeFilters = Array.Empty<Regex>();
        Regex[] excludeFilters = Array.Empty<Regex>();

        foreach (var cmd in cmds)
        {
            switch (cmd.Key)
            {
                // Only change * versions to currently latest version explicitly
                case "explicit":
                case "e":
                    Debug.Assert(cmd.Value.Length == 0, "--explicit option cannot have values");
                    Debug.Assert(!upgradeOnly, "Cannot set both --upgrade and --explicit");
                    setExplicit = true;
                    break;

                    // Only upgrade packages with an explicit version number set
                case "upgrade":
                case "u":
                    Debug.Assert(cmd.Value.Length == 0, "--upgrade option cannot have values");
                    Debug.Assert(!setExplicit, "Cannot set both --upgrade and --explicit");
                    upgradeOnly = true;
                    break;

                    // Save changes
                case "save":
                case "s":
                    Debug.Assert(cmd.Value.Length == 0, "--save option cannot have values");
                    saveChanges = true;
                    break;

                // Regex package name filter
                case "include":
                case "i":
                    Debug.Assert(cmd.Value.Length >= 1, "--include= option requires regex values");
                    includeFilters = Array.ConvertAll(cmd.Value, val => new Regex(val, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                    break;

                case "exclude":
                case "x":
                    Debug.Assert(cmd.Value.Length >= 1, "--exclude= option requires regex values");
                    excludeFilters = Array.ConvertAll(cmd.Value, val => new Regex(val, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                    break;

                // List all found package names
                case "v":
                case "verbose":
                    Debug.Assert(cmd.Value.Length == 0, "--verbose option cannot have values");
                    verbose = true;
                    break;

                case "r":
                case "recurse":
                    Debug.Assert(cmd.Value.Length == 0, "-r option cannot have values");
                    recursive = true;
                    break;

                case "":
                    break;

                default:
                    Console.WriteLine(
@"Utility to upgrade NuGet version references in .NET project files
Copyright 2023 - Olof Lagerkvist, LTR Data - https://ltr-data.se

Usage:
setpkgver [-options] files ...

--verbose           Shows each package reference found in project files with
-v                  selected filters.

--exclude=pattern   Exclude packages that match a regex pattern.
-x=pattern

--include=pattern   Include only packages that match a regex pattern.
-i=pattern

--save              Save changes to project files. Otherwise just dry-run.
-s

--upgrade           Upgrade versions for packages where there are newer
-u                  versions available.

--explicit          Change version references with asterisks to explicit
                    version number available matching the existing pattern.");

                    return -1;
            }
        }

        if (!cmds.TryGetValue("", out _))
        {
            throw new InvalidOperationException("Missing file names");
        }

        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>().ConfigureAwait(false);

        var packageVersions = new ConcurrentDictionary<string, Task<IReadOnlyList<NuGetVersion>>>(StringComparer.Ordinal);

        var files = cmds[""].AsEnumerable();

        if (recursive)
        {
            files = files.SelectMany(file =>
            {
                var path = Path.GetDirectoryName(file);
                
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = ".";
                }

                return Directory.EnumerateFiles(path, Path.GetFileName(file), SearchOption.AllDirectories);
            });
        }

        var result = 0;

        foreach (var arg in files)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Loading '{arg}'...");
                Console.ResetColor();

                var xmlDoc = XElement.Load(arg);

                var packageReferences = xmlDoc.Descendants("ItemGroup")
                    .Elements("PackageReference")
                    .Where(e => e.Attribute("Include") is not null);

                if (includeFilters.Length > 0)
                {
                    packageReferences = packageReferences.
                        Where(e =>
                        {
                            var name = e.Attribute("Include")!.Value;
                            return includeFilters.Any(filter => filter.IsMatch(name));
                        });
                }

                foreach (var filter in excludeFilters)
                {
                    packageReferences = packageReferences.
                        Where(e => !filter.IsMatch(e.Attribute("Include")!.Value));
                }

                if (setExplicit)
                {
                    packageReferences = packageReferences.
                        Where(e => e.Attribute("Version")?.Value?.EndsWith("*", StringComparison.Ordinal) ?? false);
                }

                if (upgradeOnly)
                {
                    packageReferences = packageReferences.
                        Where(e => !e.Attribute("Version")?.Value?.Contains('*') ?? true);
                }

                var modified = false;

                foreach (var node in packageReferences)
                {
                    var packageName = node.Attribute("Include")!.Value;

                    var version = node.Attribute("Version")?.Value;

                    var versions = await packageVersions.GetOrAdd(packageName,
                        async packageName =>
                        {
                            if (verbose)
                            {
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.WriteLine($"Checking latest version for {packageName}...");
                                Console.ResetColor();
                            }

                            try
                            {
                                var versions = await resource.GetAllVersionsAsync(packageName, cache, NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
                                return versions.ToList();
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.Error.WriteLine($"Error checking version for package '{packageName}': {ex.JoinMessages()}");
                                Console.ResetColor();
                                return Array.Empty<NuGetVersion>();
                            }
                        }).ConfigureAwait(false);

                    NuGetVersion? latestVersion;
                    
                    if (version is not null
                        && setExplicit
                        && version.EndsWith("*", StringComparison.Ordinal))
                    {
                        var versionPrefix = version.TrimEnd('*');

                        latestVersion = versions
                            .Where(v => v.OriginalVersion?.StartsWith(versionPrefix, StringComparison.Ordinal) ?? false)
                            .OrderByDescending(v => v.IsPrerelease)
                            .ThenBy(v => v)
                            .LastOrDefault();
                    }
                    else
                    {
                        latestVersion = versions
                            .OrderByDescending(v => v.IsPrerelease)
                            .ThenBy(v => v)
                            .LastOrDefault();
                    }

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
                    else if (verbose)
                    {
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine($"{packageName}: {version}");
                        Console.ResetColor();
                    }
                }

                if (!modified)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"No changes to '{arg}'.");
                    Console.ResetColor();
                }
                else if (saveChanges)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"Saving '{arg}'...");
                    xmlDoc.Save(arg);
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"Changes not saved. Run again with --save to save chages.");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(ex.Message);
                Console.ResetColor();
                result = ex.HResult;
            }

            Console.ResetColor();
            Console.WriteLine();
        }

        return result;
    }
}
