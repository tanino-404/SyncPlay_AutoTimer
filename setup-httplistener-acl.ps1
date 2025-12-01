# HttpListener ポート ACL 設定スクリプト
# SetWindowsHookEx を実行しているユーザーがポート 8080 にバインドするための権限を設定
# 使用方法: powershell -ExecutionPolicy Bypass -File setup-httplistener-acl.ps1 -RunAsAdministrator

param(
    [int]$Port = 8080
)

# 管理者権限チェック
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($currentUser)
if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
    Write-Host "Please run: powershell -ExecutionPolicy Bypass -File setup-httplistener-acl.ps1 -Verb RunAs" -ForegroundColor Yellow
    exit 1
}

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  HttpListener ACL Setup" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

$url = "http://+:$Port/"
Write-Host "[Setup] Setting ACL for URL: $url" -ForegroundColor Yellow
Write-Host ""

# netsh コマンドで HTTP ポート ACL を追加
Write-Host "[Setup] Running: netsh http add urlacl url=$url user=Everyone" -ForegroundColor Gray

try {
    # 既存の設定を削除（エラーは無視）
    netsh http delete urlacl url="$url" 2>$null

    # 新しい設定を追加
    $result = netsh http add urlacl url="$url" user=Everyone

    if ($LASTEXITCODE -eq 0) {
        Write-Host "[Setup] ✅ ACL registration successful!" -ForegroundColor Green
        Write-Host "[Setup] Users can now bind to port $Port without administrator privileges." -ForegroundColor Green
    }
    else {
        Write-Host "[Setup] ❌ ACL registration failed (ExitCode: $LASTEXITCODE)" -ForegroundColor Red
        Write-Host "[Setup] Output: $result" -ForegroundColor Yellow
        exit 1
    }
}
catch {
    Write-Host "[Setup] ❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "[Setup] Setup complete!" -ForegroundColor Green
Write-Host "[Setup] You can now run SyncPlay_AutoTimer.exe without administrator privileges." -ForegroundColor Cyan
