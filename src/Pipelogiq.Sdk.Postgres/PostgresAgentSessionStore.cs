using System.Text.Json;
using Npgsql;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;

namespace PipelogiqSDK.Postgres;

/// <summary>
/// PostgreSQL-backed conversation session store.
/// Stores session history in a table <c>pipelogiq_sessions</c>.
///
/// Run <see cref="EnsureSchemaAsync"/> once at startup to create the table if it doesn't exist.
///
/// Schema:
/// <code>
/// CREATE TABLE IF NOT EXISTS pipelogiq_sessions (
///     session_id TEXT PRIMARY KEY,
///     history    JSONB       NOT NULL,
///     saved_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
///     expires_at TIMESTAMPTZ NOT NULL
/// );
/// CREATE INDEX IF NOT EXISTS idx_pipelogiq_sessions_expires ON pipelogiq_sessions (expires_at);
/// </code>
/// </summary>
public sealed class PostgresAgentSessionStore : IAgentSessionStore
{
    private readonly string _connectionString;
    private readonly TimeSpan _ttl;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <param name="connectionString">Npgsql connection string.</param>
    /// <param name="sessionTtl">How long a session survives without activity. Default: 2 hours.</param>
    public PostgresAgentSessionStore(string connectionString, TimeSpan? sessionTtl = null)
    {
        _connectionString = connectionString;
        _ttl = sessionTtl ?? TimeSpan.FromHours(2);
    }

    /// <summary>
    /// Creates the <c>pipelogiq_sessions</c> table if it does not already exist.
    /// Safe to call on every startup (idempotent).
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS pipelogiq_sessions (
                session_id TEXT        PRIMARY KEY,
                history    JSONB       NOT NULL,
                saved_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
                expires_at TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_pipelogiq_sessions_expires
                ON pipelogiq_sessions (expires_at);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<List<AgentConversationTurn>?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT history FROM pipelogiq_sessions
            WHERE session_id = @sid AND expires_at > now()
            """;
        cmd.Parameters.AddWithValue("sid", sessionId);

        var raw = await cmd.ExecuteScalarAsync(ct);
        if (raw is null or DBNull) return null;

        return JsonSerializer.Deserialize<List<AgentConversationTurn>>(raw.ToString()!, JsonOptions);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string sessionId, List<AgentConversationTurn> history, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(history, JsonOptions);
        var expiresAt = DateTimeOffset.UtcNow.Add(_ttl);

        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pipelogiq_sessions (session_id, history, saved_at, expires_at)
            VALUES (@sid, @history::jsonb, now(), @expires)
            ON CONFLICT (session_id) DO UPDATE
                SET history    = EXCLUDED.history,
                    saved_at   = now(),
                    expires_at = EXCLUDED.expires_at
            """;
        cmd.Parameters.AddWithValue("sid", sessionId);
        cmd.Parameters.AddWithValue("history", json);
        cmd.Parameters.AddWithValue("expires", expiresAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pipelogiq_sessions WHERE session_id = @sid";
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
