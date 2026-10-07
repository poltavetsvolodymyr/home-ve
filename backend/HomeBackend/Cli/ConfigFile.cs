using System.Text.Json;
using System.Text.Json.Nodes;
using HomeBackend.Configuration;

namespace HomeBackend.Cli;

public static class ConfigFile
{
    /// <summary>Sets HomeBackend:PasswordHash in a JSON config file, keeping everything else in it. Creates the file if needed.</summary>
    public static void SetPasswordHash(string path, string hash)
    {
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? [] : new JsonObject();
        if (root[HomeBackendOptions.Section] is not JsonObject section)
            root[HomeBackendOptions.Section] = section = [];
        section["PasswordHash"] = hash;

        // Utf8JsonWriter instead of ToJsonString: no serializer options, so nothing reflection-based in the trimmed build
        using var file = File.Create(path);
        using (var w = new Utf8JsonWriter(file, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "+" in base64 readable
        }))
        {
            root.WriteTo(w);
        }
        file.Write("\n"u8);
    }
}
