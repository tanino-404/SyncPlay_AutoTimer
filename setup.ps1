# SyncPlay_AutoTimer v2.0 - 統合初期セットアップスクリプト
# このスクリプトはアプリケーション初回実行前に必要な設定をすべて自動化します
# 使用方法: powershell -ExecutionPolicy Bypass -File setup.ps1

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  SyncPlay_AutoTimer v2.0 Setup" -ForegroundColor Cyan
Write-Host "  Integrated Initial Configuration" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# 管理者権限チェック
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($currentUser)
$isAdmin = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "⚠️  WARNING: This script should be run with administrator privileges." -ForegroundColor Yellow
    Write-Host "Some features (HTTP ACL, Firewall) may be skipped." -ForegroundColor Yellow
    Write-Host ""
}

# ========================================
# Step 1: config.ini 確認
# ========================================
Write-Host "[Step 1] Checking config.ini..." -ForegroundColor Cyan
$documentsFolder = [Environment]::GetFolderPath([Environment]::SpecialFolder::MyDocuments)
$configDir = "$documentsFolder\SyncPlay_AutoTimer\Setting"
$configFile = "$configDir\config.ini"

if (Test-Path $configFile) {
    Write-Host "✅ Config file exists: $configFile" -ForegroundColor Green
    Write-Host "   You can customize settings by editing this file." -ForegroundColor Green
}
else {
    Write-Host "ℹ️  Config file will be auto-created on first run at:" -ForegroundColor Cyan
    Write-Host "   $configFile" -ForegroundColor Gray
}
Write-Host ""

# ========================================
# Step 2: HTTP ACL 設定（管理者権限が必要）
# ========================================
if ($isAdmin) {
    Write-Host "[Step 2] Setting up HTTP ACL for port 8080..." -ForegroundColor Cyan

    $port = 8080
    $url = "http://+:$port/"

    try {
        # 既存の設定を削除（エラーは無視）
        netsh http delete urlacl url="$url" 2>$null | Out-Null

        # 新しい設定を追加
        $result = netsh http add urlacl url="$url" user=Everyone

        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ HTTP ACL registration successful!" -ForegroundColor Green
            Write-Host "   Users can now bind to port $port without admin privileges." -ForegroundColor Green
        }
        else {
            Write-Host "⚠️  HTTP ACL registration may have failed." -ForegroundColor Yellow
            Write-Host "   You can run: setup-httplistener-acl.ps1 later if needed." -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "⚠️  Error setting up HTTP ACL: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
else {
    Write-Host "[Step 2] Skipping HTTP ACL setup (requires admin privileges)" -ForegroundColor Yellow
    Write-Host "   To set up HTTP ACL later, run: setup-httplistener-acl.ps1 as Administrator" -ForegroundColor Gray
}
Write-Host ""

# ========================================
# Step 3: ファイアウォール設定（推奨）
# ========================================
if ($isAdmin) {
    Write-Host "[Step 3] Checking Windows Firewall..." -ForegroundColor Cyan

    try {
        $firewallRule = Get-NetFirewallRule -DisplayName "SyncPlay_AutoTimer" -ErrorAction SilentlyContinue

        if ($firewallRule) {
            Write-Host "✅ Firewall rule already exists for SyncPlay_AutoTimer" -ForegroundColor Green
        }
        else {
            Write-Host "ℹ️  Adding firewall rule for port 8080..." -ForegroundColor Cyan

            # Inbound rule
            New-NetFirewallRule -DisplayName "SyncPlay_AutoTimer (HTTP Inbound)" `
                -Direction Inbound `
                -LocalPort 8080 `
                -Protocol TCP `
                -Action Allow `
                -ErrorAction SilentlyContinue | Out-Null

            # Outbound rule
            New-NetFirewallRule -DisplayName "SyncPlay_AutoTimer (HTTP Outbound)" `
                -Direction Outbound `
                -LocalPort 8080 `
                -Protocol TCP `
                -Action Allow `
                -ErrorAction SilentlyContinue | Out-Null

            Write-Host "✅ Firewall rules added for port 8080" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "⚠️  Error setting firewall rules: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
else {
    Write-Host "[Step 3] Skipping Firewall setup (requires admin privileges)" -ForegroundColor Yellow
    Write-Host "   If you need to open port 8080, run this script as Administrator." -ForegroundColor Gray
}
Write-Host ""

# ========================================
# Step 4: 必須外部アプリケーションチェック
# ========================================
Write-Host "[Step 4] Checking required applications..." -ForegroundColor Cyan

$appChecks = @(
    @{Name = "MPV"; Path = "C:\Program Files\mpv\mpv.exe"},
    @{Name = "Syncplay"; Path = "C:\Program Files (x86)\Syncplay\syncplayServer.exe"}
)

$allAppsFound = $true

foreach ($app in $appChecks) {
    if (Test-Path $app.Path) {
        Write-Host "✅ $($app.Name) found: $($app.Path)" -ForegroundColor Green
    }
    else {
        Write-Host "⚠️  $($app.Name) not found: $($app.Path)" -ForegroundColor Yellow
        Write-Host "   Please install $($app.Name) before running the application." -ForegroundColor Gray
        $allAppsFound = $false
    }
}
Write-Host ""

# ========================================
# Summary
# ========================================
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  Setup Summary" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "✅ Setup completed!" -ForegroundColor Green
Write-Host ""

Write-Host "📁 Config folder:" -ForegroundColor Gray
Write-Host "   $configDir" -ForegroundColor Gray
Write-Host ""

Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Ensure MPV and Syncplay are installed" -ForegroundColor Gray
Write-Host "  2. Edit config.ini to customize settings" -ForegroundColor Gray
Write-Host "  3. Run: $documentsFolder\SyncPlay_AutoTimer\bin\Release\net9.0\SyncPlay_AutoTimer.exe" -ForegroundColor Gray
Write-Host ""

Write-Host "Documentation:" -ForegroundColor Cyan
Write-Host "  MPV: https://mpv.io" -ForegroundColor Gray
Write-Host "  Syncplay: https://syncplay.pl" -ForegroundColor Gray
Write-Host ""
