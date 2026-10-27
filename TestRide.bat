@echo off
rem Quick test launcher: builds the C# code, then starts VeloGenesis with the keyboard simulator.
setlocal
cd /d "%~dp0"
set "GODOT=%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"

echo Building...
dotnet build VeloGenesis.csproj -nologo -v q
if errorlevel 1 (
    echo Build failed - see errors above.
    pause
    exit /b 1
)

echo.
echo   1  Free ride        (simulator: W/S = power, Up/Down = gears)
echo   2  ERG workout      (simulator: W/S = cadence, Up/Down = intensity)
echo   3  Free ride, starting just before the first fork
echo   4  Normal start     (main menu, uses your paired trainer)
echo.
echo   In test rides: F6 / F7 = slower / faster time (1x 2x 4x 8x), F12 = screenshot
echo.
choice /c 1234 /n /m "Choose 1-4: "
if errorlevel 4 (set "ARGS=" & goto run)
if errorlevel 3 (set "ARGS=--test --autoride=free --no-ble --skip=600" & goto run)
if errorlevel 2 (set "ARGS=--test --autoride=erg --no-ble" & goto run)
set "ARGS=--test --autoride=free --no-ble"

:run
"%GODOT%" --path . -- %ARGS%
