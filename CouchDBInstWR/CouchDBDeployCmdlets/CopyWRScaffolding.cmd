::Batch file to copy the scaffodling items from worker role to Cmdlet folders

@ECHO OFF

setlocal

	set solutionDir=%2
	set targetDir=%1
	set configuration=%3
	
	set srcDir=%solutionDir%ReplCouchDB\CouchHostWorkerRole\bin\%configuration%\
	set destDir=%targetDir%Scaffolding\CouchDB\WorkerRole\

	call:CopyFiles %srcDir% %destDir% Startup
	call:CopyFiles %srcDir% %destDir% WebApp
	COPY /Y %srcDir%Microsoft.WindowsAzure.CloudDrive.dll %destDir%Microsoft.WindowsAzure.CloudDrive.dll
	COPY /Y %srcDir%Microsoft.WindowsAzure.Diagnostics.dll %destDir%Microsoft.WindowsAzure.Diagnostics.dll
	COPY /Y %srcDir%Microsoft.WindowsAzure.StorageClient.dll %destDir%Microsoft.WindowsAzure.StorageClient.dll
	COPY /Y %srcDir%Microsoft.WindowsAzure.CloudDrive.xml %destDir%Microsoft.WindowsAzure.CloudDrive.xml
	COPY /Y %srcDir%Microsoft.WindowsAzure.Diagnostics.xml %destDir%Microsoft.WindowsAzure.Diagnostics.xml
	COPY /Y %srcDir%Microsoft.WindowsAzure.StorageClient.xml %destDir%Microsoft.WindowsAzure.StorageClient.xml
	COPY /Y %srcDir%HelperLib.dll  %destDir%HelperLib.dll
	COPY /Y %srcDir%CouchHostWorkerRole.dll %destDir%CouchHostWorkerRole.dll
	COPY /Y %srcDir%CouchHostWorkerRole.dll.config %destDir%CouchHostWorkerRole.dll.config
	
endlocal
GOTO:EOF
	
:CopyFiles
	setlocal
		
	set srcDir=""
	set srcDir=%1%3
			
	set destinationDir=""
	set destinationDir=%2%3
	mkdir %destinationDir%
	XCOPY /E/Y "%srcDir%" "%destinationDir%"
		
	endlocal
GOTO:EOF
	


