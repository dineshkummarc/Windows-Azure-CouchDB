<#
   Copyright © Microsoft Open Technologies, Inc.
   All Rights Reserved
   Apache 2.0 License

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

   See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.
#>

if ($args.Count -ne 3) {
    Write-Host "`r`nUSAGE: get-and-install-msi.ps1 <download-url> <pathToCheck> <name>`r`n" -foregroundcolor "green"
    exit 1
}
$installerDownloadUri = $args[0]
$pathToCheck = $args[1]
$name = $args[2]

# ::ProgramFiles:: means choose 'Program Files' or 'Program Files (x86)' based on
# architecture
if ($pathToCheck.Contains("::ProgramFiles::")) {
    $osArchitecture = Get-WmiObject -ComputerName localhost Win32_OperatingSystem  | select OSArchitecture
    if ($osArchitecture.OSArchitecture -eq "64-bit") 
    {
        $pathToCheck = $pathToCheck -replace "::ProgramFiles::", "Program Files (x86)"
    } else {
        $pathToCheck = $pathToCheck -replace "::ProgramFiles::", "Program Files"
    }
}

<#
    Download a file targetted by $downloadUrl
    
    @param string   $downloadUrl Url to the package
    @param string   $name        Name of the package
    
    @return string  Path to downloaded directory
#>
function downloadFile {
    param ($downloadUrl, $name)

    $storageDir = Join-Path $pwd "downloadtemp"
    $webclient = New-Object System.Net.WebClient
    $split = $downloadUrl.split("/")
    $fileName = $split[$split.Length-1]
    $filePath = Join-Path $storageDir $fileName
    
    if (!(Test-Path -LiteralPath $storageDir -PathType Container)) {
        New-Item -type directory -path $storageDir | Out-Null
    } else {
        Write-Host "Cleaning out temporary download directory" -foregroundcolor "yellow"
        Remove-Item (Join-Path $storageDir "*") -Recurse -Force
        Write-Host "Temporary download directory cleaned" -foregroundcolor "yellow"
    }
    
    Write-Host "Downloading '$name'. This could take time..." -foregroundcolor "yellow"
    $webclient.DownloadFile($downloadUrl, $filePath)
    Write-Host "$name downloaded" -foregroundcolor "yellow"
    return $filePath
}

<#
    Check for existance of the resource $path 
    
    @param string $path Path to the resource
    @param string $name Name of the package
    
    @return int 3 if path exists, 1 if path does not exists
#>
function checkForPath {
    param ($path, $name)
    Write-Host "Checking for $name on your machine" -foregroundcolor "yellow"
    if (!(Test-Path -LiteralPath $path)) {
        Write-Host "it looks you don't have $name installed " -foregroundcolor "yellow"
        return 1
    } else {
        Write-Host "$name is already installed on your machine" -foregroundcolor "green"
        return 3;
    }
}

<#
    Install the package targetted by $msiPath
    
    @param string   $msiPath     Path to the package
    @param string   $name        Name of the package
    
    @return boolean              True if installation succeeded, false on failure.
#>
function InstallMSI {
    param ($msiPath, $name)

    $msiArgs = @()
    $msiArgs += "/i"
    $msiArgs += "`"$msiPath`""
    $result = (Start-Process -FilePath msiexec -ArgumentList $msiArgs -Wait -PassThru).ExitCode
    if ($result -eq 0) {
        Write-Host "Done with Installation of $name. Clearing temporary storage directory"  -foregroundcolor "green"
    } else {
        Write-Host "Installation of $name Failed. Clearing temporary storage directory"  -foregroundcolor "red"
    }
    
    $storageDir = Join-Path $pwd "downloadtemp"
    if (Test-Path -LiteralPath $storageDir -PathType Container) {
        Remove-Item -path $storageDir -force -Recurse
    }
    
    return ($result -eq 0);
}

<#
    Delete the temporary directory created for downloads
#>
function cleanDownloadDir {
    $storageDir = Join-Path $pwd "downloadtemp"
    
    if ((Test-Path -LiteralPath $storageDir -PathType Container)) {
        Write-Host "Cleaning out temporary download directory" -foregroundcolor "yellow"
        Sleep -s 5
        Remove-Item (Join-Path $storageDir "*") -Recurse -Force
        Write-Host "Temporary download directory cleaned" -foregroundcolor "yellow"
    }
}

<#
    Delete the temporary directory created for downloads and exit
#>
function cleanDownloadDirAndExit {
    param ($exitCode)
    cleanDownloadDir
    exit $exitCode
}

$result = checkForPath $pathToCheck $name

if ($result -eq 1) {
    # Download and install package
    $msiPath = downloadFile $installerDownloadUri $name
    $result = InstallMSI $msiPath $name
    if (!$result) {
        cleanDownloadDirAndExit 1
    }

    cleanDownloadDir
}