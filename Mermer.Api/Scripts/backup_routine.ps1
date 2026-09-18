# ====================================================================
# СКРИПТ РЕЗЕРВНОГО КОПИРОВАНИЯ MERMER ERP (ЛОКАЛЬНО + ОБЛАКО VDS)
# ====================================================================
param (
    [string]$DbPassword = "YOUR_POSTGRES_PASSWORD",
    [string]$UsbDrive   = "E:",
    [string]$VdsHost    = "194.87.x.x", # IP-адрес VDS Леона
    [string]$VdsUser    = "root",
    [string]$VdsBackupDir = "/var/backups/mermer_clients"
)

$PgDumpPath = "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe"
$DbName     = "mermer_creation"
$DbUser     = "postgres"
$DbPort     = "5432"

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

# 2. Гарантированное сохранение на съемный диск (флешку)
Write-Host "[2/3] Проверка внешнего накопителя ($UsbDrive)..." -ForegroundColor Cyan
if (Test-Path $UsbDrive) {
    $UsbTargetDir = Join-Path $UsbDrive "Mermer_Backups"
    if (-not (Test-Path $UsbTargetDir)) { New-Item -ItemType Directory -Path $UsbTargetDir -Force | Out-Null }
    Copy-Item -Path $TempFile -Destination "$UsbTargetDir\$FileName" -Force
    Write-Host "[УСПЕХ] Локальный бэкап сохранен на флешку: $UsbTargetDir\$FileName" -ForegroundColor Green
} else {
    Write-Warning "[ВНИМАНИЕ] Флешка $UsbDrive не найдена. Бэкап сохранен только локально на диске C:"
}

# 3. Проверка связи с VDS и отправка по сети
Write-Host "[3/3] Проверка связи с VDS сервером ($VdsHost)..." -ForegroundColor Cyan
$CanReachVds = Test-Connection -ComputerName $VdsHost -Count 1 -Quiet -ErrorAction SilentlyContinue

if ($CanReachVds) {
    Write-Host "Сеть доступна. Отправка архива на VDS..." -ForegroundColor Yellow
    # Отправка через scp (стандартная утилита Windows 10/11)
    & scp -o ConnectTimeout=5 -o StrictHostKeyChecking=no $TempFile "${VdsUser}@${VdsHost}:${VdsBackupDir}/$FileName"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[УСПЕХ] Бэкап успешно скопирован на VDS!" -ForegroundColor Green
    } else {
        Write-Warning "Не удалось передать файл по scp. Копия осталась на внешнем носителе."
    }
} else {
    Write-Host "Интернета нет или VDS недоступен. Пропуск сетевой отправки." -ForegroundColor Gray
}

# Очистка временного файла на диске C (если есть копия на флешке)
if (Test-Path "$UsbDrive\Mermer_Backups\$FileName") {
    Remove-Item $TempFile -Force -ErrorAction SilentlyContinue
}