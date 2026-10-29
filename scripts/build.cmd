@echo off
setlocal

for %%I in ("%~dp0..") do set "PROJECT_ROOT=%%~fI"
set "PROJECT_FILE=%PROJECT_ROOT%\src\LivpViewer\LivpViewer.csproj"
set "RELEASE_FOLDER=%PROJECT_ROOT%\dist\LivpViewer-win-x64"
set "ARCHIVE_FILE=%PROJECT_ROOT%\dist\LivpViewer-win-x64.zip"
set "LOG_FILE=%PROJECT_ROOT%\build.log"

echo LIVP Viewer build started > "%LOG_FILE%"
echo Project: "%PROJECT_ROOT%" >> "%LOG_FILE%"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 >> "%LOG_FILE%"
  type "%LOG_FILE%"
  exit /b 1
)

if exist "%RELEASE_FOLDER%" rmdir /s /q "%RELEASE_FOLDER%"
if exist "%ARCHIVE_FILE%" del /q "%ARCHIVE_FILE%"

echo Building. This may take several minutes; output will appear when complete.
dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "%RELEASE_FOLDER%" >> "%LOG_FILE%" 2>&1
set "BUILD_EXIT=%errorlevel%"
type "%LOG_FILE%"
if not "%BUILD_EXIT%"=="0" (
  echo Build failed. See "%LOG_FILE%".
  exit /b %BUILD_EXIT%
)

copy /y "%PROJECT_ROOT%\scripts\register.cmd" "%RELEASE_FOLDER%\" >nul
copy /y "%PROJECT_ROOT%\THIRD-PARTY.md" "%RELEASE_FOLDER%\" >nul

where tar >nul 2>nul
if not errorlevel 1 (
  tar -a -c -f "%ARCHIVE_FILE%" -C "%RELEASE_FOLDER%" .
  if errorlevel 1 echo ZIP packaging failed. The executable folder is ready at "%RELEASE_FOLDER%".
)

echo Executable folder: "%RELEASE_FOLDER%"
if exist "%ARCHIVE_FILE%" echo ZIP package: "%ARCHIVE_FILE%"
echo Build succeeded. >> "%LOG_FILE%"
echo Executable: "%RELEASE_FOLDER%\LivpViewer.exe" >> "%LOG_FILE%"
exit /b 0
