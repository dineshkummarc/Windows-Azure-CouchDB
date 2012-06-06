$workerRoleLocation = $args[0];

#[System.IO.File]::AppendAllText("C:\temp\log.txt", $args[0]);
#[System.IO.File]::AppendAllText("C:\temp\log.txt", $workerRoleLocation);

#Move to Node Folder
$locationToNodeJs = [System.IO.Path]::Combine($workerRoleLocation , "CouchHostWorkerRole\Node");
Write-Host $workerRoleLocation;
Set-Location $locationToNodeJs;
#[System.IO.File]::AppendAllText("C:\temp\log.txt", $locationToNodeJs);
npm install azure express jade nano node-uuid;

#Restore original working directory.
Set-Location $workerRoleLocation;
Get-Location;