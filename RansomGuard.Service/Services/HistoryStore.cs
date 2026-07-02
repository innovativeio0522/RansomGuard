using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RansomGuard.Core.Models;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;
using System.Linq;

namespace RansomGuard.Service.Services
{
    public class HistoryStore : IHistoryStore
    {
        private readonly string _connectionString;

        public HistoryStore()
        {
            string dbPath = PathConfiguration.ActivityLogDatabasePath;
            
            // Ensure directory exists
            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Enable connection pooling and set performance options
            _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;Pooling=True;";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // Enable WAL mode for better concurrency
                var walCommand = connection.CreateCommand();
                walCommand.CommandText = "PRAGMA journal_mode=WAL;";
                walCommand.ExecuteNonQuery();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS FileActivities (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp DATETIME NOT NULL,
                        Action TEXT NOT NULL,
                        FilePath TEXT NOT NULL,
                        Entropy REAL NOT NULL,
                        IsSuspicious INTEGER NOT NULL,
                        ProcessName TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_activities_timestamp ON FileActivities(Timestamp);
                    
                    CREATE TABLE IF NOT EXISTS Threats (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp DATETIME NOT NULL,
                        Name TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        Path TEXT NOT NULL,
                        ProcessName TEXT NOT NULL,
                        ProcessId INTEGER NOT NULL,
                        Severity TEXT NOT NULL,
                        Status TEXT NOT NULL DEFAULT 'Active'
                    );
                    CREATE INDEX IF NOT EXISTS idx_threats_timestamp ON Threats(Timestamp);
                    CREATE INDEX IF NOT EXISTS idx_threats_status ON Threats(Status);
                ";
                command.ExecuteNonQuery();
                FileLogger.Log(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Database initialized successfully.");
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Database initialization failed", ex);
            }
        }

        public async Task SaveActivityAsync(FileActivity activity)
        {
            try
            {
                await RetryHelper.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync().ConfigureAwait(false);

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO FileActivities (Timestamp, Action, FilePath, Entropy, IsSuspicious, ProcessName)
                        VALUES ($timestamp, $action, $filePath, $entropy, $isSuspicious, $processName)
                    ";
                    command.Parameters.AddWithValue("$timestamp", activity.Timestamp);
                    command.Parameters.AddWithValue("$action", activity.Action);
                    command.Parameters.AddWithValue("$filePath", activity.FilePath);
                    command.Parameters.AddWithValue("$entropy", activity.Entropy);
                    command.Parameters.AddWithValue("$isSuspicious", activity.IsSuspicious ? 1 : 0);
                    command.Parameters.AddWithValue("$processName", activity.ProcessName);

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }, maxRetries: 3, shouldRetry: ex => ex is SqliteException sex && sex.SqliteErrorCode == 5); // 5 = SQLITE_BUSY
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Error saving activity", ex);
            }
        }

