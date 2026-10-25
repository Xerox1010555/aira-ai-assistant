using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using AiraAssistant.Infrastructure.Logging;
using AiraAssistant.Models;
using Microsoft.Data.Sqlite;

namespace AiraAssistant.Services.History;

/// <summary>One stored conversation session.</summary>
public sealed record ConversationSession(string Id, string Title, DateTimeOffset Created, DateTimeOffset Updated);

/// <summary>
/// SQLite-backed conversation persistence in %LOCALAPPDATA%\Aira\aira.db.
/// v1 had zero persistence: every app close erased the whole history.
/// All methods are synchronous (desktop-scale, single-writer) and FAIL-SAFE:
/// history problems are logged and never break the chat itself.
/// Uses the custom <see cref="Infrastructure.Logging.ILogger"/>.
/// </summary>
public sealed class HistoryStore
{
    private readonly string _dbFile;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public HistoryStore(ILogger logger)
    {
        _logger = logger;
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aira");
        System.IO.Directory.CreateDirectory(dir);
        _dbFile = System.IO.Path.Combine(dir, "aira.db");
        EnsureCreated();
    }

    private SqliteConnection Open()
    {
        var cs = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = _dbFile,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared
        }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private void EnsureCreated()
    {
        try
        {
            using var conn = Open();
            using (var pragma = new SqliteCommand("PRAGMA journal_mode=WAL;", conn)) pragma.ExecuteNonQuery();
            var ddl = """
                CREATE TABLE IF NOT EXISTS sessions (
                  id TEXT PRIMARY KEY,
                  title TEXT NOT NULL,
                  created_at TEXT NOT NULL,
                  updated_at TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS messages (
                  id TEXT PRIMARY KEY,
                  session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                  role TEXT NOT NULL,
                  content TEXT NOT NULL,
                  tool_call_id TEXT,
                  tool_name TEXT,
                  function_args_json TEXT,
                  created_at TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_messages_session ON messages(session_id, created_at);
                """;
            using var cmd = new SqliteCommand(ddl, conn);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Error("History database initialization failed; running without persistence.", ex);
        }
    }

    // ------------------------------------------------------------------
    // Sessions
    // ------------------------------------------------------------------

    /// <summary>Returns the id of the most recent session, creating one when the table is empty.</summary>
    public string GetOrCreateLatestSession()
    {
        lock (_gate)
        {
            using var conn = Open();
            using (var q = new SqliteCommand("SELECT id FROM sessions ORDER BY updated_at DESC LIMIT 1;", conn))
            {
                var existing = q.ExecuteScalar() as string;
                if (!string.IsNullOrEmpty(existing)) return existing;
            }
            return CreateSessionInternal(conn, "New chat");
        }
    }

    /// <summary>Always creates a fresh session (used by Clear).</summary>
    public string StartNewSession()
    {
        lock (_gate)
        {
            using var conn = Open();
            return CreateSessionInternal(conn, "New chat");
        }
    }

