# ====================================================================
# СКРИПТ РЕЗЕРВНОГО КОПИРОВАНИЯ MERMER ERP (ЛОКАЛЬНО + ФЛЕШКА)
# ====================================================================
param (
    [string]$DbPassword   = "1234",
    [string]$UsbDrive     = "E:",
    [string]$VdsHost      = "",
    [string]$VdsUser      = "root",
    [string]$VdsBackupDir = "/var/backups/mermer_clients"
)

# Автоопределение пути к pg_dump.exe
$PgDumpPath = (Get-Command pg_dump.exe -ErrorAction SilentlyContinue)?.Source
if (-not $PgDumpPath) {
    $PgDumpPath = Get-ChildItem "C:\Program Files\PostgreSQL" -Filter "pg_dump.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName -First 1
}
if (-not $PgDumpPath) {
    $PgDumpPath = "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe"
}

$DbName = "mermer_creation"
$DbUser = "postgres"
$DbPort = "5432"

$Timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm"
$FileName  = "dump_${DbName}_${Timestamp}.backup"
$TempDir   = "C:\Mermer_Server\TempBackups"

if (-not (Test-Path $TempDir)) { New-Item -ItemType Directory -Path $TempDir -Force | Out-Null }
$TempFile = Join-Path $TempDir $FileName

# 1. Снятие дампа базы данных через pg_dump
Write-Host "[1/3] Создание дампа PostgreSQL..." -ForegroundColor Cyan
$env:PGPASSWORD = $DbPassword

& $PgDumpPath -U $DbUser -h localhost -p $DbPort -F c -b -v -f $TempFile $DbName

if ($LASTEXITCODE -ne 0) {
    Write-Error "Ошибка снятия дампа базы данных!"
    exit 1
}

# 2. Сохранение на съемный накопитель
Write-Host "[2/3] Проверка накопителя ($UsbDrive)..." -ForegroundColor Cyan
if (Test-Path $UsbDrive) {
    $UsbTargetDir = Join-Path $UsbDrive "Mermer_Backups"
    if (-not (Test-Path $UsbTargetDir)) { New-Item -ItemType Directory -Path $UsbTargetDir -Force | Out-Null }
    Copy-Item -Path $TempFile -Destination "$UsbTargetDir\$FileName" -Force
    Write-Host "[УСПЕХ] Локальный бэкап сохранен: $UsbTargetDir\$FileName" -ForegroundColor Green
} else {
    Write-Host "[ИНФО] Внешний накопитель $UsbDrive не подключен. Копия сохранена в $TempDir" -ForegroundColor Yellow
}

# 3. Отправка на VDS (только если задан конкретный хост)
if (-not [string]::IsNullOrWhiteSpace($VdsHost) -and $VdsHost -ne "194.87.x.x") {
    Write-Host "[3/3] Проверка связи с сервером ($VdsHost)..." -ForegroundColor Cyan
    $CanReachVds = Test-Connection -ComputerName $VdsHost -Count 1 -Quiet -ErrorAction SilentlyContinue
    if ($CanReachVds) {
        & scp -o ConnectTimeout=5 -o StrictHostKeyChecking=no -batch $TempFile "${VdsUser}@${VdsHost}:${VdsBackupDir}/$FileName"
    }
}

# Очистка старых локальных копий (хранить последние 14 дней в TempDir)
Get-ChildItem -Path $TempDir -Filter "*.backup" | Where-Object { $_.CreationTime -lt (Get-Date).AddDays(-14) } | Remove-Item -Force