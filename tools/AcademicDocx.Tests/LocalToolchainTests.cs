using System.Diagnostics;
using AcademicDocx.Core;

namespace AcademicDocx.Tests;

public sealed class LocalToolchainTests
{
    [Fact]
    public void ManagedProcessUsesLocalToolsAndCacheWithoutChangingTheParent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ANK среда " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "dependencies.lock.json"), """
                {"quarto":{"version":"1.10.19"},"svg_converter":{"distribution_release":"v1.0.0"}}
                """);
            var parent = Environment.GetEnvironmentVariable("RENV_PATHS_CACHE");
            var start = new ProcessStartInfo();
            start.Environment["RENV_PATHS_CACHE"] = "another-cache";
            start.Environment["RENV_PATHS_LIBRARY"] = "another-project-library";
            start.Environment["QUARTO_R"] = "system-r";
            start.Environment["LC_ALL"] = "incompatible-locale";
            new LocalToolchain(root).Configure(start);
            Assert.StartsWith(Path.Combine(root, ".tools"), start.Environment["QUARTO_R"]);
            Assert.StartsWith(Path.Combine(root, ".tools"), start.Environment["PATH"]);
            Assert.Equal(Path.Combine(root, ".tools", "renv", "cache"), start.Environment["RENV_PATHS_CACHE"]);
            Assert.False(start.Environment.ContainsKey("RENV_PATHS_LIBRARY"));
            Assert.False(start.Environment.ContainsKey("LC_ALL"));
            Assert.Equal(parent, Environment.GetEnvironmentVariable("RENV_PATHS_CACHE"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
