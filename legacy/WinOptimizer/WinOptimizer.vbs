Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
scriptPath = fso.BuildPath(fso.GetParentFolderName(WScript.ScriptFullName), "WinOptimizer.ps1")
sh.Run "powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File """ & scriptPath & """", 0, False
