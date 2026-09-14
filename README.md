# DotNetFileTools

A collection of .NET command-line utilities from LTR Data for inspecting binaries,
working with files and archives, extracting photo metadata, editing offline
registry hives, and backing up virtual disk changes. Each tool is a separate
project. The repository also includes the Windows desktop helper PictureOnMap.

## Tools

| Tool | Purpose | Platform or usage notes |
| --- | --- | --- |
| [checksum](checksum) | Calculate hashes of files, standard input, or UTF-8 string values; supports selectable algorithms and HMAC keys. | Available algorithms depend on the runtime. Defaults to MD5; use `-a:SHA256` to select SHA-256. |
| [hexdump](hexdump) | Display hexadecimal file or stream contents, with optional offset and byte count. | Also has raw-device access paths through Arsenal Image Mounter; these depend on the OS and device permissions. |
| [peinfo](peinfo) | Inspect PE/ELF headers and available dependency, import, and export information. Can process entries in ZIP, 7z, TAR, and CAB archives, and files inside disk/WIM images. | PE API-set resolution uses the Windows process mappings or a supplied/discovered `apisetschema.dll`. Image support comes from DiscUtils and image-provider dependencies. |
| [netcheck](netcheck) | Display .NET assembly identities, framework information, and referenced-assembly trees. | Loads assemblies into the tool's runtime; resolution depends on available assemblies and runtime compatibility. |
| [TestAssembly](TestAssembly) | List .NET types and members and detect failures resolving their metadata; supports quiet operation for scripts. | Loads assemblies through reflection. This is an inspection utility; the repository's unit tests are in TestProject. |
| [ExtractExeNetStrings](ExtractExeNetStrings) | Extract strings from the `#US` user-string heap in .NET metadata and print escaped string literals. | Reads the input file directly. |
| [picmetadump](picmetadump) | Export photo date, GPS coordinates, and camera make/model as semicolon-separated text. | Accepts local file patterns and HTTP(S) image URLs; metadata parsing uses MetadataExtractor. |
| [PictureOnMap](PictureOnMap) | Open a photo's GPS location in a web map and install JPEG context-menu entries for Bing Maps and Google Maps. | Windows Forms application. Running with no arguments starts installation; installing/removing shell integration requests elevation. |
| [reged](reged) | Query, create, update, and remove keys/values in offline Windows registry hive files, including hives inside supported disk images. | Uses DiscUtils hive parsing. Image editing requires writable container/filesystem support; WIM access is read-only. |
| [VhdBlockBackup](VhdBlockBackup) | Compare virtual-disk block checksum lists and copy changed blocks into a new differencing image whose parent is the backup target. | Requires a format with differencing support. See the existing [backup workflow and examples](VhdBlockBackup/examples.txt). |
| [ZipIO](ZipIO) | List, read, add, delete, extract, freshen, and replace ZIP entries, and work with their timestamps. | Subcommand-based interface; some read operations also accept standard input or an archive URL. |
| [NetCompress](NetCompress) | Compress/decompress standard input to standard output using .NET compression streams. | Select a method such as `-m:GZip` or `-m:Deflate`; without a method it copies the stream unchanged. |
| [ReplaceText](ReplaceText) | Apply literal text replacements to matching files, optionally recursively. | Rewrites changed files in place as UTF-8 without a BOM. |
| [splitbystr](splitbystr) | Split a file or standard input at a UTF-8 token string into numbered output files. | Output names receive `.001`, `.002`, etc. |
| [txtcnv](txtcnv) | Convert text between named encodings or numeric code pages. | Reads files or standard input and writes converted bytes to standard output. |
| [setpkgver](setpkgver) | Find/update NuGet package versions in .NET project files, including replacing floating versions with explicit versions. | Queries nuget.org; changes are saved only with `--save`. Supports package-name filters and recursive file searches. |

## Frameworks and platforms

The executable projects declare .NET 9 and .NET 10 targets. Most also target
.NET 8; `peinfo` does not. PictureOnMap uses the corresponding
`net8.0-windows`, `net9.0-windows`, and `net10.0-windows` targets.

Many projects retain .NET Framework targets ranging from 2.0 to 4.8, and some
also declare .NET Standard targets. Check each project file for its exact list.
For command-line use on modern .NET, select a runnable target such as `net10.0`.

Most console tools work with managed file and stream APIs. PictureOnMap requires
Windows, and device access, assembly loading, image providers, and API-set
resolution have the additional constraints noted above. A target framework alone
does not establish that every operation works on every OS.

## Build and run

Use a .NET 10 SDK for the current .NET 10 targets. Build individual projects to
select the tools and frameworks you need. For example, from the repository root:

```sh
git clone https://github.com/LTRData/DotNetFileTools.git
cd DotNetFileTools
dotnet build checksum/checksum.csproj -c Release -p:TargetFrameworks=net10.0
dotnet Release/net10.0/checksum.dll -a:SHA256 sample.bin
```

Setting `TargetFrameworks` to a single target limits both restore and build to
that framework. [Directory.Build.props](Directory.Build.props) places build
outputs in the shared root `Debug/` or `Release/` directory, with framework
subdirectories.

[DotNetFileTools.slnx](DotNetFileTools.slnx) includes all tools and the test project.
Building all its declared targets also requires the relevant .NET Framework
reference assemblies and Windows desktop targeting components. Use an IDE/MSBuild
version that understands the `.slnx` solution format. NuGet dependencies are
restored from the declared package references; many package versions float using `*`, so
the resolved dependencies can change between restores.

The [xUnit test project](TestProject) targets .NET 9 and .NET 10. Its API-set
fixture test refers to files under `X:\workfiles\testimages` that are not included
in this repository, so the complete test suite needs those external fixtures.
The registry parsing test can be selected independently:

```sh
dotnet test TestProject/TestProject.csproj -c Release -p:TargetFrameworks=net10.0 --filter FullyQualifiedName~TestProject.reged
```

## Usage examples

After building each corresponding project for `net10.0`:

```sh
dotnet Release/net10.0/hexdump.dll --offset:0 --count:256 sample.bin
dotnet Release/net10.0/peinfo.dll --imports --exports sample.dll
dotnet Release/net10.0/picmetadump.dll photo.jpg
dotnet Release/net10.0/ZipIO.dll list --long archive.zip
dotnet Release/net10.0/reged.dll --query --hive=SOFTWARE
```

For more options, use the individual tool's help, for example `peinfo --help`,
`reged --help`, or `ZipIO add --help`. There is no shared option syntax across
all tools; each project's `Program.cs` (or `Program.vb`) contains its argument
handling.

For VhdBlockBackup, keep each `.blocklist.bin` synchronized with its corresponding
disk image. `--copy` creates a differencing image; merging it into the target is a
separate operation. The [workflow document](VhdBlockBackup/examples.txt) explains
initial preparation, subsequent backups, and moving the metadata after a merge;
the tool's help also documents `--validate`.
