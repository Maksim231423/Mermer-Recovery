@echo off
chcp 65001 > nul
cd /d "%~dp0"

echo ===================================================
echo    Proverka i ustanovka obnovleniya servera...
echo ===================================================

:: 1. Zavershaem rabotu zapushchennogo servera, esli on otkryt
echo [1/3] Ostanovka rabotayushchego servera...
taskkill /f /im Mermer.Api.exe >nul 2>&1

:: 2. Poluchaem svezhiy kod i client_update.zip cherez Git
echo [2/3] Zagruzka obnovleniy iz Git...
git fetch origin migration/net8-upgrade
git reset --hard origin/migration/net8-upgrade

if %ERRORLEVEL% NEQ 0 (
    echo [OSHIBKA] Ne udalos poluchit obnovlenie iz Git.
    pause
    exit /b %ERRORLEVEL%
)

:: 3. Publikuem svezhuyu versiyu API v tekushchuyu papku
echo [3/3] Sborka i obnovlenie servera...
dotnet publish Mermer.Api\Mermer.Api.csproj -c Release -o .

if %ERRORLEVEL% NEQ 0 (
    echo [OSHIBKA] Ne udalos sobrat API!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo    Obnovlenie uspeshno ustanovleno!
echo    Zapusk servera...
echo ===================================================
call RUN_SERVER.bat