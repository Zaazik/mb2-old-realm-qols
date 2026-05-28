@echo off
dotnet build -c Release
exit /b %ERRORLEVEL%
