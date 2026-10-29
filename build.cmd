@echo off
call "%~dp0scripts\build.cmd"
set "BUILD_EXIT=%errorlevel%"
echo.
echo Build result code: %BUILD_EXIT%
echo Log file: "%~dp0build.log"
echo Press any key to close this window.
pause >nul
exit /b %BUILD_EXIT%
