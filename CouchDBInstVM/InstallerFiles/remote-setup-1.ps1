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
$drive = "C:\"
$couchDownloadUrl = "http://www.gtlib.gatech.edu/pub/apache/couchdb/packages/win32/1.2.0/setup-couchdb-1.2.0_otp_R15B.exe"
$couchServicePrefix = "Apache CouchDB"
$serviceStartRetry = 5
$couchConfigPath = Join-Path $drive "Program Files (x86)\Apache Software Foundation\CouchDB\etc\couchdb\local.ini"

<#
   Prepare the json string with the IP addresses of all nodes for
   initialization. 
#>
$rsInitCmd = "config = {_id: 'rs', members:["
$i = 0;
foreach ($arg in $args)
{
    $rsInitCmd += "{"
    $rsInitCmd += "_id:"
    $rsInitCmd += $i
    $rsInitCmd += ", host:"
    $rsInitCmd += "'" + $arg + "'"
    $rsInitCmd += "}, "
    $i++;
}

$rsInitCmd += "]};"

function logStatus {
    param ($message)
    Write-Host "info:   $message" -foregroundcolor "yellow"
}

function logStatus2 {
    param ($message)
    Write-Host "info:   $message" -foregroundcolor "blue"
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

<#
    Install the package targetted by $innoPath
    
    @param string   $innoPath     Path to the package
    @param string   $name        Name of the package
    
    @return boolean              True if installation succeeded, false on failure.
#>
function RunINNOInstaller {
    param ($innoPath, $name)

    $innoArgs = @()
    $innoArgs += "/sp- /verysilent /norestart"
    $result = (Start-Process -FilePath $innoPath -ArgumentList $innoArgs -verb runas -Wait -PassThru).ExitCode
    if ($result -eq 0) {
        logSuccess "Done with Installation of $name."
        return $True
    } else {
        logErr "Installation of $name Failed."
        return $False
    }
}

<#
    Download the couch installer and install it.
#>
function Download-And-Install-CouchDB {

    $storageDir = Join-Path $pwd "downloadtemp"
    $webclient = New-Object System.Net.WebClient
    $split = $couchDownloadUrl.split("/")
    $fileName = $split[$split.Length-1]
    $filePath = Join-Path $storageDir $fileName 

    if (!(Test-Path -LiteralPath $storageDir -PathType Container)) {
        New-Item -type directory -path $storageDir | Out-Null
    }
    else {
        logStatus "Cleaning out temporary download directory"
        Remove-Item (Join-Path $storageDir "*") -Recurse -Force
        logStatus "Temporary download directory cleaned"
    }

    logStatus "Downloading couchdb installer. This could take time..."
    $webclient.DownloadFile($couchDownloadUrl, $filePath)
    logStatus "couchdb installer downloaded"
    
    logStatus "Applying ACL to the download directroy"
    icacls $storageDir /grant Everyone:F /T

    logStatus "Installing CouchDB"
    $result = RunINNOInstaller $filePath "CouchDB"
    if ($result) {
        logStatus "Installed CouchDB"
    } else {
        logErr "CouchDB installation Failed"
    }
    # Wait for some time before deleting the temp directory
    Start-Sleep -s 5
    logStatus "Clearing temporary storage directory" 
    if (Test-Path -LiteralPath $storageDir -PathType Container) {
        Remove-Item -path $storageDir -force -Recurse
    }
    
    if (!$result) {
        exit 1
    }  
}

<#
    Gets the host IP address
  
    @return string  The host IP address
#>
function getHostIPv4Address {  
    $IPconfigset = Get-WmiObject Win32_NetworkAdapterConfiguration
    foreach ($IPConfig in $IPConfigSet) {
        if ($Ipconfig.IPaddress) {  
            foreach ($addr in $Ipconfig.Ipaddress) { 
                # Select the first IPv4 address
                if ($addr -match "\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}") {
                    return $addr
                }
            }  
        }
    } 
}

<#
    Restart/Start couch service, retry $retry times if fails
#>
function Restart-CouchService {
    # Find the service with prefix $couchServicePrefix
    $couchService = $null
    $findServiceRetryCount = 5
    $retry= 0
    logStatus "Locating service with prefix $couchServicePrefix"
    Start-Sleep -s 10
    do {
        $service=Get-Service | Where-Object {$_.name -like ("{0}*" -f $couchServicePrefix)}
        if(!$service) {
            logStatus2 "No service found with prefix $couchServicePrefix, retrying $retry"
            Start-Sleep -s 7
            $retry = $retry + 1   
        } else {
            if ($service.count) {
                logStatus "Found more than one service prefix $couchServicePrefix, selecting first one"
                $service = $service[0]
            }

            $couchService = $service.Name
            break
        }
    } while($retry -le $findServiceRetryCount);
    
    if (!$couchService) {
        logErr "Failed to find  a service with name srart with $couchServicePrefix"
        exit 1
    } else {
        logStatus "Found CouchDB  service $couchService"
    }
    
    $success = $false;
    $retry = 0;
    logStatus "Trying to Start/restart $couchServicePrefix..."
    do {
            Start-Sleep -s 12
            try 
            {
                $arrService = Get-Service -Name $couchService
                if ($arrService.Status -ne "Running"){
                    Start-Service $couchService
                    logSuccess "The service $couchService Started.."
                } else {
                    Restart-Service $couchService
                    logSuccess "The service $couchService Restarted.."
                }
                
                $success = $true; 
            }
            catch
            {
                if ( $error[0].Exception -match "Microsoft.PowerShell.Commands.ServiceCommandException")
                {
                    Write-Host $error[0] -foregroundcolor "red"
                    if ($retry -gt 5)
                    {                             
                        $message = "Can not execute Restart-Service/Start-Service command. Really exiting with the error: " + $_.Exception.ToString();
                        throw $message;
                    }
                }
                else
                {
                    throw $_.exception
                }
                
                logStatus2 "Sleeping before $retry retry of Restart/Start-Service command"; 
                $retry = $retry + 1;
            }
    } while(!$success);
    
}

logStatus "Start with setup"
logStatus "Adding firewall exception for couch-port"
&netsh advfirewall firewall add rule name="CouchDB (TCP-In)" dir=in action=allow service=any enable=yes profile=any localport=5984 protocol=tcp
logStatus "Downloading and installing CouchDB. This will take some-time..."
Download-And-Install-CouchDB
logStatus "Updating local.ini file with host IP..."
$ipAddress = getHostIPv4Address
$bindAddress = "bind_address = " + $ipAddress
[regex]::Replace(
    [io.file]::readalltext($couchConfigPath), 
    ";bind_address = 127.0.0.1",
    $bindAddress
) | Out-File $couchConfigPath -Encoding ascii –Force
Restart-CouchService
logStatus "Done with setup"