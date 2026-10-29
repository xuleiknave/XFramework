"""把官网文件压缩打包进一个 PowerShell 脚本，在 Windows 服务器上以管理员身份运行即可部署到 IIS。

用法：python3 website/make_deploy_script.py [输出路径]
默认输出到 website/dist/deploy-website.ps1。远程部署用 deploy_aliyun.py，它分段上传文件，不受单条命令大小限制。
"""
import base64
import gzip
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
FILES = ["index.html", "favicon.svg", "web.config", "design/index.html"]
STAGING_DIR = r"C:\inetpub\starline-staging"
SITE_DIR = r"C:\inetpub\starline"

TEMPLATE = r"""# 星线官网部署脚本（由 website/make_deploy_script.py 生成，不要手改）
# 用法：在服务器上以管理员身份打开 PowerShell，运行
#   powershell -ExecutionPolicy Bypass -File .\deploy-website.ps1
$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host '请用管理员身份运行 PowerShell 后再执行本脚本。' -ForegroundColor Red
    exit 1
}

Write-Host '1/4 安装 IIS（已安装会跳过）...'
if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
    Install-WindowsFeature Web-Server, Web-Static-Content, Web-Default-Doc, Web-Http-Errors, Web-Stat-Compression -IncludeManagementTools | Out-Null
} else {
    Enable-WindowsOptionalFeature -Online -All -NoRestart -FeatureName IIS-WebServerRole, IIS-StaticContent, IIS-DefaultDocument, IIS-HttpErrors, IIS-HttpCompressionStatic, IIS-ManagementConsole | Out-Null
}

Write-Host '2/4 写入网页文件到 __SITE_DIR__ ...'
$site = '__SITE_DIR__'
New-Item -ItemType Directory -Force -Path $site | Out-Null
$files = @{
__FILES__
}
# deploy_aliyun.py 会先把较大的文件分段上传到暂存目录（文件名里的 / 写成 __）
$staging = '__STAGING_DIR__'
if (Test-Path $staging) {
    foreach ($f in Get-ChildItem $staging -Filter *.b64) {
        $files[$f.BaseName.Replace('__', '/')] = [IO.File]::ReadAllText($f.FullName).Trim()
    }
}
foreach ($name in $files.Keys) {
    $path = Join-Path $site $name
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    $gz = New-Object IO.Compression.GZipStream((New-Object IO.MemoryStream(,[Convert]::FromBase64String($files[$name]))), [IO.Compression.CompressionMode]::Decompress)
    $out = [IO.File]::Create($path); $gz.CopyTo($out); $out.Close(); $gz.Close()
}
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }

Write-Host '3/4 把 IIS 默认站点指向官网目录...'
Import-Module WebAdministration
Set-ItemProperty 'IIS:\Sites\Default Web Site' -Name physicalPath -Value $site
if (-not (Get-WebBinding -Name 'Default Web Site' -Protocol http -Port 80)) {
    New-WebBinding -Name 'Default Web Site' -Protocol http -Port 80 -IPAddress '*'
}
Start-Service W3SVC
Start-WebSite -Name 'Default Web Site'

Write-Host '4/4 打开 Windows 防火墙 80 端口...'
if (-not (Get-NetFirewallRule -DisplayName 'Starline HTTP 80' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Starline HTTP 80' -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow | Out-Null
}

try {
    $r = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost/'
    if ($r.Content -match 'Starline') { Write-Host '完成：本机访问 http://localhost/ 已能看到星线官网。' -ForegroundColor Green }
    else { Write-Host '网站已启动，但首页内容不对，请把这段输出发给 Claude。' -ForegroundColor Yellow }
} catch {
    Write-Host ('本机访问失败：' + $_.Exception.Message) -ForegroundColor Yellow
}
Write-Host '别忘了在云服务商控制台的安全组里放行 TCP 80 端口。'
"""


def encode(name):
    return base64.b64encode(gzip.compress((HERE / name).read_bytes(), 9, mtime=0)).decode("ascii")


def build(inline=True):
    """inline=False 时脚本里不带文件内容，由调用方先把文件分段上传到暂存目录。"""
    entries = [f"    '{name}' = '{encode(name)}'" for name in FILES] if inline else []
    return (TEMPLATE.replace("__SITE_DIR__", SITE_DIR).replace("__STAGING_DIR__", STAGING_DIR)
            .replace("__FILES__", "\n".join(entries)))


def main():
    out = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else HERE / "dist" / "deploy-website.ps1"
    out.parent.mkdir(parents=True, exist_ok=True)
    # Windows PowerShell 5.1 要有 BOM 才按 UTF-8 读中文，换行用 CRLF
    out.write_bytes(b"\xef\xbb\xbf" + build().replace("\n", "\r\n").encode("utf-8"))
    print(out)


if __name__ == "__main__":
    main()
