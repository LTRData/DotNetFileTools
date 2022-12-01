using LTRLib.Extensions;
using LTRLib.Geodesy.Positions;
using LTRLib.Imaging;
using LTRLib.LTRGeneric;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace PictureOnMap;

public static class Program
{
    [STAThread]
    public static int Main(params string[] args)
    {
        if (args is null || args.Length == 0)
        {
            args = new[] { "/install" };
        }

        var result = 0;

        var baseurl = "bingmaps:?cp={lat}~{lon}&lvl=16&collection=point.{lat}_{lon}_{name}";

        foreach (var arg in StringSupport.ParseCommandLine(args, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (arg.Key.Equals("install", StringComparison.OrdinalIgnoreCase) && arg.Value.Length == 0)
                {
                    Install();
                    continue;
                }
                else if (arg.Key.Equals("uninstall", StringComparison.OrdinalIgnoreCase) && arg.Value.Length == 0)
                {
                    Uninstall();
                    continue;
                }
                else if (arg.Key.StartsWith("url", StringComparison.OrdinalIgnoreCase) && arg.Value.Length == 1)
                {
                    baseurl = arg.Value[0];
                    continue;
                }
                else if (arg.Key.Length == 0)
                {
                    foreach (var path in arg.Value)
                    {
                        if (!Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out var picUri))
                        {
                            picUri = new Uri(Path.GetFullPath(path));
                        }

                        var pos = GeoLocators.GetImageGeoLocation(picUri);

                        var lat = pos.LatitudeToString(LatLonPosition.GeoFormat.Degrees);
                        var lon = pos.LongitudeToString(LatLonPosition.GeoFormat.Degrees);
                        var name = Path.GetFileName(path).Replace("_", "-");

                        var uri = baseurl.Replace("{lat}", lat)
                                         .Replace("{lon}", lon)
                                         .Replace("{name}", name);

                        var processStartInfo = new ProcessStartInfo(uri)
                        {
                            UseShellExecute = true
                        };

                        Process.Start(processStartInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                result = ex.HResult;
                Trace.WriteLine(ex.ToString());
                MessageBox.Show(ex.JoinMessages(), "Picture on map", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        return result;
    }

    public static void Install()
    {
        if (!RelaunchElevated("/install"))
        {
            return;
        }

        var asmpath = Assembly.GetExecutingAssembly().Location;

        foreach (var basekey in new[] {
            @"HKEY_CLASSES_ROOT\SystemFileAssociations\.jpg\Shell",
            @"HKEY_CLASSES_ROOT\SystemFileAssociations\.jpe\Shell",
            @"HKEY_CLASSES_ROOT\SystemFileAssociations\.jpeg\Shell",
            @"HKEY_CLASSES_ROOT\jpegfile\shell"
        })
        {
            Registry.SetValue($@"{basekey}\showonmap", null, "Show photo location in Maps app");
            Registry.SetValue($@"{basekey}\showonmap\command", null, $@"""{asmpath}"" /url=""bingmaps:?cp={{lat}}~{{lon}}&lvl=16&collection=point.{{lat}}_{{lon}}_{{name}}"" ""%L""");
            Registry.SetValue($@"{basekey}\showongooglemaps", null, "Show photo location in Google Maps");
            Registry.SetValue($@"{basekey}\showongooglemaps\command", null, $@"""{asmpath}"" /url=""https://www.google.com/maps/place/{{lat}},{{lon}}/@{{lat}},{{lon}},16z/"" ""%L""");
        }

        using (var uninstall = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PictureOnMap"))
        {
            uninstall.SetValue("DisplayName", "Show photo location on map");
            uninstall.SetValue("Publisher", "LTR Data");
            uninstall.SetValue("HelpLink", "http://ltr-data.se/opencode.html");
            uninstall.SetValue("DisplayVersion", "*");
            uninstall.SetValue("UninstallString", $@"""{asmpath}"" /uninstall");
            uninstall.SetValue("DisplayIcon", asmpath);
            uninstall.SetValue("EstimatedSize", 320);
            uninstall.SetValue("NoRepair", 1);
            uninstall.SetValue("NoModify", 1);
            uninstall.SetValue("Size", "");
        }

        MessageBox.Show("Context menu options installed.", "Picture on map", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static void Uninstall()
    {
        if (!RelaunchElevated("/uninstall"))
        {
            return;
        }

        Registry.ClassesRoot.DeleteSubKeyTree(@"SystemFileAssociations\.jpg\Shell\showonmap", throwOnMissingSubKey: false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"SystemFileAssociations\.jpg\Shell\showongooglemaps", throwOnMissingSubKey: false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"SystemFileAssociations\.jpe\Shell\showonmap", throwOnMissingSubKey: false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"SystemFileAssociations\.jpe\Shell\showongooglemaps", throwOnMissingSubKey: false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"SystemFileAssociations\.jpeg\Shell\showonmap", throwOnMissingSubKey: false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"SystemFileAssociations\.jpeg\Shell\showongooglemaps", throwOnMissingSubKey: false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"jpegfile\shell\showonmap", throwOnMissingSubKey: false);
        Registry.ClassesRoot.DeleteSubKeyTree(@"jpegfile\shell\showongooglemaps", throwOnMissingSubKey: false);
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PictureOnMap", throwOnMissingSubKey: false);

        MessageBox.Show("Context menu options uninstalled.", "Picture on map", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static bool RelaunchElevated(string arguments)
    {
        using (var id = WindowsIdentity.GetCurrent())
        {
            if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator))
            {
                return true;
            }
        }

        var exepath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Unknown path to application file");

        var psinfo = new ProcessStartInfo
        {
            ErrorDialog = true,
            FileName = exepath,
            Arguments = arguments,
            Verb = "runas",
            UseShellExecute = true
        };

        Process.Start(psinfo);

        return false;
    }
}
