@echo off
rem Inner-loop build script: linux-x64 self-contained single-file publish only.
rem Full cross-platform build is build.bat.
rem
rem Written by Derek Pascarella (ateam)

setlocal EnableDelayedExpansion

set /p VERSION=<version.txt
rem Strip leading 'v' so a version.txt of either "1.0.0" or "v1.0.0" works.
if /i "%VERSION:~0,1%"=="v" set "VERSION=%VERSION:~1%"

echo ================================================
echo Building TIFF Town v%VERSION% for linux-x64
echo ================================================
echo.

if exist "_releases\TiffTown.v%VERSION%-linux-x64" (
    rd /s /q "_releases\TiffTown.v%VERSION%-linux-x64"
)

echo Formatting code...
dotnet format TiffTown.sln
if %ERRORLEVEL% neq 0 goto :error

rem dotnet format reads C# only, so XAML line endings are handled separately.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0normalize-xaml.ps1"
if %ERRORLEVEL% neq 0 goto :error
echo.

dotnet publish src\TiffTown.App\TiffTown.App.csproj ^
    -c Release -r linux-x64 --self-contained true ^
    -o "_releases\TiffTown.v%VERSION%-linux-x64"
if %ERRORLEVEL% neq 0 goto :error

copy /Y LICENSE.txt "_releases\TiffTown.v%VERSION%-linux-x64\" >nul 2>&1

rem Tar from inside WSL to chmod +x the binary first. Cmd.exe's native tar
rem drops Unix exec bits.
wsl bash -c "chmod +x '_releases/TiffTown.v%VERSION%-linux-x64/TiffTown' && cd _releases && tar -czf TiffTown.v%VERSION%-linux-x64.tar.gz TiffTown.v%VERSION%-linux-x64" < NUL
if %ERRORLEVEL% neq 0 echo Warning: failed to create linux-x64 tar.gz

echo.
echo Built: _releases\TiffTown.v%VERSION%-linux-x64.tar.gz
goto :end

:error
echo Build failed with code %ERRORLEVEL%
pause
exit /b %ERRORLEVEL%

:end
