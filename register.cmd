@echo off
call "%~dp0scripts\register.cmd"
set "REGISTER_EXIT=%errorlevel%"
if not "%REGISTER_EXIT%"=="0" (
  echo Press any key to close this window.
  pause >nul
)
exit /b %REGISTER_EXIT%
