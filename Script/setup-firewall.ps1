# ファイアウォールルール設定スクリプト
# SyncPlay AutoTimer v2.0 - ポート8080のインバウンド接続許可

# 管理者権限チェック（setup.batから呼ばれるため通常は不要だが念のため）
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "   [エラー] 管理者権限が必要です" -ForegroundColor Red
    exit 1
}

# 既存のルールを確認・削除
$existingRule = Get-NetFirewallRule -DisplayName "SyncPlay HTTP Server Port 8080" -ErrorAction SilentlyContinue

if ($existingRule) {
    Write-Host "   既存のルールを削除しています..." -ForegroundColor Gray
    Remove-NetFirewallRule -DisplayName "SyncPlay HTTP Server Port 8080" -ErrorAction SilentlyContinue | Out-Null
}

# 新しいルールを作成
try {
    New-NetFirewallRule `
        -DisplayName "SyncPlay HTTP Server Port 8080" `
        -Description "SyncPlay AutoTimer v2.0 - HTTP Server for synchronized video control" `
        -Direction Inbound `
        -LocalPort 8080 `
        -Protocol TCP `
        -Action Allow `
        -Profile Domain,Private,Public `
        -Enabled True `
        -ErrorAction Stop | Out-Null

    Write-Host "   ファイアウォールルールを作成しました" -ForegroundColor Gray
    Write-Host "   - ポート: 8080 (TCP)" -ForegroundColor Gray
    Write-Host "   - 方向: 受信許可" -ForegroundColor Gray
    Write-Host "   - プロファイル: すべて" -ForegroundColor Gray
    exit 0
}
catch {
    Write-Host "   [エラー] ファイアウォールルール作成に失敗しました" -ForegroundColor Red
    Write-Host "   詳細: $_" -ForegroundColor Red
    exit 1
}
