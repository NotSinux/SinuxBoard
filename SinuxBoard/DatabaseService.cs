using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SinuxBoard;

/// <summary>
/// Owns all SQLite access for SinuxBoard. Every public method opens its
/// own short-lived connection, which keeps the service safe to call from
/// multiple threads (native clipboard callbacks, UI thread, background
/// import/export) without hand-rolled locking.
/// </summary>
public sealed class DatabaseService
{
    private const int DefaultRecentCount = 100;
    private const int MaxHistorySize = 10_000;

    private readonly string _connectionString;

    public DatabaseService()
    {
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SinuxBoard");

        Directory.CreateDirectory(appDataDir);

        string dbPath = Path.Combine(appDataDir, "sinuxboard.db");
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// Must be called once, before any other database access, after
    /// SQLitePCL.Batteries.Init() has run.
    /// </summary>
    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ClipboardHistory
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Content TEXT NOT NULL,
                Type TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ClipboardHistory_CreatedAt
                ON ClipboardHistory (CreatedAt);
            """;

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts a new clipboard entry unless its content is identical to
    /// the most recent entry already stored.
    /// Returns true if a new row was written.
    /// </summary>
    public bool InsertIfNotDuplicate(string content, string type = "Text")
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        string? latest = GetLatestContent(connection, transaction);

        if (string.Equals(latest, content, StringComparison.Ordinal))
        {
            transaction.Commit();
            return false;
        }

        InsertEntry(
            connection,
            transaction,
            content,
            type,
            DateTime.UtcNow);

        transaction.Commit();

        EnforceHistoryLimit();

        return true;
    }

    public ClipboardEntry? GetLatest()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, Content, Type, CreatedAt
            FROM ClipboardHistory
            ORDER BY Id DESC
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();

        return reader.Read()
            ? ReadEntry(reader)
            : null;
    }

    public List<ClipboardEntry> GetRecent(int count = DefaultRecentCount)
    {
        var results = new List<ClipboardEntry>();

        if (count <= 0)
        {
            return results;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, Content, Type, CreatedAt
            FROM ClipboardHistory
            ORDER BY Id DESC
            LIMIT $count;
            """;

        command.Parameters.AddWithValue("$count", count);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(ReadEntry(reader));
        }

        return results;
    }

    /// <summary>
    /// Returns the entire history, newest first, for export.
    /// </summary>
    public List<ClipboardEntry> GetAll()
    {
        var results = new List<ClipboardEntry>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, Content, Type, CreatedAt
            FROM ClipboardHistory
            ORDER BY Id DESC;
            """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(ReadEntry(reader));
        }

        return results;
    }

    /// <summary>
    /// Imports entries from an export file into the existing database.
    /// Entries are inserted oldest-first so relative ordering is preserved.
    /// Consecutive duplicate content is skipped.
    /// Returns the number of rows actually inserted.
    /// </summary>
    public int Import(IEnumerable<ClipboardEntry> entries)
    {
        int inserted = 0;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        string? lastContent = GetLatestContent(
            connection,
            transaction);

        var ordered = new List<ClipboardEntry>(entries);

        // Exported history is newest-first.
        // Insert oldest-first to preserve chronological order.
        ordered.Reverse();

        foreach (var entry in ordered)
        {
            if (string.IsNullOrEmpty(entry.Content))
            {
                continue;
            }

            if (string.Equals(
                    lastContent,
                    entry.Content,
                    StringComparison.Ordinal))
            {
                continue;
            }

            DateTime timestamp =
                entry.CreatedAtUtc == default
                    ? DateTime.UtcNow
                    : entry.CreatedAtUtc.ToUniversalTime();

            string type =
                string.IsNullOrWhiteSpace(entry.Type)
                    ? "Text"
                    : entry.Type;

            InsertEntry(
                connection,
                transaction,
                entry.Content,
                type,
                timestamp);

            lastContent = entry.Content;
            inserted++;
        }

        transaction.Commit();

        if (inserted > 0)
        {
            EnforceHistoryLimit();
        }

        return inserted;
    }

    /// <summary>
    /// Keeps the table from growing without bound by removing the
    /// oldest rows once the configured maximum is exceeded.
    /// </summary>
    private void EnforceHistoryLimit()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM ClipboardHistory
            WHERE Id NOT IN
            (
                SELECT Id
                FROM ClipboardHistory
                ORDER BY Id DESC
                LIMIT $max
            );
            """;

        command.Parameters.AddWithValue(
            "$max",
            MaxHistorySize);

        command.ExecuteNonQuery();
    }

    private static string? GetLatestContent(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = """
            SELECT Content
            FROM ClipboardHistory
            ORDER BY Id DESC
            LIMIT 1;
            """;

        return command.ExecuteScalar() as string;
    }

    private static void InsertEntry(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string content,
        string type,
        DateTime createdAtUtc)
    {
        using var command = connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO ClipboardHistory
                (Content, Type, CreatedAt)
            VALUES
                ($content, $type, $createdAt);
            """;

        command.Parameters.AddWithValue(
            "$content",
            content);

        command.Parameters.AddWithValue(
            "$type",
            type);

        command.Parameters.AddWithValue(
            "$createdAt",
            createdAtUtc
                .ToUniversalTime()
                .ToString(
                    "O",
                    CultureInfo.InvariantCulture));

        command.ExecuteNonQuery();
    }

    private static ClipboardEntry ReadEntry(
        SqliteDataReader reader)
    {
        string createdAtText = reader.GetString(3);

        DateTime createdAtUtc = DateTime.Parse(
            createdAtText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        // The database stores UTC timestamps. Normalize here as an
        // additional safeguard for imported/legacy values.
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            createdAtUtc = createdAtUtc.ToUniversalTime();
        }

        return new ClipboardEntry
        {
            Id = reader.GetInt64(0),
            Content = reader.GetString(1),
            Type = reader.GetString(2),
            CreatedAtUtc = createdAtUtc
        };
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(
            _connectionString);

        connection.Open();

        return connection;
    }
}