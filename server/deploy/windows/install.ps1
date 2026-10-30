# Installs or updates the Starline game server on a Windows server (the Hong Kong test server,
# decision 71). Run in an administrator PowerShell from the unzipped package folder:
#
#   powershell -ExecutionPolicy Bypass -File .\install.ps1 -DevLogin
#
# What it does (each step is skipped when already done, so running it again updates the server):
#   1. Installs MongoDB Community as a Windows service listening on 127.0.0.1 only.
#   2. Copies the server to C:\Starline\server (stops the running one first).
#   3. Registers the scheduled task "StarlineServer" that starts it at boot as SYSTEM and restarts it
#      when it exits; output goes to C:\Starline\logs\server.log.
#   4. Opens the port in Windows Firewall and checks http://localhost:<port>/status.
# It does not touch IIS or the website. The port must also be opened in the Alibaba Cloud console
# (轻量应用服务器 > 防火墙). The WeChat AppSecret goes into a machine environment variable, never a file.
param(
    [int]$Port = 8899,
    [switch]$DevLogin,
    [string]$InstallDir = "C:\Starline\server",
    [string]$LogDir = "C:\Starline\logs",
    [string]$MongoVersion = "7.0.14",
    [string]$WechatAppId = "",
    [string]$WechatSecret = ""
)
$ErrorActionPreference = "Stop"
$TaskName = "StarlineServer"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Step($text) { Write-Host "== $text" -ForegroundColor Cyan }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "请用管理员身份运行 PowerShell"
}
if (-not (Test-Path (Join-Path $Here "starline-server.exe"))) {
    throw "在 $Here 里没找到 starline-server.exe：请在解压后的目录里运行"
}

Step "1/4 MongoDB"
if (Get-Service -Name "MongoDB" -ErrorAction SilentlyContinue) {
    Write-Host "已安装，跳过"
} else {
    $msi = Join-Path $env:TEMP "mongodb-$MongoVersion.msi"
    $url = "https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-$MongoVersion-signed.msi"
    Write-Host "下载 $url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $msi -UseBasicParsing
    # ServerService installs mongod as the "MongoDB" service; its default config binds 127.0.0.1 only.
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn ADDLOCAL=`"ServerService`" SHOULD_INSTALL_COMPASS=`"0`"" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "MongoDB 安装失败，msiexec 退出码 $($p.ExitCode)" }
}
Start-Service -Name "MongoDB"
Write-Host "MongoDB 服务：$((Get-Service MongoDB).Status)"

Step "2/4 复制服务端到 $InstallDir"
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName
}
Get-Process -Name "starline-server" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
New-Item -ItemType Directory -Force -Path $InstallDir, $LogDir | Out-Null
Copy-Item -Path (Join-Path $Here "*") -Destination $InstallDir -Recurse -Force

if ($WechatAppId -ne "") { [Environment]::SetEnvironmentVariable("STARLINE_WX_APPID", $WechatAppId, "Machine") }
if ($WechatSecret -ne "") { [Environment]::SetEnvironmentVariable("STARLINE_WX_SECRET", $WechatSecret, "Machine") }

Step "3/4 开机自启（计划任务 $TaskName）"
$serverArgs = "--port $Port --mongo mongodb://127.0.0.1:27017 --db-name starline"
if ($DevLogin) { $serverArgs += " --dev-login" }
$exe = Join-Path $InstallDir "starline-server.exe"
$log = Join-Path $LogDir "server.log"
$action = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c `"`"$exe`" $serverArgs >> `"$log`" 2>&1`"" -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Step "4/4 防火墙和自检"
$rule = "Starline server $Port"
if (-not (Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
}
$status = $null
for ($i = 0; $i -lt 20 -and $status -eq $null; $i++) {
    Start-Sleep -Seconds 1
    try { $status = Invoke-WebRequest -Uri "http://localhost:$Port/status" -UseBasicParsing -TimeoutSec 3 } catch { }
}
if ($status -eq $null) {
    Write-Host "服务端没有响应，最近的日志：" -ForegroundColor Red
    if (Test-Path $log) { Get-Content $log -Tail 30 }
    exit 1
}
Write-Host $status.Content
Write-Host ""
Write-Host "完成。还要在阿里云控制台（轻量应用服务器 > 防火墙）放行 TCP $Port，然后在浏览器打开 http://<服务器公网 IP>:$Port/status" -ForegroundColor Green
