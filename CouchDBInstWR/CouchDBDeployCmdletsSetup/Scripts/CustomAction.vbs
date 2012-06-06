' Copyright © Microsoft Open Technologies, Inc.
' All Rights Reserved
' Apache 2.0 License
' 
'    Licensed under the Apache License, Version 2.0 (the "License");
'    you may not use this file except in compliance with the License.
'    You may obtain a copy of the License at
' 
'      http://www.apache.org/licenses/LICENSE-2.0
' 
'    Unless required by applicable law or agreed to in writing, software
'    distributed under the License is distributed on an "AS IS" BASIS,
'    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
'    See the License for the specific language governing permissions and
'    limitations under the License.
' 
' See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.
'/

Function Main()
    Dim properties, windowsAzureSdkVersion, workerRoleFolder
    properties = Split(Session.Property("CustomActionData"), ";", -1, 1)      
    windowsAzureSdkVersion = properties(0)
    workerRoleFolder = properties(1)
           
    ' Get Windows Azure SDK Path
    Dim windowsAzureSDKLibraryPath
    Set wshShell = CreateObject("WScript.Shell")
    windowsAzureSDKLibraryPath = wshShell.ExpandEnvironmentStrings("%SystemDrive%") & "\Program Files\Windows Azure SDK\" & windowsAzureSdkVersion & "\ref\"      
      
    ' Copy Windows Azure SDK dlls
    Set fso = CreateObject("Scripting.FileSystemObject")
    If Not fso.FileExists(workerRoleFolder & "Microsoft.WindowsAzure.Diagnostics.dll") Then
        fso.CopyFile windowsAzureSDKLibraryPath & "Microsoft.WindowsAzure.Diagnostics.dll", workerRoleFolder & "Microsoft.WindowsAzure.Diagnostics.dll"
    End If

    If Not fso.FileExists(workerRoleFolder & "Microsoft.WindowsAzure.StorageClient.dll") Then
        fso.CopyFile windowsAzureSDKLibraryPath & "Microsoft.WindowsAzure.StorageClient.dll", workerRoleFolder & "Microsoft.WindowsAzure.StorageClient.dll"
    End If

    If Not fso.FileExists(workerRoleFolder & "Microsoft.WindowsAzure.CloudDrive.dll") Then
        fso.CopyFile windowsAzureSDKLibraryPath & "Microsoft.WindowsAzure.CloudDrive.dll", workerRoleFolder & "Microsoft.WindowsAzure.CloudDrive.dll"
    End If 
    Set fso = Nothing      
End Function