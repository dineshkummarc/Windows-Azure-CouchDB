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

Add-Type -AssemblyName System.ServiceModel.Web, System.Runtime.Serialization

<#
    Display the usage
#>
function showUsageAndExit() {
    param ($message)
    if ($message) {
        Write-Host "`r`n$message`r`n" -foregroundcolor "red"
    }
    
    Write-Host "`r`nUSAGE: couchdbCreateReplicaDB.ps1 <db-name> <path-to-connection-string-file> [continue-on-http-error]`r`n" -foregroundcolor "green"
    Write-Host "<db-name> : The name of the database to be created and replicated" -foregroundcolor "green"
    Write-Host "<path-to-connection-string-file> : path to CouchDB connection string file" -foregroundcolor "green"
    Write-Host "[continue-on-http-error] : Continue even if one couchDB instance returns error" -foregroundcolor "green"
    exit 1
}

# Gets the commandline arguments

$dbName = $null
$connectionSettingsFile = '.\connectionStrings.json';
$continueOnHttpError = $True
if ($args.Count -eq 3) {
    $dbName =  $args[0];
    $connectionSettingsFile = $args[1];
    $continueOnHttpError = $args[2];
} elseif ($args.Count -eq 2) {
    $dbName =  $args[0];
    $connectionSettingsFile = $args[1];
} else {
    showUsageAndExit "Missing required arguments"
}

if ($continueOnHttpError -eq 'True' -or $continueOnHttpError -eq 'TRUE' -or $continueOnHttpError -eq 'true' ) {
    $continueOnHttpError = $True
} elseif ($continueOnHttpError -eq 'False' -or $continueOnHttpError -eq 'FALSE' -or $continueOnHttpError -eq 'false' ) {
    $continueOnHttpError = $False
} else {
    showUsageAndExit 'Third argument should be a boolean value'
}

if (!(Test-Path -LiteralPath $connectionSettingsFile)) {
    showUsageAndExit "Failed to resolve path to json file [$connectionSettingsFile] containing CouchDB connection strings"
}

<#
    Reads the couchDB connection string file connectionStrings.json
#>
function get-CouchInstances
{
   try
   {
        [hashtable]$result = @{} 
        $jsonString = Get-Content ($connectionSettingsFile)
        $bytes = [Byte[]][System.Text.Encoding]::ASCII.GetBytes($jsonString)
        $jsonReader = [System.Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader($bytes, [System.Xml.XmlDictionaryReaderQuotas]::Max)
        $xml = New-Object Xml.XmlDocument
        $xml.Load($jsonReader)
        # $xml.OuterXml
        $result.couchInstances = @()
        $xml | Select-Xml '//root/item/Couch/item/info/connectionUrl/item' | Foreach {$result.couchInstances += $_.Node.InnerText}
        return $result
   }
   finally
   {
        $jsonReader.Close()
   }
}

function doHTTPRequest2 {
    param($url, $method, $contentType, $body)
    [hashtable]$result = @{}
    [net.httpWebRequest] $req = [net.webRequest]::create($url)
    $req.method = $method
    if($body) {
        if ($contentType) {
            $req.ContentType = $contentType
        }
        
        $buffer = [text.encoding]::ascii.getbytes($body)
        $req.ContentLength = $buffer.length
        $reqst = $req.getRequestStream()
        $reqst.write($buffer, 0, $buffer.length)
        $reqst.flush()
        $reqst.close()
    } else {
        $req.ContentLength = 0
    }

    $req.TimeOut = 50000

    try {
        [net.httpWebResponse] $res = $req.getResponse()
        $resst = $res.getResponseStream()
        $sr = new-object IO.StreamReader($resst)
        $result.success = $True
        $result.StatusCode = $httpResponse.StatusCode
        $result.response = $sr.ReadToEnd()
        $sr.Close()
    } catch [System.Net.WebException] {
        [net.WebResponse] $response = $_.Exception.Response
        [net.HttpWebResponse] $httpResponse = [net.HttpWebResponse] $response;
        if ($httpResponse) {
            $data = $httpResponse.GetResponseStream()
            $stream = new-object System.IO.StreamReader $data 
            $body = $stream.ReadToEnd()
            $result.success = $False
            $result.StatusCode = $httpResponse.StatusCode
            $result.response =  $body
            $stream.Close()
        } else {
            $result.success = $False
            $result.StatusCode = $null
            $result.response =  'Unknown Error'
        }
    } catch [Exception] {
        $result.success = $False
        $result.StatusCode = $null;
        $result.response = $_.Exception.GetType().FullName
    }
    
    return $result;
}

function create-DataBases {
    param ($endpoints)
    foreach ($endpoint in $endpoints) {
        Write-Host "Creating Dataabase on $endpoint" -foregroundcolor "yellow"
        $url = "http://" + $endpoint + "/" + $dbName
        $result2 = (doHTTPRequest2 $url "PUT" null null)
        if ($result2.success) {
            Write-Host $result2.StatusCode -foregroundcolor "green"
            Write-Host $result2.response -foregroundcolor "green"
        } else {
            Write-Host $result2.StatusCode -foregroundcolor "red"
            Write-Host $result2.response -foregroundcolor "red"
            if(!$continueOnHttpError) {
                exit 1
            }
        }
    }
}

function create-Replica {
    param ($endpoints)
    foreach ($endpoint in $endpoints) {
        foreach ($endpoint2 in $endpoints) {
            if($endpoint -eq $endpoint2) {
                # Write-Host "Setting Replicas for $endpoint" -foregroundcolor "yellow"
                continue
            }
            
            $command = "{`"source`":`"http://" + $endpoint2 + "/$dbName`",`"target`":`"$dbName`",`"continuous`":true}"
            echo $command
            $url = "http://" + $endpoint + "/_replicate"
            $result2 = (doHTTPRequest2 $url "POST" "application/json" $command)
            if ($result2.success) {
                Write-Host $result2.StatusCode -foregroundcolor "green"
                Write-Host $result2.response -foregroundcolor "green"
            } else {
                Write-Host $result2.StatusCode -foregroundcolor "red"
                Write-Host $result2.response -foregroundcolor "red"
                if(!$continueOnHttpError) {
                    exit 1
                }
            }
            
        }
    }
}

$result = get-CouchInstances

if($result.couchInstances.length -eq 0) {
    Write-Host "connection string json file does not contain any couchDB connection details" -foregroundcolor "red"
    exit 1
}

create-DataBases $result.couchInstances
create-Replica $result.couchInstances