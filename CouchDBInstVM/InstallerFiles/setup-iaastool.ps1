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

$global:ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.ServiceModel.Web, System.Runtime.Serialization

$nodejsDownloadUrl = $null
$nodeIAASToolDownloadUrl = $null
$nodeJSMinVersion = $null
$iaasUsePackageName = $null
$iaasPackageName = $null

$osArchitecture = Get-WmiObject -ComputerName localhost Win32_OperatingSystem  | select OSArchitecture
# Path to nodejs binary
$nodejsPath = $env:SystemDrive + "\Program Files";
if ($osArchitecture.OSArchitecture -eq "64-bit") 
{
    $nodejsPath += " (x86)";
}

$npmpath = $nodejsPath;
$nodejsPath += "\nodejs\node.exe";
$npmpath += "\nodejs\npm.cmd";

# Path to IAAS Tool global command file
$nodeIAASToolScriptPath = $env:APPDATA + "\npm\azure.cmd";
# Path to IAAS Tool lib directory
$nodeIAASToolLibPath = $env:APPDATA + "\npm\node_modules\azure\lib";
# Path to IAAS Tool azure.js file
$nodeIAASToolAzureJS = $env:APPDATA + "\npm\node_modules\azure\bin\azure.js";
# Path to IAAS Tool configuration and certificate files
$nodeIAASToolConfigPath = (Join-Path -Path $env:HOMEDRIVE -ChildPath $env:HOMEPATH) + "\.azure\";

function logStatus {
    param ($message)
    Write-Host "info:   $message" -foregroundcolor "yellow"
}

function logErr {
    param ($message)
    Write-Host "error:  $message" -foregroundcolor "red"
}

function logSuccess {
    param ($message)
    Write-Host "info:   $message" -foregroundcolor "green"
}

function logInput {
    param ($message)
    Write-Host "input:  $message" -foregroundcolor "magenta"
}

#--------------------------------------------------Read setup.xml file for prerequisite locations----------------------------

if (!(Test-Path -LiteralPath (Join-Path $pwd "setup.xml")  -PathType Leaf)) 
{
    logErr "Unable to locate setup.xml file"
    exit 1
}

[xml]$setupfile = Get-Content (Join-Path $pwd "setup.xml")

$nodeJSMinVersion = $setupfile.setup.nodejs.minversion
if (!$nodeJSMinVersion) {
  logErr "Unable to read node.js minimum version from setup.xml [\\setup\nodejs\minversion]"
}

$nodejsDownloadUrl = $setupfile.setup.nodejs.downloadurl
if (!$nodejsDownloadUrl) {
  logErr "Unable to read node.js download url from setup.xml [\\setup\nodejs\downloadurl]"
}

$iaasUsePackageName = $setupfile.setup.iaastool.usepackagename
if ($iaasUsePackageName -eq 'true' -or $iaasUsePackageName -eq 'TRUE') {
    $iaasUsePackageName = $True
} elseif ($iaasUsePackageName -eq 'false' -or $iaasUsePackageName -eq 'FALSE') {
    $iaasUsePackageName = $False
} else {
    logErr "Unable to read or parse the boolean iaasUsePackageName from setup.xml [\\setup\iaastool\iaasUsePackageName]"
}

$iaasPackageName = $setupfile.setup.iaastool.packagename
if ($iaasUsePackageName -and !$iaasPackageName) {
  logErr "Unable to read 'Windows Azure Command Line Tools for Mac and Linux' npm package name from setup.xml [\\setup\iaastool\packagename]"
}

$nodeIAASToolDownloadUrl = $setupfile.setup.iaastool.downloadurl
if (!$iaasUsePackageName -and !$nodeIAASToolDownloadUrl) {
  logErr "Unable to read 'Windows Azure Command Line Tools for Mac and Linux' download url from setup.xml [\\setup\iaastool\downloadurl]"
}

#---------------------------------------------------------------------------------------------------------------------------

<#
    Check for existance of the resource $path and prompt for installation of $name if resource
    does not exists.
    
    @param string $path Path to the resource
    @param string $name Name of the package
    
    @return int 3 if path exists, 2 if user denied installation of package and 1 if user accet 
                  to install the package.
#>
function checkForPathAndPromptIfNot {
    param ($path, $name)
    logStatus "Checking for '$name' on your machine"
    if (!(Test-Path -LiteralPath $path)) {
        $yes = New-Object System.Management.Automation.Host.ChoiceDescription "&Yes", "Install $name"
        $no = New-Object System.Management.Automation.Host.ChoiceDescription "&No", "Do not install $name"
        $options = [System.Management.Automation.Host.ChoiceDescription[]]($yes, $no)
        logInput "Looks like you don't have '$name' installed - shall I install it for you?:"
        $result = $host.ui.PromptForChoice($null, 
            $null, 
            $options, 
        0)
        switch ($result) {
            0 {return 1}
            1 {return 2}
        }
    } else {
        logStatus "'$name' is already installed on your machine"
        return 3;
    }
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

<#
    Download a file targetted by $downloadUrl
    
    @param string   $downloadUrl Url to the package
    @param string   $name        Name of the package
    @param string   $fName       Name to be used  for downloaded file

    @return string  Path to downloaded directory
#>
function downloadFile {
    param ($downloadUrl, $name, $fName)

    $storageDir = Join-Path $pwd "downloadtemp"
    $webclient = New-Object System.Net.WebClient
    $split = $downloadUrl.split("/")
    $fileName = $fName
    if ($fName -eq $null) {
      $fileName = $split[$split.Length-1]
    }

    $filePath = Join-Path $storageDir $fileName
    
    if (!(Test-Path -LiteralPath $storageDir -PathType Container)) {
        New-Item -type directory -path $storageDir | Out-Null
    } else {
        logStatus "Cleaning out temporary download directory"
        Remove-Item (Join-Path $storageDir "*") -Recurse -Force
        logStatus "Temporary download directory cleaned"
    }
    
    logStatus "Downloading '$name'. This could take time..."
    $webclient.DownloadFile($downloadUrl, $filePath)
    logStatus "$name downloaded"
    return $filePath
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
        logSuccess "Done with Installation of $name."
    } else {
        logErr "Installation of $name Failed."
    }
    
    $storageDir = Join-Path $pwd "downloadtemp"
    if (Test-Path -LiteralPath $storageDir -PathType Container) {
        logStatus "Clearing temporary storage directory"
        Remove-Item -path $storageDir -force -Recurse
        logStatus "Cleared temporary storage directory"
    }
    
    return ($result -eq 0);
}

