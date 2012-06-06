 '  Copyright © Microsoft Open Technologies, Inc.
 '  All Rights Reserved
 '  Apache 2.0 License
 '
 ' Licensed under the Apache License, Version 2.0 (the "License");
 ' you may not use this file except in compliance with the License.
 ' You may obtain a copy of the License at
 '
 '   http://www.apache.org/licenses/LICENSE-2.0
 '
 ' Unless required by applicable law or agreed to in writing, software
 ' distributed under the License is distributed on an "AS IS" BASIS,
 ' WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 ' See the License for the specific language governing permissions and
 ' limitations under the License.
 '
 ' See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.
 '/

Function Main()
    Dim properties, powerShellFolder, powerShellPathWow64, powerShellPath32
    properties = Split(Session.Property("CustomActionData"), ";", -1, 1)

    powerShellFolder = properties(0)
    Set wshShell = CreateObject("WScript.Shell")
    powerShellPathWow64 = wshShell.ExpandEnvironmentStrings("%SystemRoot%") & "\syswow64\WindowsPowerShell\v1.0\"
    powerShellPath32 = wshShell.ExpandEnvironmentStrings("%SystemRoot%") & "\system32\WindowsPowerShell\v1.0\"

    Set fso = CreateObject("Scripting.FileSystemObject")
    If fso.FolderExists(powerShellPathWow64) Then
        fso.CopyFile powerShellFolder & "powershell.exe.config", powerShellPathWow64 & "powershell.exe.config"
    End If

    If fso.FolderExists(powerShellPath32) Then
        fso.CopyFile powerShellFolder & "powershell.exe.config", powerShellPath32 & "powershell.exe.config"
    End If

   Set fso = Nothing
End Function