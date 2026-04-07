using System.Text.Json;
using Npgsql;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;

namespace PipelogiqSDK.Postgres;

/// <summary>
/// PostgreSQL-backed agent memory store.
/// Stores memory entries in table <c>pipelogiq_memory</c>.
///
/// Run <see cref="EnsureSchemaAsync"/> once at startup.
///
/// Schema:
/// <code>
/// CREATE TABLE IF NOT EXISTS pipelogiq_memory (
///     id         TEXT        PRIMARY KEY,
///     session_id TEXT        NOT NULL,
///     category   TEXT        NOT NULL DEFAULT 'fact',
///     content    TEXT        NOT NULL,
///     stored_at  TIMESTAMPTZ NOT NULL DEFAULT now()
/// );
/// CREATE INDEX IF NOT EXISTS idx_pipelogiq_memory_session ON pipelogiq_memory (session_id);
/// </code>
///
/// Recall uses keyword matching. For semantic recall, extend this class with pgvector.
/// </summary>
public sealed class PostgresAgentMemoryStore : IAgentMemoryStore
{
    private readonly string _connectionString;

    /// <param name="connectionString">Npgsql connection string.</param>
    public PostgresAgentMemoryStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates the <c>pipelogiq_memory</c> table if it does not already exist.
    /// Safe to call on every startup (idempotent).
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS pipelogiq_memory (
                id         TEXT        PRIMARY KEY,
                session_id TEXT        NOT NULL,
                category   TEXT        NOT NULL DEFAULT 'fact',
                content    TEXT        NOT NULL,
                stored_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_pipelogiq_memory_session
                ON pipelogiq_memory (session_id);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentMemoryEntry>> RecallAsync(
        string sessionId,
        string query,
        int maxEntries = 5,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, session_id, category, content, stored_at
            FROM pipelogiq_memory
            WHERE session_id = @sid
            ORDER BY stored_at DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("sid", sessionId);
        // Fetch a wider pool and do keyword scoring in memory (same as in-memory store)
        cmd.Parameters.AddWithValue("limit", maxEntries * 10);

        var entries = new List<AgentMemoryEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(new AgentMemoryEntry
            {
                Id = reader.GetString(0),
                SessionId = reader.GetString(1),
                Category = reader.GetString(2),
                Content = reader.GetString(3),
                StoredAt = reader.GetFieldValue<DateTimeOffset>(4),
            });
        }

        if (entries.Count == 0) return Array.Empty<AgentMemoryEntry>();

        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries
            .Select(e =>
            {
                var hits = queryWords.Count(w => e.Content.Contains(w, StringComparison.OrdinalIgnoreCase));
                e.RelevanceScore = hits > 0 ? (float)hits / queryWords.Length : 0f;
                return e;
            })
            .OrderByDescending(e => e.RelevanceScore)
            .ThenByDescending(e => e.StoredAt)
            .Take(maxEntries)
            .ToList();
    }

    /// <inheritdoc />
    public async Task StoreAsync(AgentMemoryEntry entry, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pipelogiq_memory (id, session_id, category, content, stored_at)
            VALUES (@id, @sid, @cat, @content, @stored_at)
            ON CONFLICT (id) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("id", entry.Id);
        cmd.Parameters.AddWithValue("sid", entry.SessionId);
        cmd.Parameters.AddWithValue("cat", entry.Category);
        cmd.Parameters.AddWithValue("content", entry.Content);
        cmd.Parameters.AddWithValue("stored_at", entry.StoredAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pipelogiq_memory WHERE session_id = @sid";
        cmd.Parameters.AddWithValue("sid", sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
