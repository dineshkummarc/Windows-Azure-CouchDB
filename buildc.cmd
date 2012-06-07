@echo off

setlocal

if "%1"=="/?" goto usage
if "%1"=="-?" goto usage

@echo Building CouchDBInstWR CouchDBInstVM and for external release
@echo Deleting previous build

if exist build rmdir /S /Q build
if exist Inst4WA\bin rmdir /S /Q Inst4WA\bin 
if exist Inst4WA\DeployCmdlets4WA\bin rmdir /S /Q  Inst4WA\DeployCmdlets4WA\bin 

if exist CouchDBDeployCmdletsSetup\bin  rmdir /S /Q CouchDBDeployCmdletsSetup\bin 
if exist CouchDBInstWR\ReplCouchDB\HelperLib\bin rmdir /S /Q CouchDBInstWR\ReplCouchDB\HelperLib\bin
if exist CouchDBInstWR\ReplCouchDB\CouchHostWorkerRole\bin rmdir /S /Q CouchDBInstWR\ReplCouchDB\CouchHostWorkerRole\bin
if exist CouchDBInstVM\CouchDBInstVM\bin rmdir /S /Q CouchDBInstVM\CouchDBInstVM\bin
if exist CouchDBInstVM\InstallerFiles\bin rmdir /S /Q CouchDBInstVM\InstallerFiles\bin
if exist CouchDBDeployCmdlets\bin  rmdir /S /Q CouchDBDeployCmdlets\bin 

if exist Inst4WA\obj rmdir /S /Q Inst4WA\obj 
if exist Inst4WA\DeployCmdlets4WA\obj rmdir /S /Q  Inst4WA\DeployCmdlets4WA\obj 

if exist CouchDBDeployCmdletsSetup\obj  rmdir /S /Q CouchDBDeployCmdletsSetup\obj 
if exist CouchDBInstWR\ReplCouchDB\HelperLib\obj rmdir /S /Q CouchDBInstWR\ReplCouchDB\HelperLib\obj
if exist CouchDBInstWR\ReplCouchDB\CouchHostWorkerRole\obj rmdir /S /Q CouchDBInstWR\ReplCouchDB\CouchHostWorkerRole\obj
if exist CouchDBInstVM\CouchDBInstVM\obj rmdir /S /Q CouchDBInstVM\CouchDBInstVM\obj
if exist CouchDBInstVM\InstallerFiles\obj rmdir /S /Q CouchDBInstVM\InstallerFiles\obj
if exist CouchDBDeployCmdlets\obj  rmdir /S /Q CouchDBDeployCmdlets\obj 

@echo Building Debug
set Configuration=Debug

msbuild Inst4WA\Inst4WA.sln
if errorlevel 1 goto error

msbuild CouchDBInstWR\CouchDBInstWR.sln
if errorlevel 1 goto error

msbuild CouchDBInstVM\CouchDBInstVM.sln
if errorlevel 1 goto error

mkdir build\CouchDBInstWR\Debug
mkdir build\CouchDBInstVM\Debug

COPY /Y CouchDBInstWR\CouchDBDeployCmdletsSetup\bin\Debug build\CouchDBInstWR\Debug
COPY /Y CouchDBInstVM\CouchDBInstVM\bin\Debug build\CouchDBInstVM\Debug
COPY /Y Inst4WA\bin\Debug build\CouchDBInstWR\Debug
COPY /Y Inst4WA\bin\Debug build\CouchDBInstVM\Debug

@echo Building Release
set Configuration=Release

msbuild Inst4WA\Inst4WA.sln
if errorlevel 1 goto error

msbuild CouchDBInstWR\CouchDBInstWR.sln
if errorlevel 1 goto error

msbuild CouchDBInstVM\CouchDBInstVM.sln
if errorlevel 1 goto error

mkdir build\CouchDBInstWR\Release
mkdir build\CouchDBInstVM\Release

COPY /Y CouchDBInstWR\CouchDBDeployCmdletsSetup\bin\Release build\CouchDBInstWR\Release
COPY /Y CouchDBInstVM\CouchDBInstVM\bin\Release build\CouchDBInstVM\Release
COPY /Y Inst4WA\bin\Release build\CouchDBInstWR\Release
COPY /Y Inst4WA\bin\Release build\CouchDBInstVM\Release

goto noerror

:error
@echo !!! Build Error !!!
goto end

:noerror
exit /b 0

:usage
@echo.
@echo Builds CouchDBInstWR and CouchDBInstVM for external release
@echo Usage: build
goto end

:end
endlocal