        public async Task<List<FileActivity>> GetHistoryAsync(int limit = 100)
        {
            var results = new List<FileActivity>();
            try
            {
                await RetryHelper.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync().ConfigureAwait(false);

                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM FileActivities ORDER BY Timestamp DESC LIMIT $limit";
                    command.Parameters.AddWithValue("$limit", limit);

                    using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        results.Add(new FileActivity
                        {
                            Timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp")),
                            Action = reader.GetString(reader.GetOrdinal("Action")),
                            FilePath = reader.GetString(reader.GetOrdinal("FilePath")),
                            Entropy = reader.GetDouble(reader.GetOrdinal("Entropy")),
                            IsSuspicious = reader.GetInt32(reader.GetOrdinal("IsSuspicious")) == 1,
                            ProcessName = reader.GetString(reader.GetOrdinal("ProcessName"))
                        });
                    }
                }, maxRetries: 3, shouldRetry: ex => ex is SqliteException sex && sex.SqliteErrorCode == 5);
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Error reading history", ex);
            }
            return results;
        }

        public async Task SaveThreatAsync(Threat threat)
        {
            try
            {
                await RetryHelper.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync().ConfigureAwait(false);

                    // Check for existing recent threat for this path
                    var checkCmd = connection.CreateCommand();
                    checkCmd.CommandText = "SELECT Id, Status FROM Threats WHERE Path = $path AND Name = $name AND Timestamp >= $since LIMIT 1";
                    checkCmd.Parameters.AddWithValue("$path", threat.Path);
                    checkCmd.Parameters.AddWithValue("$name", threat.Name);
                    checkCmd.Parameters.AddWithValue("$since", threat.Timestamp.AddMinutes(-15));
                    
                    using (var reader = await checkCmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            long id = reader.GetInt64(reader.GetOrdinal("Id"));
                            string currentStatus = reader.GetString(reader.GetOrdinal("Status"));
                            
                            // If the new status is more "advanced" than the current one, update it
                            if (threat.ActionTaken != "Detected" && threat.ActionTaken != "Active" && currentStatus != threat.ActionTaken)
                            {
                                var updateCmd = connection.CreateCommand();
                                updateCmd.CommandText = "UPDATE Threats SET Status = $status, Timestamp = $timestamp WHERE Id = $id";
                                updateCmd.Parameters.AddWithValue("$status", threat.ActionTaken);
                                updateCmd.Parameters.AddWithValue("$timestamp", threat.Timestamp);
                                updateCmd.Parameters.AddWithValue("$id", id);
                                await updateCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            }
                            return; // Handled existing entry
                        }
                    }

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO Threats (Timestamp, Name, Description, Path, ProcessName, ProcessId, Severity, Status)
                        VALUES ($timestamp, $name, $description, $path, $processName, $processId, $severity, $status)
                    ";
                    command.Parameters.AddWithValue("$timestamp", threat.Timestamp);
                    command.Parameters.AddWithValue("$name", threat.Name);
                    command.Parameters.AddWithValue("$description", threat.Description);
                    command.Parameters.AddWithValue("$path", threat.Path);
                    command.Parameters.AddWithValue("$processName", threat.ProcessName);
                    command.Parameters.AddWithValue("$processId", threat.ProcessId);
                    command.Parameters.AddWithValue("$severity", threat.Severity.ToString());
                    command.Parameters.AddWithValue("$status", threat.ActionTaken ?? "Detected");

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }, maxRetries: 3, shouldRetry: ex => ex is SqliteException sex && sex.SqliteErrorCode == 5);
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Error saving threat", ex);
            }
        }


        public async Task<List<Threat>> GetActiveThreatsAsync()
        {
            var results = new List<Threat>();
            try
            {
                await RetryHelper.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync().ConfigureAwait(false);

                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM Threats WHERE Status IN ('Active', 'Detected', 'Awaiting Confirmation', 'Mitigating', 'Mitigated', 'Mitigated (Auto)', 'Quarantined', 'Quarantined (Auto)', 'Terminated', 'User Declined') ORDER BY Timestamp DESC LIMIT 100";

                    using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        string severityStr = reader.GetString(reader.GetOrdinal("Severity"));
                        ThreatSeverity severity = ThreatSeverity.Medium;
                        if (Enum.TryParse<ThreatSeverity>(severityStr, out var parsedSeverity))
                        {
                            severity = parsedSeverity;
                        }

                        string dbStatus = reader.GetString(reader.GetOrdinal("Status"));
                        string actionTaken = dbStatus; 

                        results.Add(new Threat
                        {
                            Timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Description = reader.GetString(reader.GetOrdinal("Description")),
                            Path = reader.GetString(reader.GetOrdinal("Path")),
                            ProcessName = reader.GetString(reader.GetOrdinal("ProcessName")),
                            ProcessId = reader.GetInt32(reader.GetOrdinal("ProcessId")),
                            Severity = severity,
                            ActionTaken = actionTaken
                        });
                    }
                }, maxRetries: 3, shouldRetry: ex => ex is SqliteException sex && sex.SqliteErrorCode == 5);
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Error reading active threats", ex);
            }
            return results;
        }

        public async Task UpdateThreatStatusAsync(string path, string status)
        {
            try
            {
                await RetryHelper.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync().ConfigureAwait(false);

                    var command = connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE Threats 
                        SET Status = $status 
                        WHERE Path = $path AND (Status = 'Active' OR Status = 'Awaiting Confirmation')
                    ";
                    command.Parameters.AddWithValue("$status", status);
                    command.Parameters.AddWithValue("$path", path);

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }, maxRetries: 3, shouldRetry: ex => ex is SqliteException sex && sex.SqliteErrorCode == 5);
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Error updating threat status", ex);
            }
        }
        public async Task ClearActivitiesAsync()
        {
            try
            {
                await RetryHelper.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync().ConfigureAwait(false);

                    var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM FileActivities";
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    
                    // Vaccuum to reclaim space
                    var vacCmd = connection.CreateCommand();
                    vacCmd.CommandText = "VACUUM";
                    await vacCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }, maxRetries: 3, shouldRetry: ex => ex is SqliteException sex && sex.SqliteErrorCode == 5);
                
                FileLogger.Log(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Activity history cleared successfully.");
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.HistoryStoreLogFile, "[HistoryStore] Failed to clear activities", ex);
            }
        }
    }
}
