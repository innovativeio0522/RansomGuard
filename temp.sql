-- Insert test threats into RansomGuard database
INSERT INTO Threats (Timestamp, Name, Description, Path, ProcessName, ProcessId, Severity, Status)
VALUES 
(datetime('now', '-30 minutes'), 'Suspicious Extension Detected', 'File renamed with ransomware-associated extension .locked', 'C:\\Users\\slayer\\Documents\\important_document.docx.locked', 'Sentinel Heuristics', 0, 'High', 'Active'),
(datetime('now', '-25 minutes'), 'High Entropy Data Detected', 'File exhibits abnormally high entropy suggesting encryption', 'C:\\Users\\slayer\\Pictures\\family_photo.jpg.encrypted', 'Sentinel Heuristics', 0, 'Critical', 'Active'),
(datetime('now', '-20 minutes'), 'Suspicious Extension Detected', 'File renamed with ransomware-associated extension .crypty', 'C:\\Users\\slayer\\Desktop\\project_files.zip.crypty', 'Sentinel Heuristics', 0, 'High', 'Active'),
(datetime('now', '-15 minutes'), 'High Entropy Data Detected', 'File exhibits abnormally high entropy suggesting encryption', 'C:\\Users\\slayer\\Documents\\financial_report.xlsx', 'Sentinel Heuristics', 0, 'Medium', 'Active'),
(datetime('now', '-10 minutes'), 'Suspicious Extension Detected', 'File renamed with ransomware-associated extension .wannacry', 'C:\\Users\\slayer\\Videos\\vacation_2025.mp4.wannacry', 'Sentinel Heuristics', 0, 'Critical', 'Active');

SELECT 'Inserted ' || COUNT(*) || ' threats' FROM Threats WHERE Status='Active';
