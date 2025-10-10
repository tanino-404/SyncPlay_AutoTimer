# ファイアウォールルール設定スクリプト
# 目的: ポート8080のインバウンド接続を許可

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  ファイアウォールルール設定" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 管理者権限チェック
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "[ERROR] このスクリプトは管理者権限で実行する必要があります" -ForegroundColor Red
    Write-Host "[解決] PowerShellを右クリック → '管理者として実行'" -ForegroundColor Yellow
    Read-Host "Enterキーを押して終了"
    exit
}

Write-Host "[INFO] 管理者権限で実行中..." -ForegroundColor Green
Write-Host ""

# 既存のルールを確認
Write-Host "[1] 既存のファイアウォールルール確認..." -ForegroundColor Yellow
$existingRule = Get-NetFirewallRule -DisplayName "SyncPlay HTTP Server Port 8080" -ErrorAction SilentlyContinue

if ($existingRule) {
    Write-Host "  [存在] ルールは既に存在します" -ForegroundColor Yellow
    Write-Host "  削除して再作成しますか？ (Y/N)" -ForegroundColor Yellow
    $answer = Read-Host

    if ($answer -eq "Y" -or $answer -eq "y") {
        Remove-NetFirewallRule -DisplayName "SyncPlay HTTP Server Port 8080"
        Write-Host "  [削除] 既存ルールを削除しました" -ForegroundColor Green
    } else {
        Write-Host "  [スキップ] 既存ルールをそのまま使用します" -ForegroundColor Cyan
        Read-Host "Enterキーを押して終了"
        exit
    }
}

# 新しいルールを作成
Write-Host ""
Write-Host "[2] 新しいファイアウォールルール作成中..." -ForegroundColor Yellow

try {
    New-NetFirewallRule `
        -DisplayName "SyncPlay HTTP Server Port 8080" `
        -Description "SyncPlay AutoTimer v2.0 - MESH HTTP Server" `
        -Direction Inbound `
        -LocalPort 8080 `
        -Protocol TCP `
        -Action Allow `
        -Profile Domain,Private,Public `
        -Enabled True

    Write-Host "  [成功] ファイアウォールルールを作成しました" -ForegroundColor Green
    Write-Host ""
    Write-Host "  ルール名: SyncPlay HTTP Server Port 8080" -ForegroundColor White
    Write-Host "  ポート: 8080" -ForegroundColor White
    Write-Host "  プロトコル: TCP" -ForegroundColor White
    Write-Host "  方向: Inbound (受信)" -ForegroundColor White
    Write-Host "  アクション: Allow (許可)" -ForegroundColor White
    Write-Host "  プロファイル: Domain, Private, Public" -ForegroundColor White
}
catch {
    Write-Host "  [ERROR] ファイアウォールルール作成に失敗しました" -ForegroundColor Red
    Write-Host "  エラー: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  設定完了" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "[次のステップ]" -ForegroundColor Yellow
Write-Host "1. main.ps1を再起動してください" -ForegroundColor White
Write-Host "2. クライアントPCから以下のコマンドでテストしてください:" -ForegroundColor White
Write-Host "   Invoke-RestMethod -Uri 'http://<サーバーIP>:8080/api/status' -Method Get" -ForegroundColor Cyan
Write-Host ""

Read-Host "Enterキーを押して終了"
