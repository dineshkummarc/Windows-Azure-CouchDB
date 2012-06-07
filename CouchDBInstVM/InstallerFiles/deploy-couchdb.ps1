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

$mylocation =  [System.IO.Path]::GetDirectoryName($MyInvocation.MyCommand.Path)
Set-Location $mylocation

$nodejsDownloadUrl = $null
$nodeIAASToolDownloadUrl = $null
$nodeJSMinVersion = $null
$iaasUsePackageName = $null
$iaasPackageName = $null
[hashtable]$imageLocationMapping = @{}

$osArchitecture = Get-WmiObject -ComputerName localhost Win32_OperatingSystem  | select OSArchitecture

$programFiles = $env:SystemDrive + "\Program Files";
if ($osArchitecture.OSArchitecture -eq "64-bit") 
{
    $programFiles += " (x86)";
}

$nodejsPath = $programFiles
$npmpath = $programFiles;
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
# iaas tool management certificate name
$mgmtCertificate = "managementCertificate.pem"
# iaas tool config file name
$configFile = "config.json"
# variable to hold the subscription ID
$subscriptionID = $null
$serHost = 'management.core.windows.net'

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

#----------------------------------------Ensure User is Running the Script in elevated mode----------------------------------

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal $identity
$elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) {
    $error = "Sorry, you need to run this script"
    if ([System.Environment]::OSVersion.Version.Major -gt 5) {
        $error += " in an elevated shell."
    } else {
        $error += " as Administrator."
    }

    logErr $error
    exit 1
}

#--------------------------------------------------------------------------------------------------------------------------

<#
    Display the usage
#>
function showUsageAndExit() {
    Write-Host "`r`nUSAGE: deploy-couchdb.ps1 <instance-count> <dns-prefix> <image-name> <password> <location> <pub-settings-file-path>`r`n" -foregroundcolor "green"
    Write-Host " instance-count         : Number of nodes in CouchDB replication" -foregroundcolor "green"
    Write-Host " dns-prefix             : The hosted service name" -foregroundcolor "green"
    Write-Host " image-name             : Name of the Windows Image with WIN-RM Service enabled" -foregroundcolor "green"
    Write-Host " password               : Password that meets Windows password complexity requirement" -foregroundcolor "green"
    Write-Host " location               : The location in which the hosted service resides" -foregroundcolor "green"
    Write-Host " pub-settings-file-path : Path to publish settings file" -foregroundcolor "green"
    exit 1
}

#--------------------------------------------------------------------------------------------------------------------------

# Gets the commandline arguments

if ($args.Count -ne 6) {
    logErr "Missing required arguments`r`n"
    showUsageAndExit
}

$NODECOUNT = 0;
if (!([System.Int32]::TryParse($args[0], [ref]$NODECOUNT)) -or ($NODECOUNT -eq 0))
{
    logErr "The <instance-count> argument should be number greater than 0`r`n"
    showUsageAndExit
}

if (!(Test-Path -LiteralPath $args[5]  -PathType Leaf)) 
{
    logErr "The <pub-settings-file-path> argument should refer to a file containing publish settings`r`n"
    showUsageAndExit
}

$DNSPREFIX = $args[1]
$IMG = $args[2]
$PASSWORD = $args[3]
$LOCATION = $args[4]
$PUBLISHPROFILEPATH = $args[5]
$VMNAMEPREFIX = "CouchVM"
$USER = "Administrator"
$WINRMIMGURL = $null
#--------------------------------------------------Handle possible max environment block size issue-------------------------

$pathEnv = [environment]::GetEnvironmentVariable("Path")
$pathEnv = ($programFiles + "\nodejs") + ";" + $pathEnv
[Environment]::SetEnvironmentVariable("Path", $pathEnv, "Process")

#--------------------------------------------------Read setup.xml file for prerequisite locations----------------------------

logStatus "Reading setup.xml for node.js and 'Windows Azure Command Line Tools for Mac and Linux' package locations and version"

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

logStatus "Retrieved package locations and version from setup.xml"

