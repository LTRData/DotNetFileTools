using peinfo;

namespace TestProject.peinfo;

public class ApiSet
{
    [Fact]
    public void TestParseApiSetSchema()
    {
        string[] files = [
            @"X:\workfiles\testimages\Windows_7\apisetschema.dll",
            @"X:\workfiles\testimages\Windows_8.1\apisetschema.dll",
            @"X:\workfiles\testimages\Windows_11\apisetschema.dll"];

        foreach (var file in files)
        {
            var apiSet = ApiSetResolver.GetApiSetTranslations(file);

            Assert.True(apiSet.HasTranslations);
        }
    }

    [Fact]
    public void TestParseApiSetSchema2()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(ApiSetResolver.Default.HasTranslations);
        }
    }
}
