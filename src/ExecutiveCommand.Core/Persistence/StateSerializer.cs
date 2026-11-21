using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Persistence;

public static class GameInfo
{
    public const string Version = "0.1.0";
}

public static class StateSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(GameState s) => JsonSerializer.Serialize(s, Options);

    public static GameState Deserialize(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new SaveException("Save data is not a JSON object.");
        int version = node["SchemaVersion"]?.GetValue<int>() ?? 0;
        if (version > GameState.CurrentSchemaVersion)
            throw new SaveException($"This save was created by a newer version of the game (schema {version}).");
        if (version < GameState.CurrentSchemaVersion) node = SaveMigrations.Migrate(node, version);
        return node.Deserialize<GameState>(Options) ?? throw new SaveException("Save data could not be read.");
    }

    public static string Checksum(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    /// <summary>Hash of the full state — used by determinism and anti-manipulation tests.</summary>
    public static string StateHash(GameState s) => Checksum(Serialize(s));
}

public sealed class SaveException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Ordered chain of schema migrations. Each step upgrades a raw JSON document by exactly one version,
/// so a save from any past release can be brought forward.
/// </summary>
public static class SaveMigrations
{
    private static readonly SortedDictionary<int, Func<JsonObject, JsonObject>> Steps = new()
    {
        // v0 → v1: pre-release prototype saves had no SchemaVersion and no Difficulty field.
        [0] = j => { j["Difficulty"] ??= "Professional"; j["SchemaVersion"] = 1; return j; },
    };

    public static JsonObject Migrate(JsonObject json, int fromVersion)
    {
        for (int v = fromVersion; v < GameState.CurrentSchemaVersion; v++)
        {
            if (!Steps.TryGetValue(v, out var step)) throw new SaveException($"No migration available from schema {v}.");
            json = step(json);
        }
        return json;
    }
}
