using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RansomGuard.Core.Models;
using RansomGuard.Core.Helpers;

namespace RansomGuard.Service.Services
{
    public class HistoryStore : IHistoryStore
    {
        private readonly string _connectionString;

        public HistoryStore()
        {
            string dbPath = PathConfiguration.ActivityLogDatabasePath;
            
            // Ensure directory exists
            string dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

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
                Console.WriteLine("[HistoryStore] Database initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HistoryStore] Database initialization failed: {ex.Message}");
            }
        }

        public async Task SaveActivityAsync(FileActivity activity)
        {
            try
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HistoryStore] Error saving activity: {ex.Message}");
            }
        }

        public async Task<List<FileActivity>> GetHistoryAsync(int limit = 100)
        {
            var results = new List<FileActivity>();
            try
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
                        Timestamp = reader.GetDateTime(1),
                        Action = reader.GetString(2),
                        FilePath = reader.GetString(3),
                        Entropy = reader.GetDouble(4),
                        IsSuspicious = reader.GetInt32(5) == 1,
                        ProcessName = reader.GetString(6)
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HistoryStore] Error reading history: {ex.Message}");
            }
            return results;
        }

        public async Task SaveThreatAsync(Threat threat)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync().ConfigureAwait(false);

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
                command.Parameters.AddWithValue("$severity", threat.Severity);
                command.Parameters.AddWithValue("$status", "Active");

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HistoryStore] Error saving threat: {ex.Message}");
            }
        }

        public async Task<List<Threat>> GetActiveThreatsAsync()
        {
            var results = new List<Threat>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync().ConfigureAwait(false);

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Threats WHERE Status = 'Active' ORDER BY Timestamp DESC";

                using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    string severityStr = reader.GetString(7);
                    ThreatSeverity severity = ThreatSeverity.Medium; // Default
                    if (Enum.TryParse<ThreatSeverity>(severityStr, out var parsedSeverity))
                    {
                        severity = parsedSeverity;
                    }
                    
                    results.Add(new Threat
                    {
                        Timestamp = reader.GetDateTime(1),
                        Name = reader.GetString(2),
                        Description = reader.GetString(3),
                        Path = reader.GetString(4),
                        ProcessName = reader.GetString(5),
                        ProcessId = reader.GetInt32(6),
                        Severity = severity
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HistoryStore] Error reading active threats: {ex.Message}");
            }
            return results;
        }

        public async Task UpdateThreatStatusAsync(string path, string status)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync().ConfigureAwait(false);

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE Threats 
                    SET Status = $status 
                    WHERE Path = $path AND Status = 'Active'
                ";
                command.Parameters.AddWithValue("$status", status);
                command.Parameters.AddWithValue("$path", path);

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HistoryStore] Error updating threat status: {ex.Message}");
            }
        }
    }
}
