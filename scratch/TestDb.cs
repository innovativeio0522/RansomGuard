using System;
using Microsoft.Data.Sqlite;
class Program {
    static void Main() {
        var str = "Data Source=C:\\ProgramData\\RansomGuard\\History\\sentinel_history.db";
        using var conn = new SqliteConnection(str);
        conn.Open();
        using var cmd = new SqliteCommand("SELECT Path, Severity FROM Threats ORDER BY Timestamp DESC LIMIT 20", conn);
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while(reader.Read()) {
            Console.WriteLine(reader.GetString(0));
            count++;
        }
        if(count == 0) Console.WriteLine("ZERO THREATS IN DB");
    }
}
