Set fso = CreateObject("Scripting.FileSystemObject")
Set WshShell = CreateObject("WScript.Shell")
baseDir = fso.GetParentFolderName(WScript.ScriptFullName)
WshShell.CurrentDirectory = baseDir
WshShell.Run "cmd.exe /c """"" & baseDir & "\TenantGateway.exe"" >> """ & baseDir & "\gateway.log"" 2>&1""", 0, False
