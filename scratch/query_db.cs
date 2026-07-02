using System;
using Microsoft.Data.Sqlite;

class Program {
    static void Main() {
        string dbPath = @"C:\ProgramData\RGCoreEssentials\activity_log.db";
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        
        Console.WriteLine("--- Latest Threat ---");
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Timestamp, Name, Description, ProcessName, Status FROM Threats ORDER BY Timestamp DESC LIMIT 1";
        using (var reader = cmd.ExecuteReader()) {
            if (reader.Read()) {
                Console.WriteLine($"Timestamp: {reader[0]}");
                Console.WriteLine($"Name: {reader[1]}");
                Console.WriteLine($"Description: {reader[2]}");
                Console.WriteLine($"Process: {reader[3]}");
                Console.WriteLine($"Status: {reader[4]}");
            }
        }

        Console.WriteLine("\n--- Recent File Activities ---");
        var cmd2 = connection.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM FileActivities";
        Console.WriteLine($"Total FileActivities in DB: {cmd2.ExecuteScalar()}");
        
        cmd2.CommandText = "SELECT Timestamp, Action, FilePath, Entropy, IsSuspicious, ProcessName FROM FileActivities ORDER BY Timestamp DESC LIMIT 10";
        using (var reader2 = cmd2.ExecuteReader()) {
            while (reader2.Read()) {
                Console.WriteLine($"{reader2[0]} | {reader2[1]} | {reader2[2]} | Entropy: {reader2[3]} | Suspicious: {reader2[4]} | Process: {reader2[5]}");
            }
        }
    }
}
