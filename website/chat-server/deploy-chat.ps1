# 星线留言板部署脚本：在服务器上从 GitHub 拉取指定提交的源码，编译后挂到 IIS 的 /api 应用下。
# 由 deploy_chat.py 填好提交号和文件列表后通过云助手执行；也可以远程桌面上以管理员身份手动运行。
# 第一次运行会安装 ASP.NET Core Hosting Bundle 和 .NET 8 SDK（各需几分钟）。
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Repo = 'xuleiknave/XFramework'
$Ref = '__REF__'
$Files = @(
__FILES__
)
$Site = 'C:\inetpub\starline'
$AppDir = 'C:\starline-apps\chat-api'
$DataDir = 'C:\starline-data\chat'
$Tools = 'C:\starline-tools'
$Src = Join-Path $Tools "chat-src"
$Pool = 'StarlineChat'

Write-Host "1/6 检查 ASP.NET Core Hosting Bundle..."
Import-Module WebAdministration
if (-not (Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue)) {
    New-Item -ItemType Directory -Force $Tools | Out-Null
    $exe = Join-Path $Tools 'dotnet-hosting-8-win.exe'
    Invoke-WebRequest -UseBasicParsing 'https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe' -OutFile $exe
    $p = Start-Process $exe -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { throw "Hosting Bundle 安装失败，退出码 $($p.ExitCode)" }
    & net stop was /y | Out-Null
    & net start w3svc | Out-Null
    Write-Host '   已安装'
} else { Write-Host '   已存在' }

Write-Host "2/6 检查 .NET 8 SDK..."
$dotnet = Join-Path $Tools 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $ps1 = Join-Path $Tools 'dotnet-install.ps1'
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $ps1
    & $ps1 -Channel 8.0 -InstallDir (Join-Path $Tools 'dotnet') -NoPath | Out-Null
    Write-Host '   已安装'
} else { Write-Host '   已存在' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host "3/6 从 GitHub 下载 $Ref 的源码..."
if (Test-Path $Src) { Remove-Item -Recurse -Force $Src }
foreach ($f in $Files) {
    $dst = Join-Path $Src $f
    New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
    Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/$Repo/$Ref/$f" -OutFile $dst
}

Write-Host "4/6 编译..."
$out = "$AppDir-new"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
& $dotnet publish (Join-Path $Src 'website\chat-server\Starline.Chat.csproj') -c Release -o $out --nologo -v q
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Copy-Item -Force (Join-Path $Src 'website\chat-server\web.config') (Join-Path $out 'web.config')

Write-Host "5/6 更新 IIS..."
New-Item -ItemType Directory -Force "$DataDir\logs" | Out-Null
if (-not (Test-Path "IIS:\AppPools\$Pool")) {
    New-WebAppPool -Name $Pool | Out-Null
    Set-ItemProperty "IIS:\AppPools\$Pool" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$Pool" -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty "IIS:\AppPools\$Pool" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
}
& icacls $DataDir /grant "IIS AppPool\${Pool}:(OI)(CI)M" /T /Q | Out-Null
if ((Get-WebAppPoolState -Name $Pool).Value -eq 'Started') { Stop-WebAppPool -Name $Pool }
for ($i = 0; $i -lt 20 -and (Get-WebAppPoolState -Name $Pool).Value -ne 'Stopped'; $i++) { Start-Sleep 1 }
Start-Sleep 2
if (Test-Path $AppDir) { Remove-Item -Recurse -Force $AppDir }
Move-Item $out $AppDir
if (-not (Get-WebApplication -Site 'Default Web Site' -Name 'api')) {
    New-WebApplication -Site 'Default Web Site' -Name 'api' -PhysicalPath $AppDir -ApplicationPool $Pool | Out-Null
}
Start-WebAppPool -Name $Pool

New-Item -ItemType Directory -Force "$Site\chat" | Out-Null
Copy-Item -Force (Join-Path $Src 'website\chat\*.html') "$Site\chat\"

Write-Host "6/6 自检..."
Start-Sleep 3
foreach ($u in 'http://localhost/api/health', 'http://localhost/chat/', 'https://xulei.ai/api/health', 'https://xulei.ai/chat/') {
    try {
        $r = Invoke-WebRequest -UseBasicParsing $u -TimeoutSec 20
        $body = $r.Content; if ($body.Length -gt 80) { $body = $body.Substring(0, 80) }
        Write-Host "   $u -> $($r.StatusCode) $body"
    } catch { Write-Host "   $u -> 失败：$($_.Exception.Message)" }
}
$pwdFile = Join-Path $DataDir 'admin-password.txt'
if (Test-Path $pwdFile) {
    Write-Host "首次管理密码（登录后台改掉后这个文件会自动删除）：$(Get-Content $pwdFile)"
}
Write-Host '完成'
