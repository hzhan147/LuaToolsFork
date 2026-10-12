@echo off
setlocal

pushd "%~dp0"
if errorlevel 1 exit /b 1

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK was not found. Install the SDK and reopen your terminal.
    popd
    exit /b 1
)

echo Building Debug...
dotnet build LuaToolsGui.sln -c Debug -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false -v minimal
if errorlevel 1 goto :failed

echo Building Release...
dotnet build LuaToolsGui.sln -c Release -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false -v minimal
if errorlevel 1 goto :failed

echo Publishing Release...
dotnet publish src/LuaToolsGui/LuaToolsGui.csproj -c Release --no-build --no-restore -v minimal
if errorlevel 1 goto :failed

echo.
echo Build completed successfully.
echo Debug:   %CD%\src\LuaToolsGui\bin\Debug\net8.0-windows
echo Release: %CD%\src\LuaToolsGui\bin\Release\net8.0-windows\publish
popd
exit /b 0

:failed
set "buildExitCode=%errorlevel%"
echo.
echo ERROR: Build failed with exit code %buildExitCode%.
popd
exit /b %buildExitCode%
