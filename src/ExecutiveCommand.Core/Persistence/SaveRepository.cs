using System.Globalization;
using ExecutiveCommand.Core.Model;
using Microsoft.Data.Sqlite;

namespace ExecutiveCommand.Core.Persistence;

public sealed record SaveSlotInfo(string Slot, DateTime SavedUtc, DateOnly GameDate, string Summary, string GameVersion);

public sealed record LoadResult(GameState State, bool RecoveredFromBackup, string? Warning);

/// <summary>
/// SQLite save store. Each slot holds a full state snapshot with a SHA-256 checksum. Writes are
/// transactional, and the previous snapshot of every slot is kept as a backup generation so a
/// corrupted or truncated save can be recovered.
/// </summary>
public sealed class SaveRepository
{
    private const int RepositorySchema = 1;
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public SaveRepository(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        Initialize();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return c;
    }

    private void Initialize()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS saves (
                slot TEXT NOT NULL,
                generation INTEGER NOT NULL,          -- 0 = current, 1 = previous (backup)
                saved_utc TEXT NOT NULL,
                game_date TEXT NOT NULL,
                schema_version INTEGER NOT NULL,
                game_version TEXT NOT NULL,
                checksum TEXT NOT NULL,
                summary TEXT NOT NULL,
                state_json TEXT NOT NULL,
                PRIMARY KEY (slot, generation)
            );
            CREATE TABLE IF NOT EXISTS decision_log (
                slot TEXT NOT NULL, id TEXT NOT NULL, game_date TEXT NOT NULL, action TEXT NOT NULL,
                summary TEXT NOT NULL, decision_quality REAL NOT NULL, outcome_score REAL,
                PRIMARY KEY (slot, id)
            );
            INSERT OR IGNORE INTO meta(key, value) VALUES ('repository_schema', '1');
            """;
        cmd.ExecuteNonQuery();
        using var check = c.CreateCommand();
        check.CommandText = "SELECT value FROM meta WHERE key='repository_schema'";
        var v = int.Parse((string)check.ExecuteScalar()!, CultureInfo.InvariantCulture);
        if (v > RepositorySchema) throw new SaveException("The save database was created by a newer version of the game.");
    }

    public void Save(string slot, GameState state)
    {
        ValidateSlot(slot);
        string json = StateSerializer.Serialize(state);
        string checksum = StateSerializer.Checksum(json);
        string summary = $"{state.Player.Title} · {state.Company.Name} · {state.CurrentDate:MMM d, yyyy}";

        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, "DELETE FROM saves WHERE slot=$s AND generation=1", ("$s", slot));
        Exec(c, tx, "UPDATE saves SET generation=1 WHERE slot=$s AND generation=0", ("$s", slot));
        Exec(c, tx, """
            INSERT INTO saves(slot, generation, saved_utc, game_date, schema_version, game_version, checksum, summary, state_json)
            VALUES ($s, 0, $t, $d, $v, $gv, $c, $sum, $j)
            """,
            ("$s", slot), ("$t", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)), ("$d", state.CurrentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$v", state.SchemaVersion), ("$gv", GameInfo.Version), ("$c", checksum), ("$sum", summary), ("$j", json));
        Exec(c, tx, "DELETE FROM decision_log WHERE slot=$s", ("$s", slot));
        foreach (var d in state.Decisions)
            Exec(c, tx, "INSERT INTO decision_log(slot, id, game_date, action, summary, decision_quality, outcome_score) VALUES ($s,$i,$d,$a,$sum,$q,$o)",
                ("$s", slot), ("$i", d.Id), ("$d", d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$a", d.Action), ("$sum", d.Summary),
                ("$q", d.Evaluation.Overall), ("$o", (object?)d.OutcomeScore ?? DBNull.Value));
        tx.Commit();
    }

    public LoadResult Load(string slot)
    {
        ValidateSlot(slot);
        using var c = Open();
        using (var ic = c.CreateCommand())
        {
            ic.CommandText = "PRAGMA quick_check";
            var r = ic.ExecuteScalar() as string;
            if (r != "ok") throw new SaveException($"The save database failed its integrity check ({r}).");
        }
        string? firstError = null;
        foreach (int gen in new[] { 0, 1 })
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT state_json, checksum FROM saves WHERE slot=$s AND generation=$g";
            cmd.Parameters.AddWithValue("$s", slot);
            cmd.Parameters.AddWithValue("$g", gen);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) { firstError ??= $"No save found in slot '{slot}'."; continue; }
            string json = reader.GetString(0), checksum = reader.GetString(1);
            if (StateSerializer.Checksum(json) != checksum) { firstError ??= "The save data failed its checksum (it may be corrupted or edited)."; continue; }
            try
            {
                var state = StateSerializer.Deserialize(json);
                return new LoadResult(state, gen == 1, gen == 1 ? $"{firstError} Recovered the previous save instead." : null);
            }
            catch (Exception ex) when (ex is SaveException or System.Text.Json.JsonException)
            {
                firstError ??= $"The save data could not be read: {ex.Message}";
            }
        }
        throw new SaveException(firstError ?? "Save could not be loaded.");
    }

    public IReadOnlyList<SaveSlotInfo> ListSlots()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT slot, saved_utc, game_date, summary, game_version FROM saves WHERE generation=0 ORDER BY saved_utc DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<SaveSlotInfo>();
        while (r.Read())
            list.Add(new SaveSlotInfo(r.GetString(0), DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateOnly.ParseExact(r.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture), r.GetString(3), r.GetString(4)));
        return list;
    }

    public bool Exists(string slot) => ListSlots().Any(s => s.Slot == slot);

    public void Delete(string slot)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, "DELETE FROM saves WHERE slot=$s", ("$s", slot));
        Exec(c, tx, "DELETE FROM decision_log WHERE slot=$s", ("$s", slot));
        tx.Commit();
    }

    /// <summary>Test/diagnostic hook: overwrite the current generation's JSON without updating its checksum.</summary>
    internal void CorruptForTesting(string slot, string json)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, "UPDATE saves SET state_json=$j WHERE slot=$s AND generation=0", ("$s", slot), ("$j", json));
        tx.Commit();
    }

    private static void ValidateSlot(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot) || slot.Length > 40 || !slot.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
            throw new ArgumentException("Slot names may contain letters, digits, '-' and '_' (max 40).", nameof(slot));
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }
}
