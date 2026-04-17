# Script to insert test threat entries into RansomGuard database
$dbPath = "$env:ProgramData\RansomGuard\activity_log.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "Database not found at: $dbPath"
    exit 1
}

# Load SQLite assembly
Add-Type -Path "RansomGuard.Core\bin\Debug\net8.0\Microsoft.Data.Sqlite.dll"

$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$dbPath")
$conn.Open()

Write-Host "Inserting test threats into database..."

# Test threats based on common ransomware patterns
$threats = @(
    @{
        Name = "Suspicious Extension Detected"
        Description = "File renamed with ransomware-associated extension .locked"
        Path = "C:\Users\$env:USERNAME\Documents\important_document.docx.locked"
        ProcessName = "Sentinel Heuristics"
        ProcessId = 0
        Severity = "High"
        Timestamp = (Get-Date).AddMinutes(-30).ToString("yyyy-MM-dd HH:mm:ss")
    },
    @{
        Name = "High Entropy Data Detected"
        Description = "File exhibits abnormally high entropy suggesting encryption"
        Path = "C:\Users\$env:USERNAME\Pictures\family_photo.jpg.encrypted"
        ProcessName = "Sentinel Heuristics"
        ProcessId = 0
        Severity = "Critical"
        Timestamp = (Get-Date).AddMinutes(-25).ToString("yyyy-MM-dd HH:mm:ss")
    },
    @{
        Name = "Suspicious Extension Detected"
        Description = "File renamed with ransomware-associated extension .crypty"
        Path = "C:\Users\$env:USERNAME\Desktop\project_files.zip.crypty"
        ProcessName = "Sentinel Heuristics"
        ProcessId = 0
        Severity = "High"
        Timestamp = (Get-Date).AddMinutes(-20).ToString("yyyy-MM-dd HH:mm:ss")
    },
    @{
        Name = "High Entropy Data Detected"
        Description = "File exhibits abnormally high entropy suggesting encryption"
        Path = "C:\Users\$env:USERNAME\Documents\financial_report.xlsx"
        ProcessName = "Sentinel Heuristics"
        ProcessId = 0
        Severity = "Medium"
        Timestamp = (Get-Date).AddMinutes(-15).ToString("yyyy-MM-dd HH:mm:ss")
    },
    @{
        Name = "Suspicious Extension Detected"
        Description = "File renamed with ransomware-associated extension .wannacry"
        Path = "C:\Users\$env:USERNAME\Videos\vacation_2025.mp4.wannacry"
        ProcessName = "Sentinel Heuristics"
        ProcessId = 0
        Severity = "Critical"
        Timestamp = (Get-Date).AddMinutes(-10).ToString("yyyy-MM-dd HH:mm:ss")
    }
)

foreach ($threat in $threats) {
    $cmd = $conn.CreateCommand()
    $insertSql = "INSERT INTO Threats (Timestamp, Name, Description, Path, ProcessName, ProcessId, Severity, Status) VALUES (@timestamp, @name, @description, @path, @processName, @processId, @severity, 'Active')"
    $cmd.CommandText = $insertSql
    
    $cmd.Parameters.AddWithValue("@timestamp", $threat.Timestamp) | Out-Null
    $cmd.Parameters.AddWithValue("@name", $threat.Name) | Out-Null
    $cmd.Parameters.AddWithValue("@description", $threat.Description) | Out-Null
    $cmd.Parameters.AddWithValue("@path", $threat.Path) | Out-Null
    $cmd.Parameters.AddWithValue("@processName", $threat.ProcessName) | Out-Null
    $cmd.Parameters.AddWithValue("@processId", $threat.ProcessId) | Out-Null
    $cmd.Parameters.AddWithValue("@severity", $threat.Severity) | Out-Null
    
    $cmd.ExecuteNonQuery() | Out-Null
    Write-Host "Inserted threat: $($threat.Name) - $($threat.Path)"
}

# Verify insertion
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT COUNT(*) FROM Threats WHERE Status='Active'"
$count = $cmd.ExecuteScalar()

$conn.Close()

Write-Host ""
Write-Host "Success! Inserted $count active threats into database."
Write-Host "Restart the UI to see the threats in the Active Alerts section."
