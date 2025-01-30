using DiscUtils.Registry;

namespace TestProject.reged;

public class UnitTest1
{
    [Fact]
    public void ParseDataString()
    {
        const string str = @"%ProgramFiles%\\WindowsPowerShell\\Modules;%SystemRoot%\\system32\\WindowsPowerShell\\v1.0\\Modules;%SystemDrive%\\Utils\\net9.0";
        const string expected = @"%ProgramFiles%\WindowsPowerShell\Modules;%SystemRoot%\system32\WindowsPowerShell\v1.0\Modules;%SystemDrive%\Utils\net9.0";

        var parsed = global::reged.Program.ParseDataString(str, RegistryValueType.ExpandString);

        Assert.Equal(expected, parsed);
    }
}
