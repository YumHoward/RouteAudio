' Check whether .NET 8 Desktop Runtime (x64) is installed.
' Sets the DOTNET8FOUND MSI property to "1" when a 8.x shared runtime folder exists.

Set fso = CreateObject("Scripting.FileSystemObject")
found = "0"
On Error Resume Next
Set folder = fso.GetFolder("C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App")
If Err.Number = 0 Then
  For Each subFolder In folder.SubFolders
    If Left(subFolder.Name, 2) = "8." Then found = "1"
  Next
End If
On Error GoTo 0
Session.Property("DOTNET8FOUND") = found
