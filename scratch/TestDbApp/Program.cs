using System;
using Microsoft.Data.Sqlite;
class Program {
    static void Main() {
        var str = "Data Source=C:\\ProgramData\\RansomGuard\\History\\sentinel_history.db";
        using var conn = new SqliteConnection(str);
        conn.Open();
        using var cmd = new SqliteCommand("SELECT COUNT(*) FROM Threats", conn);
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Console.WriteLine("THREAT_COUNT=" + count);
    }
}
