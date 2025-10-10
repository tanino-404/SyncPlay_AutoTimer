# Phase 1 テストガイド（最終版）

## 実装完了内容

Phase 1では以下の機能を実装しました：

### 1. HTTPサーバーモジュール (`http-server.ps1`)
- `System.Net.HttpListener`を使用した軽量HTTPサーバー
- メインループでポーリング処理（非同期タスク管理）
- エンドポイント:
  - `POST /api/toggle` - 再生/停止トグル
  - `POST /api/quit` - VLC/Syncplay終了
  - `POST /api/restart` - 全システム再起動
  - `GET /api/status` - VLC状態取得

### 2. VLC制御モジュール (`vlc-controller.ps1`)
- SendKeys方式でVLCを制御

### 3. main.ps1拡張
- HTTPサーバー起動ロジック（メインループ統合）
- キーボード入力テスト機能（P/Q/R/Sキー）

---

## 必須条件

✅ **PowerShellを管理者権限で実行**（外部PCからのHTTPアクセスのため）

---

## テスト手順

### テスト1: スクリプト起動確認

```powershell
cd "d:\Lemon\Documents\SyncPlay_AutoTimer\Script"
PowerShell -ExecutionPolicy Bypass -File "main.ps1"
```

**期待される動作**:
- HTTPサーバー起動ログ表示
- Syncplayサーバー起動ログ表示
- キーボード入力テストモードの説明表示

---

### テスト2: HTTPサーバー動作確認

別のPowerShellで:
```powershell
Invoke-RestMethod -Uri "http://localhost:8080/api/status" -Method Get
```

**期待される動作**:
- リクエスト受信ログ表示
- JSONレスポンス返却

---

### テスト3-7: 詳細は省略

VLC制御、クライアントPC通信など、すべて正常動作を確認してください。

---

## Phase 1 完了チェックリスト

- [ ] HTTPサーバー起動成功
- [ ] 同PC上でHTTPリクエスト成功
- [ ] キーボード入力でVLC制御成功
- [ ] クライアントPC通信成功

すべて完了したら**Phase 2（MESH連携）**へ進みます！