#--------------------------------------------------Read imagelocations.xml file ---------------------------------------------

logStatus "Reading imagelocations.xml for list of available locations"

if (!(Test-Path -LiteralPath (Join-Path $pwd "imagelocations.xml")  -PathType Leaf)) 
{
    logErr "Unable to locate imagelocations.xml file"
    exit 1
}

[xml]$imagelocationsfile = Get-Content (Join-Path $pwd "imagelocations.xml")

foreach ($imagelocation in $imagelocationsfile.imagelocations.imagelocation) {
   $imageLocationMapping.Add($imagelocation.location, $imagelocation.imageurl)
}

if ($imageLocationMapping.ContainsKey($LOCATION)) {
     $WINRMIMGURL = $imageLocationMapping[$LOCATION]
} else {
     logErr "Could not resolve the <location> parameter '$LOCATION'"
     logStatus "Available locations are:"
     foreach ($location in $imageLocationMapping.Keys) {
         Write-Host "            $location" -foregroundcolor "green"
     }
}

logStatus "Retrieved image locations from  imagelocations.xml"
#----------------------------------------------------------------------------------------------------------------------------

logStatus "Checking Virtual Box configuration for WinRM compatibility"
# Key in Registry containing all Network Adapters
pushd 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}'
# Find all VirtualBox adapters and update them accordingly (Ignore and continue on error)
$vbEntryFound = $False
$rejectedVBModi = $False
$asked = $False
$vb = 1
dir -ea 0  | % {
    $node = $_.pspath
    $desc = gp $node -name driverdesc
    if ($desc -like "*virtualbox host-only*") {
        $deviceType = Get-ItemProperty $node -Name "*NdisDeviceType"
        if(!$deviceType) {
            if (!$asked) {
                logStatus "WinRM enabling requires VirtualBox’s 'Unidentified Network' to be marked as 'Not A True External Network Connection'"
                logStatus "This will not affect Virtual Box functionaility"
                $asked = $True
            }

            logInput "Found Virtual Box adapter# $vb, Process?"
            $vb++
            if ($host.ui.PromptForChoice($null, $null, [Management.Automation.Host.ChoiceDescription[]]@("&Yes", "&No"), 0) -eq $false) {
                Set-ItemProperty $node -Name "*NdisDeviceType" -Value 1 -Type "DWORD"
                $vbEntryFound = $True
            } else {
                $rejectedVBModi = $True
            }
        } else {
            $type = Get-ItemProperty $node -Name "*NdisDeviceType"
            if ($type."*NdisDeviceType" -ne 1) {
                if (!$asked) { 
                    logStatus "WinRM enabling requires VirtualBox’s 'Unidentified Network' to be marked as 'Not A True External Network Connection'"
                    logStatus "This will not affect Virtual Box functionaility"
                    $asked = $True
                }

                logInput "Found Virtual Box adapter# $vb, Process?"
                $vb++
                if ($host.ui.PromptForChoice($null, $null, [Management.Automation.Host.ChoiceDescription[]]@("&Yes", "&No"), 0) -eq $false) {
                    Set-ItemProperty $node -Name "*NdisDeviceType" -Value 1 -Type "DWORD"
                } else {
                    $rejectedVBModi = $True
                }
            }
        }
    }
}
popd

if ($rejectedVBModi) {
    logInput "Some of the VirtualBox AdapterType skipped, this will cause WinRM config to fail, Continue?"
    if ($host.ui.PromptForChoice($null, $null, [Management.Automation.Host.ChoiceDescription[]]@("&No", "&Yes"), 0) -eq $true) {
    } else {
       exit 1
    }
}

