using System.Text.Json;

namespace AcademicDocx.Core;

// The same small JSON catalog is consumed by the Quarto Lua filter.
public sealed record AcademicProfile(string Label, string Description = "");
public sealed record AcademicProfileAlias(string Default);
public sealed record AcademicProfiles(string Default, Dictionary<string, AcademicProfile> Profiles,
    Dictionary<string, AcademicProfileAlias> Aliases)
{
    public static AcademicProfiles Read(string path) =>
        JsonSerializer.Deserialize<AcademicProfiles>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Missing academic profiles catalog");

    public string Resolve(string? name)
    {
        var id = name ?? Default;
        if (Aliases.TryGetValue(id, out var alias)) id = alias.Default;
        if (!Profiles.ContainsKey(id)) throw new ArgumentException("Unknown profile: " + name);
        return id;
    }
}
