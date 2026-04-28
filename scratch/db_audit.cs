using System;
using Microsoft.Data.Sqlite;

try
{
    string dbPath = @"C:\ProgramData\RGCoreEssentials\activity_log.db";
    string connStr = $"Data Source={dbPath};Mode=ReadOnly;";
    using var conn = new SqliteConnection(connStr);
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Name, Status, COUNT(*) FROM Threats GROUP BY Name, Status";
    using var reader = cmd.ExecuteReader();
    Console.WriteLine("Name | Status | Count");
    Console.WriteLine("---------------------");
    while (reader.Read())
    {
        Console.WriteLine($"{reader.GetString(0)} | {reader.GetString(1)} | {reader.GetInt32(2)}");
    }
}
catch (Exception ex)
{
    Console.WriteLine("Error: " + ex.Message);
}