if ($vbEntryFound) {
    logStatus "Restarting VirtualBox network adapters"
    # Disable and re-enable all VirtualBox network adapters to enforce new setting
    gwmi win32_networkadapter | ? {$_.name -like "*virtualbox host-only*" } | % {
        # Disable
        Write-Host -nonew "info: Disabling $($_.name) ... "  -foregroundcolor "yellow"
        $result = $_.Disable()
        if ($result.ReturnValue -eq -0) { Write-Host " success." -foregroundcolor "green"} else { Write-Host " failed." -foregroundcolor "red"}
        # Enable
        Write-Host -nonew "info: Enabling $($_.name) ... "  -foregroundcolor "yellow"
        $result = $_.Enable()
        if ($result.ReturnValue -eq -0) { Write-Host " success." -foregroundcolor "green" } else { Write-Host " failed." -foregroundcolor "red" }
    }
}

logStatus "Done with Virtual Box configuration checking"

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
        logStatus "Cleaning out temporary download directory"
        Remove-Item (Join-Path $storageDir "*") -Recurse -Force
        logStatus "Temporary download directory cleaned"
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
    param ($downloadUrl, $name, $fname)

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
        logSuccess "Done with Installation of $name. Clearing temporary storage directory"
    } else {
        logErr "Installation of $name Failed. Clearing temporary storage directory"
    }
    
    $storageDir = Join-Path $pwd "downloadtemp"
    if (Test-Path -LiteralPath $storageDir -PathType Container) {
        Remove-Item -path $storageDir -force -Recurse
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
        cleanDownloadDirAndExit 1
    }

    $result = comapreVersion $version $nodeJSMinVersion
    if ($result -eq -1) {
        $yes = New-Object System.Management.Automation.Host.ChoiceDescription "&Yes", "Install node.js $nodeJSMinVersion"
        $no = New-Object System.Management.Automation.Host.ChoiceDescription "&No", "Do not install node.js $nodeJSMinVersion"
        $options = [System.Management.Automation.Host.ChoiceDescription[]]($yes, $no)
        logInput "Your node.js version is $version but One-Click requires minimum $nodeJSMinVersion, shall I upgrade it to node.js $nodeJSMinVersion for you?"
        $result = $host.ui.PromptForChoice($null, 
            $null, 
            $options, 
        0)
        switch ($result) {
            0 {
                 $msiPath = downloadFile $nodejsDownloadUrl "node.js" $null
                 $result = InstallMSI $msiPath "node.js"
                 if (!$result) {
                     cleanDownloadDirAndExit 1
                 }

                 cleanDownloadDir
            }
            1 {
                 logErr "node.js minimum version $nodeJSMinVersion is required for One-Click install, Please rerun the script to install it."
                 cleanDownloadDirAndExit 1
            }
        }
    } else {
        logStatus "Found compactible node.js version $version"
    }
} elseif ($result -eq 2) {
    logErr "node.js is required for One-Click install, Please rerun the script to install it."
    cleanDownloadDirAndExit 1
}

#------------------ Download and Install Windows Azure Cross Platform Tool If not installed -------------------------------

$result = checkForPathAndPromptIfNot $nodeIAASToolScriptPath "Windows Azure Command Line Tools for Mac and Linux"

if ($result -eq 1) {
    # (Download and) install Windows Azure Cross Platform Tool
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
    logErr "'Windows Azure Command Line Tools for Mac and Linux' is required for One-Click install, Please rerun the script to install it."
    cleanDownloadDirAndExit 1
}

cleanDownloadDir
#------------------ Enable WinRM service in this machine ------------------------------------------------------------------

logStatus "Enabling WinRM service on your machine."
logStatus "Adding firewall rule for WinRM port."
netsh advfirewall firewall add rule name="Windows Remote Management (HTTP-In)" dir=in action=allow service=any enable=yes profile=any localport=5985 protocol=tcp
# netsh advfirewall firewall add rule name="ICMPv6 echo" dir=in action=allow enable=yes protocol=icmpv6:128,any
If ($lastexitcode -ne 0)
{
    logErr "Failed to add firewall rule for WinRM"
    exit 1
}
logStatus "'Configuring WinRM."
winrm quickconfig -quiet 
If ($lastexitcode -ne 0)
{
    logErr "WinRM Quick Config failed"
    exit 1
}
winrm set winrm/config/service/auth '@{Basic="true"}'
If ($lastexitcode -ne 0)
{
    logErr "WinRM Auth Config failed"
    exit 1
}
winrm set winrm/config/client '@{AllowUnencrypted="true"}'
If ($lastexitcode -ne 0)
{
    logErr "WinRM Encrpt Config failed"
    exit 1
}
winrm set winrm/config/client '@{TrustedHosts="*"}'
If ($lastexitcode -ne 0)
{
    logErr "WinRM Trusted Host Config failed"
    exit 1
}

