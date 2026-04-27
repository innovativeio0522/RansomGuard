using System;
using Microsoft.Data.Sqlite;

class Program {
    static void Main() {
        string dbPath = @"C:\ProgramData\RGCoreEssentials\activity_log.db";
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        
        Console.WriteLine("--- Recent Threats ---");
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Timestamp, Path, Status, Severity FROM Threats ORDER BY Timestamp DESC LIMIT 5";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            Console.WriteLine($"{reader[0]} | {reader[1]} | {reader[2]} | {reader[3]}");
        }
    }
}