<#
    Compare two versions
    
    @param string   $version1  First version string
    @param string   $version2  Second version string
    
    @return int  1 if version1 >= version2, -1 otherwise
#>
function comapreVersion {
    param ($version1, $version2)
    $parts1 = $version1.TrimStart('vV').Split('.')
    $parts2 = $version2.TrimStart('vV').Split('.')
    $range = $parts1.length
    if ($range -lt $parts2.length) {
        $range = $parts2.length
    }

    for ($i = 0; $i -lt $range; $i++) {
        $num1 = 0;
        $num2 = 0;
        if (!([System.Int32]::TryParse($parts1[$i], [ref]$num1)) -or !([System.Int32]::TryParse($parts2[$i], [ref]$num2)))
        {
            logErr "Version compare failed, version string contains non-numeric parts"
            exit 1
        }

        if ($num1 -gt $num2) {
            return 1
        } elseif ($num1 -eq $num2) {
        } else {
            return -1
        }
    }

    return 1
}
#------------------ Download and Install Nodejs If not installed --------------------------------------------------------

$result = checkForPathAndPromptIfNot $nodejsPath "node.js"

if ($result -eq 1) {
    # Download and install nodejs
    $msiPath = downloadFile $nodejsDownloadUrl "node.js" $null
    $result = InstallMSI $msiPath "node.js"
    if (!$result) {
        cleanDownloadDirAndExit 1
    }

    Sleep -s 5
    cleanDownloadDir
} elseif ($result -eq 3) {
    logStatus "Checking for node.js version for compatibility"
    $version = & $nodejsPath -v
    If ($lastexitcode -ne 0) {
        logErr "Failed to retrieve node.js version."
        exit 1;
    }

    $result = comapreVersion $version $nodeJSMinVersion
    if ($result -eq -1) {
        $yes = New-Object System.Management.Automation.Host.ChoiceDescription "&Yes", "Install node.js $nodeJSMinVersion"
        $no = New-Object System.Management.Automation.Host.ChoiceDescription "&No", "Do not install node.js $nodeJSMinVersion"
        $options = [System.Management.Automation.Host.ChoiceDescription[]]($yes, $no)
        logInput "Your node.js version is $version but 'Windows Azure Command Line Tools for Mac and Linux' requires minimum $nodeJSMinVersion, shall I upgrade it to node.js $nodeJSMinVersion for you?"
        $result = $host.ui.PromptForChoice($null, 
            $null, 
            $options, 
        0)
        switch ($result) {
            0 {
                 $msiPath = downloadFile $nodejsDownloadUrl "node.js" $null
                 $result = InstallMSI $msiPath "node.js"
                 if (!$result) {
                     exit 1
                 }
            }
            1 {
                 logErr "node.js minimum version $nodeJSMinVersion is required for 'Windows Azure Command Line Tools for Mac and Linux', Please rerun the script to install it."
                 exit 1
            }
        }
    } else {
        logStatus "Found compactible node.js version $version"
    }
} elseif ($result -eq 2) {
    logErr "node.js is required for 'Windows Azure Command Line Tools for Mac and Linux', Please rerun the script to install it."
    cleanDownloadDirAndExit 1
}

cleanDownloadDir
#------------------ Download and Install Windows Azure Cross Platform Tool If not installed -------------------------------

$result = checkForPathAndPromptIfNot $nodeIAASToolScriptPath "Windows Azure Command Line Tools for Mac and Linux"

if ($result -eq 1) {
    # Download and install IAAS Tool
    $packagePath = $null
    if ($iaasUsePackageName) {
        $packagePath = $iaasPackageName
    } else {
        $packagePath = downloadFile $nodeIAASToolDownloadUrl "Windows Azure Command Line Tools for Mac and Linux" "azure-0.5.3.tgz"
    }

    logStatus "Installing 'Windows Azure Command Line Tools for Mac and Linux'"
    Start-Sleep -s 5
    &$npmpath install $packagePath --global
    If ($lastexitcode -ne 0)
    {
        logErr "npm install of 'Windows Azure Command Line Tools for Mac and Linux' failed"
        cleanDownloadDirAndExit 1
    }

    logSuccess "Done with Installation of 'Windows Azure Command Line Tools for Mac and Linux'. Clearing temporary storage directory"
    Start-Sleep -s 5
    $storageDir = Join-Path $pwd "downloadtemp"
    if (Test-Path -LiteralPath $storageDir -PathType Container) {
        Remove-Item -path $storageDir -force -Recurse
    }
} elseif ($result -eq 2) {
    logErr "'Windows Azure Command Line Tools for Mac and Linux' installation cancelled, Please rerun the script to install it."
    cleanDownloadDirAndExit 1
}

cleanDownloadDir