logSuccess "WinRM client configured on your machine"

#------------------ Import the publisher settings file if not already imported and get the subscription ID-----------------

$needImport = $True
# $needImport = !(Test-Path -LiteralPath ($nodeIAASToolConfigPath + $mgmtCertificate))
# $needImport = $needImport -or !(Test-Path -LiteralPath ($nodeIAASToolConfigPath + $configFile))
if($needImport)
{
    logStatus "Importing Publisher settings file"
    & $nodejsPath $nodeIAASToolAzureJS account import $PUBLISHPROFILEPATH
    If ($lastexitcode -ne 0)
    {
        logErr "Failed to import the publish settings file"
        exit 1
    }
}

try
{
    $jsonString = Get-Content ($nodeIAASToolConfigPath + $configFile)
    $bytes = [Byte[]][System.Text.Encoding]::ASCII.GetBytes($jsonString)
    $jsonReader = [System.Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader($bytes, [System.Xml.XmlDictionaryReaderQuotas]::Max)
    $xml = New-Object Xml.XmlDocument
    $xml.Load($jsonReader)
    $subscriptionID = ($xml | Select-Xml '//subscription')
    if ($subscriptionID -eq $null) 
    {
        logErr "Failed to retrieve the subscription from the config file"
        exit 1
    }

    $serHost = ($xml | Select-Xml '//endpoint')
    if ($serHost -eq $null) 
    {
        logStatus "Unable to read service endpoint from config, default to production"
        $serHost = "management.core.windows.net"
    }

    logStatus "Host: $serHost"
}
catch
{
    logErr $_exception;
    exit 1
}
finally
{
    $jsonReader.Close()
}
# -----------------------------------------------Validate image------------------------------------------------------------

logStatus "Validating image '$IMG'"
$PEMFILE = $nodeIAASToolConfigPath + $mgmtCertificate
& $nodejsPath validate-image-name.js lib=$nodeIAASToolLibPath pem=$PEMFILE s=$subscriptionID imagename=$IMG host=$serHost
If ($lastexitcode -eq 1)
{
    logErr "Image validation failed"
    exit 1
} elseIf ($lastexitcode -eq 0) {
} elseIf ($lastexitcode -eq 2) {
     $yes = New-Object System.Management.Automation.Host.ChoiceDescription "&Yes", "Create a Windows image $IMG with WinRM enabled"
     $no = New-Object System.Management.Automation.Host.ChoiceDescription "&No", "Do not create a Windows image with WinRM enabled"
     $options = [System.Management.Automation.Host.ChoiceDescription[]]($yes, $no)
     logInput "There is no image with name $IMG in your subscription, Do you want to create a Windows image $IMG with WinRM enabled?"
     $result = $host.ui.PromptForChoice($null, 
         $null, 
         $options, 
     0)
     switch ($result) {
         0 {
              logStatus "Checking blob url for 'Windows WinRM Image' in the image repository"
              if (!$WINRMIMGURL) {
                  logErr "Failed to retrieve WinRM Windows Image url for '$LOCATION' from imagelocations.xml"
                  exit 1
              }

              logStatus "Found 'Windows WinRM Image' blob url for the location '$LOCATION'"

              if ($LOCATION -eq "Windows Azure Preview") {
                  $env:blobapistage=37
              } else {
                  $env:blobapistage=38
              }
              

              logStatus "Verifying image container"
              $PEMFILE = $nodeIAASToolConfigPath + $mgmtCertificate
              & $nodejsPath init-storage.js lib=$nodeIAASToolLibPath pem=$PEMFILE s=$subscriptionID location="$LOCATION" host=$serHost
              If ($lastexitcode -ne 0)
              {
                  logErr "Verification of image container failed"
                  exit 1
              }

              logStatus "vm image create $IMG $WINRMIMGURL"
              & $nodejsPath $nodeIAASToolAzureJS vm image create $IMG $WINRMIMGURL --os windows --location "$LOCATION"
              If ($lastexitcode -ne 0) {
                  logErr "Attempt to create Windows image with WinRM enabled failed"
                  exit 1
              }
         }
         1 {
              logErr "Rerun your script with <image-name> as name of the Windows image with WinRM enabled"
              exit 1
         }
     }
}

# -----------------------------------------------Create VMs From the Windows Image-----------------------------------------
$i = 3389;
$CONNECT = ""; 
While ($i -lt (3389 + $NODECOUNT)) 
{
    $VMNAME = $VMNAMEPREFIX + "-" + $i
    logStatus "vm create $DNSPREFIX $IMG $USER $PASSWORD -r $i --vm-name $VMNAME -l '$LOCATION' $CONNECT"
    & $nodejsPath $nodeIAASToolAzureJS vm create -l $LOCATION $DNSPREFIX $IMG $USER $PASSWORD -r $i --vm-name $VMNAME -l "$LOCATION" $CONNECT
    If ($lastexitcode -ne 0)
    {
        logErr "Failed to create VM"
        exit 1
    }
    
    logStatus "VM '$VMNAME' created"
    $CONNECT = "-c"
    $i++;
}

# -----------------------------------------------Adding endpoint mapping for WinRM Service----------------------------------
$i = 3389;
$j = 5985;
While ($i -lt (3389 + $NODECOUNT)) 
{
    $VMNAME = $VMNAMEPREFIX + "-" + $i
    logStatus "azure.cmd vm endpoint create $VMNAME $j 5985 -d $DNSPREFIX"
    & $nodejsPath $nodeIAASToolAzureJS vm endpoint create $VMNAME $j 5985 -d $DNSPREFIX
    If ($lastexitcode -ne 0)
    {
        logErr "Failed to add WinRM endpoint to VM"
        exit 1
    }
    
    logStatus "endpoint mapping [$j, 5985] added for the VM '$VMNAME'"
    $i++;
    $j++;
}
# -----------------------------------------------Adding endpoint mapping for CouchDB Service--------------------------------
$i = 3389;
# Skip NODECOUNT ports to avoid conflict with WinRM ports
$j = 5986 + $NODECOUNT;
While ($i -lt (3389 + $NODECOUNT)) 
{
    $VMNAME = $VMNAMEPREFIX + "-" + $i
    logStatus "azure.cmd vm endpoint create $VMNAME $j 5984 -d $DNSPREFIX"
    & $nodejsPath $nodeIAASToolAzureJS vm endpoint create $VMNAME $j 5984 -d $DNSPREFIX
    If ($lastexitcode -ne 0)
    {
        logErr "Failed to add CouchDB endpoint to VM"
        exit 1
    }
    
    logStatus "endpoint mapping [$j, 5984] added for the VM '$VMNAME'"
    $i++;
    $j++;
}

# -----------------------------------------------Check VMs status----------------------------------------------------------
logStatus "Checking VM(s) Status"
$PEMFILE = $nodeIAASToolConfigPath + $mgmtCertificate
& $nodejsPath check-status.js lib=$nodeIAASToolLibPath pem=$PEMFILE s=$subscriptionID dnsprefix=$DNSPREFIX user=$USER password=$PASSWORD remoteport=5985 host=$serHost
If ($lastexitcode -ne 0)
{
    logErr "Status Check failed"
    exit 1
}

logStatus "All VMs are ready"
# -----------------------------------------------Execute the remote script--------------------------------------------------

logStatus "Initiating couchDB installation on VMs "
invoke-expression -Command .\remote-setup.ps1