    private static string CreateSessionInternal(SqliteConnection conn, string title)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var ins = new SqliteCommand(
            "INSERT INTO sessions(id, title, created_at, updated_at) VALUES($id, $t, $c, $u);", conn);
        ins.Parameters.AddWithValue("$id", id);
        ins.Parameters.AddWithValue("$t", title);
        ins.Parameters.AddWithValue("$c", now);
        ins.Parameters.AddWithValue("$u", now);
        ins.ExecuteNonQuery();
        return id;
    }

    public void RenameSession(string sessionId, string title)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var cmd = new SqliteCommand("UPDATE sessions SET title = $t WHERE id = $id;", conn);
                cmd.Parameters.AddWithValue("$t", title);
                cmd.Parameters.AddWithValue("$id", sessionId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: rename failed ({ex.Message}).");
            }
        }
    }

    public void TouchSession(string sessionId)
    {
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var cmd = new SqliteCommand(
                    "UPDATE sessions SET updated_at = $u WHERE id = $id;", conn);
                cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$id", sessionId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: touch failed ({ex.Message}).");
            }
        }
    }

    // Auto-title: rename "New chat" to the first user message snippet once.
    public void AutoTitle(string sessionId, string firstUserText)
    {
        if (string.IsNullOrWhiteSpace(firstUserText)) return;
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var q = new SqliteCommand("SELECT title FROM sessions WHERE id = $id;", conn);
                q.Parameters.AddWithValue("$id", sessionId);
                var title = q.ExecuteScalar() as string;
                if (title is null || title == "New chat")
                {
                    var snippet = firstUserText.Length > 64 ? firstUserText[..64] + "…" : firstUserText;
                    RenameSession(sessionId, snippet);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: autotitle failed ({ex.Message}).");
            }
        }
    }

    // ------------------------------------------------------------------
    // Messages
    // ------------------------------------------------------------------

    public void AppendMessage(string sessionId, ChatMessage msg)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var tx = conn.BeginTransaction();

                using (var ins = new SqliteCommand(
                    "INSERT INTO messages(id, session_id, role, content, tool_call_id, tool_name, function_args_json, created_at) " +
                    "VALUES($id, $s, $r, $c, $tc, $tn, $fa, $ts);", conn, tx))
                {
                    ins.Parameters.AddWithValue("$id", msg.Id);
                    ins.Parameters.AddWithValue("$s", sessionId);
                    ins.Parameters.AddWithValue("$r", msg.Role.ToString());
                    ins.Parameters.AddWithValue("$c", msg.Content);
                    ins.Parameters.AddWithValue("$tc", (object?)msg.ToolCallId ?? DBNull.Value);
                    ins.Parameters.AddWithValue("$tn", (object?)msg.ToolName ?? DBNull.Value);
                    ins.Parameters.AddWithValue("$fa", msg.ToolCalls is { Count: > 0 }
                        ? JsonSerializer.Serialize(msg.ToolCalls, JsonOptsDb)
                        : DBNull.Value);
                    ins.Parameters.AddWithValue("$ts", msg.Timestamp.ToString("O"));
                    ins.ExecuteNonQuery();
                }

                using (var upd = new SqliteCommand("UPDATE sessions SET updated_at = $u WHERE id = $id;", conn, tx))
                {
                    upd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
                    upd.Parameters.AddWithValue("$id", sessionId);
                    upd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: append failed ({ex.Message}).");
            }
        }
    }

    public IReadOnlyList<ChatMessage> LoadMessages(string sessionId)
    {
        var list = new List<ChatMessage>();
        if (string.IsNullOrWhiteSpace(sessionId)) return list;
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var cmd = new SqliteCommand(
                    "SELECT id, role, content, tool_call_id, tool_name, function_args_json, created_at " +
                    "FROM messages WHERE session_id = $s ORDER BY created_at ASC;", conn);
                cmd.Parameters.AddWithValue("$s", sessionId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var role = reader.GetString(1);
                    var content = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    var toolCallId = reader.IsDBNull(3) ? null : reader.GetString(3);
                    var toolName = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var argsJson = reader.IsDBNull(5) ? null : reader.GetString(5);
                    var created = reader.IsDBNull(6)
                        ? DateTimeOffset.UtcNow
                        : (DateTimeOffset.TryParse(reader.GetString(6), out var ts) ? ts : DateTimeOffset.UtcNow);

                    var parsedRole = Enum.TryParse<ChatRole>(role, ignoreCase: true, out var r) ? r : ChatRole.User;

                    IReadOnlyList<ToolCallInfo>? calls = null;
                    if (!string.IsNullOrEmpty(argsJson))
                    {
                        try
                        {
                            calls = JsonSerializer.Deserialize<List<ToolCallInfo>>(argsJson, JsonOptsDb);
                        }
                        catch { /* tolerate corrupt rows */ }
                    }

                    list.Add(new ChatMessage
                    {
                        Id = reader.IsDBNull(0) ? Guid.NewGuid().ToString("N") : reader.GetString(0),
                        Role = parsedRole,
                        Content = content,
                        ToolCallId = toolCallId,
                        ToolName = toolName,
                        ToolCalls = calls,
                        Timestamp = created
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: load failed ({ex.Message}).");
            }
        }
        return list;
    }

    /// <summary>Deletes every message of the session, leaving the session row itself in place (used by Clear).</summary>
    public void ClearMessages(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var cmd = new SqliteCommand("DELETE FROM messages WHERE session_id = $s;", conn);
                cmd.Parameters.AddWithValue("$s", sessionId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: clear failed ({ex.Message}).");
            }
        }
    }

    /// <summary>Keeps only the N most recent sessions and deletes the rest (housekeeping on startup).</summary>
    public void TrimSessions(int keep)
    {
        if (keep <= 0) keep = 1;
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var cmd = new SqliteCommand(
                    """
                    DELETE FROM messages WHERE session_id IN (
                      SELECT id FROM sessions WHERE id NOT IN (
                        SELECT id FROM sessions ORDER BY updated_at DESC LIMIT $k));
                    DELETE FROM sessions WHERE id NOT IN (
                      SELECT id FROM sessions ORDER BY updated_at DESC LIMIT $k);
                    """, conn);
                cmd.Parameters.AddWithValue("$k", keep);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: trim failed ({ex.Message}).");
            }
        }
    }

    /// <summary>Most recently active sessions, newest first (Sessions dialog).</summary>
    public IReadOnlyList<ConversationSession> ListSessions(int limit = 30)
    {
        var list = new List<ConversationSession>();
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var cmd = new SqliteCommand(
                    "SELECT id, title, created_at, updated_at FROM sessions ORDER BY updated_at DESC LIMIT $l;", conn);
                cmd.Parameters.AddWithValue("$l", limit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    var title = reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1))
                        ? "(untitled)"
                        : reader.GetString(1);
                    var created = ParseTimestamp(reader.IsDBNull(2) ? null : reader.GetString(2));
                    var updated = ParseTimestamp(reader.IsDBNull(3) ? null : reader.GetString(3));
                    if (!string.IsNullOrEmpty(id))
                    {
                        list.Add(new ConversationSession(id, title, created, updated));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: list failed ({ex.Message}).");
            }
        }
        return list;
    }

    /// <summary>Removes a session and all of its messages (Sessions dialog).</summary>
    public void DeleteSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        lock (_gate)
        {
            try
            {
                using var conn = Open();
                using var tx = conn.BeginTransaction();
                using (var delMsgs = new SqliteCommand("DELETE FROM messages WHERE session_id = $s;", conn, tx))
                {
                    delMsgs.Parameters.AddWithValue("$s", sessionId);
                    delMsgs.ExecuteNonQuery();
                }
                using (var delSession = new SqliteCommand("DELETE FROM sessions WHERE id = $s;", conn, tx))
                {
                    delSession.Parameters.AddWithValue("$s", sessionId);
                    delSession.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                _logger.Warning($"History: delete session failed ({ex.Message}).");
            }
        }
    }

    private static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var ts) ? ts : DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions JsonOptsDb = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
