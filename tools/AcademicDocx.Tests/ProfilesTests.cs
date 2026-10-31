using AcademicDocx.Core;

namespace AcademicDocx.Tests;

public sealed class ProfilesTests
{
    [Fact]
    public void WorkspaceCatalogResolvesOldNamesAndKeepsExplicitBundles()
    {
        var catalog = AcademicProfiles.Read(Path.Combine(AppContext.BaseDirectory, "profiles.json"));
        Assert.Equal("coursework-susu-21-2008", catalog.Resolve(null));
        Assert.Equal("coursework-susu-21-2008", catalog.Resolve("coursework"));
        Assert.Equal("referat-susu-17-2008", catalog.Resolve("referat"));
        Assert.Equal("coursework-susu-04-2008", catalog.Resolve("coursework-susu-04-2008"));
        Assert.Equal("research-susu-19-2008", catalog.Resolve("research-susu-19-2008"));
        Assert.Throws<ArgumentException>(() => catalog.Resolve("referat-susu-04-2008"));
    }
}
