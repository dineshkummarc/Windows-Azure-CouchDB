$couchInstallFolder = $args
$couchdbDownloadUrl = "http://localhost/couchdb.zip"
$couchdbBinaryTarget = Join-Path "$couchInstallFolder" "CouchDB"
$couchdbBatFile = Join-Path (Join-Path "$couchdbBinaryTarget" "bin") "couchdb.bat"
$storageDir = Join-Path "$couchInstallFolder" "downloadtemp"

function Download-Binaries()
{
    if(Test-Path -LiteralPath $couchdbBatFile -PathType Leaf){
        Write-Host "CouchDB binaries already downloaded. Delete these binaries if you want to download again."
        return
    }
    
    Write-Host $storageDir
    $webclient = New-Object System.Net.WebClient
    $split = $couchdbDownloadUrl.split("/")
    $fileName = $split[$split.Length - 1]
    $filePath = Join-Path $storageDir $fileName
    
    if(!(Test-Path -LiteralPath $storageDir -PathType Container))
    {
        New-Item -type directory -path $storageDir | Out-Null
    }
    else
    {
        Write-Host "Cleaning out temporary download directory"
        Remove-Item (Join-Path $storageDir "*") -Recurse -Force
        Write-Host "Temporary download directory cleaned"
    }
    
    Write-Host "Downloading CouchDB binaries. This could take time..."
    $webclient.DownloadFile($couchdbDownloadUrl, $filePath)
    Write-Host "CouchDB binaries downloaded. Unzipping.."
    
    $shell_app = new-object -com shell.application
    $zip_file = $shell_app.namespace($filePath)
    $destination = $shell_app.namespace($storageDir)
    
    $destination.Copyhere($zip_file.items())
    
    Write-Host "Binaries Unzipped. Copying to destination"
    $unzipDir = GetUnzipPath($storageDir, $filePath)
    Copy-Item $unzipDir -destination "$couchdbBinaryTarget" -Recurse
    Write-Host "Done copying. Clearing temporary storage directory"
    
    if (Test-Path -LiteralPath $storageDir -PathType Container) {
        Remove-Item -path $storageDir -force -Recurse
    }

	Write-Host "CouchDb binaries are downloaded."   
}

function GetUnzipPath {
    Param($downloadDir, $downloadFile)
    $dir = Get-Item (Join-Path $storageDir "*") -Exclude $fileName
    return $dir.FullName
}

Download-Binaries