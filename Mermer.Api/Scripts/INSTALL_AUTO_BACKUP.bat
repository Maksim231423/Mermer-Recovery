@echo off
title Ustanovka avto-bekapa Mermer ERP
chcp 65001 >nul

:: Proverka prav Administratora
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [OSHIBKA] Zapustite etot fayl ot imeni Administratora!
    pause
    exit /b 1
)

set SCRIPT_PATH=%~dp0backup_routine.ps1

echo Registraciya zadachi v planirovshike Windows...
schtasks /create /tn "Mermer_AutoBackup" /tr "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File \"%SCRIPT_PATH%\"" /sc hourly /mo 12 /ru "SYSTEM" /rl HIGHEST /f

if %errorLevel% equ 0 (
    echo.
    echo [USPEH] Avtomaticheskiy bekap uspeshno nastroyen!
    echo Zadacha budet rabotat' v fone kazhdyye 12 chasov bez uchastiya pol'zovatelya.
) else (
    echo.
    echo [OSHIBKA] Ne udalos' sozdat' zadachu.
)

pause