@echo off
rem Inner-loop build script: win-x64 self-contained single-file publish only.
rem Full cross-platform build is build.bat.
rem
rem Written by Derek Pascarella (ateam)

setlocal EnableDelayedExpansion

set /p VERSION=<version.txt
rem Strip leading 'v' so a version.txt of either "1.0.0" or "v1.0.0" works.
if /i "%VERSION:~0,1%"=="v" set "VERSION=%VERSION:~1%"

echo ================================================
echo Building TIFF Town v%VERSION% for win-x64
echo ================================================
echo.

if exist "_releases\TiffTown.v%VERSION%-win-x64" (
    rd /s /q "_releases\TiffTown.v%VERSION%-win-x64"
)

echo Formatting code...
dotnet format TiffTown.sln
if %ERRORLEVEL% neq 0 goto :error

rem dotnet format reads C# only, so XAML line endings are handled separately.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0normalize-xaml.ps1"
if %ERRORLEVEL% neq 0 goto :error
echo.

dotnet publish src\TiffTown.App\TiffTown.App.csproj ^
    -c Release -r win-x64 --self-contained true ^
    -o "_releases\TiffTown.v%VERSION%-win-x64"
if %ERRORLEVEL% neq 0 goto :error

copy /Y LICENSE.txt "_releases\TiffTown.v%VERSION%-win-x64\" >nul 2>&1

pushd "_releases\TiffTown.v%VERSION%-win-x64"
tar -a -c -f ..\TiffTown.v%VERSION%-win-x64.zip *
popd
if %ERRORLEVEL% neq 0 echo Warning: failed to create win-x64 zip

echo.
echo Built: _releases\TiffTown.v%VERSION%-win-x64.zip
goto :end

:error
echo Build failed with code %ERRORLEVEL%
pause
exit /b %ERRORLEVEL%

:end
