@echo off
setlocal

for %%I in ("%~dp0..") do set "PROJECT_ROOT=%%~fI"
set "VIEWER_EXE=%~dp0LivpViewer.exe"
if not exist "%VIEWER_EXE%" set "VIEWER_EXE=%PROJECT_ROOT%\dist\LivpViewer-win-x64\LivpViewer.exe"

if not exist "%VIEWER_EXE%" (
  echo LivpViewer.exe was not found. Run build.cmd first.
  exit /b 1
)

"%VIEWER_EXE%" --register
exit /b %errorlevel%
