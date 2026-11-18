@echo off
where msbuild.exe >nul 2>nul
if errorlevel 1 (
    echo Run this file in Developer Command Prompt for Visual Studio.
    exit /b 1
)
msbuild.exe "%~dp0FixPoint.sln" /t:Build /p:Configuration=Release /nologo
exit /b %errorlevel%
