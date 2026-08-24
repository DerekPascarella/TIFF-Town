@echo off
setlocal

pushd "%~dp0" || exit /b 1

for %%D in (
    "src\TiffTown.Core\bin"
    "src\TiffTown.Core\obj"
    "src\TiffTown.App\bin"
    "src\TiffTown.App\obj"
    "_tests\TiffTown.Core.Tests\bin"
    "_tests\TiffTown.Core.Tests\obj"
) do (
    if exist "%%~D" rd /s /q "%%~D"
    if exist "%%~D" (
        echo ERROR: Failed to remove %%~D
        popd
        exit /b 1
    )
)

popd
exit /b 0
