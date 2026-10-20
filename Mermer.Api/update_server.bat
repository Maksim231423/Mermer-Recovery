@echo off
chcp 65001 > nul
cd /d "%~dp0"

echo ===================================================
echo   Proverka i ustanovka obnovleniya servera...
echo ===================================================

:: 1. Завершаем работу запущенного сервера, если он открыт
taskkill /f /im Mermer.Api.exe >nul 2>&1

:: 2. Скачиваем свежий архив сервера из GitHub Releases (или вашего источника)
echo Zagruzka svezhego servera...
curl -L -o server_update.zip "https://github.com/Maksim231423/Mermer-Recovery/releases/latest/download/server_update.zip"

if %ERRORLEVEL% NEQ 0 (
    echo [OSHIBKA] Ne udalos skachat obnovlenie servera.
    pause
    exit /b 1
)

:: 3. Распаковываем файлы с заменой (без затирания appsettings.json, если исключить его из архива)
echo Ustanovka obnovleniya...
tar -xf server_update.zip -C "%~dp0"
del /f /q server_update.zip

echo.
echo Obnovlenie servera uspeshno ustanovleno!
echo Zapusk...
call RUN_SERVER.bat