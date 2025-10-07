# Phase 1 テストガイド

## 実装内容

Phase 1では以下の機能を実装しました：

### 1. HTTPサーバーモジュール (`http-server.ps1`)
- System.Net.HttpListenerを使用した軽量HTTPサーバー
- エンドポイント:
  - `POST /api/toggle` - 再生/停止トグル
  - `POST /api/quit` - VLC/Syncplay終了
  - `POST /api/restart` - 全システム再起動
  - `GET /api/status` - VLC状態取得

### 2. VLC制御モジュール (`vlc-controller.ps1`)
- SendKeys方式でVLCを制御
- 関数:
  - `VLC-Send-Play` - 再生/一時停止トグル
  - `VLC-Send-Stop` - 停止
  - `VLC-Send-Quit` - 終了
  - `VLC-Get-Status` - 状態取得

### 3. main.ps1拡張
- モジュールインポート機能
- HTTPサーバー起動ロジック
- キーボード入力テスト機能（P/Q/R/Sキー）
- グローバル状態管理

---

## テスト手順

### テスト1: スクリプト起動確認

1. PowerShellを管理者権限で起動
2. 以下のコマンドを実行:
   ```powershell
   cd "d:\Lemon\Documents\SyncPlay_AutoTimer\Script"
   PowerShell -ExecutionPolicy Bypass -File "main.ps1"
   ```

**期待される動作**:
- ✅ HTTPサーバーが起動（Port: 8080）
- ✅ Syncplayサーバーが起動（ServerMode=trueの場合）
- ✅ キーボード入力テストモードの説明が表示

**確認ポイント**:
```
[HH:mm:ss] [SUCCESS] HTTPサーバーが起動しました (Port: 8080)
[HH:mm:ss] [SUCCESS] Syncplay Serverが正常に起動しました (PID: XXXX)

【キーボード入力テストモード】
  P: 再生/停止トグル
  Q: 終了
  R: 再起動
  S: 状態確認
```

---

### テスト2: キーボード入力テスト（VLC未起動）

1. スクリプト起動後、`S`キーを押す

**期待される動作**:
```
[HH:mm:ss] [DEBUG] キーボード入力: S (Status) → VLC状態: stopped
```

---

### テスト3: 手動でVLCを起動してキーボード入力テスト

1. 別ウィンドウでVLCを手動起動し、動画ファイルを開く
2. スクリプトウィンドウに戻り、`P`キーを押す

**期待される動作**:
```
======================================================
[HH:mm:ss] [INFO] Toggle コマンドを受信しました
======================================================
[HH:mm:ss] [INFO] VLC再生/一時停止コマンドを送信中...
[HH:mm:ss] [SUCCESS] VLCにキー送信:
======================================================
```

- ✅ VLCが再生開始（または一時停止）

3. 再度`P`キーを押す

**期待される動作**:
- ✅ VLCが一時停止（または再生再開）

---

### テスト4: VLC終了コマンド

1. VLC起動中に`Q`キーを押す

**期待される動作**:
```
======================================================
[HH:mm:ss] [INFO] Quit コマンドを受信しました
======================================================
[HH:mm:ss] [INFO] VLCを終了しています...
[HH:mm:ss] [SUCCESS] VLCを終了しました
======================================================
```

- ✅ VLCが強制終了
- ✅ Syncplayクライアントも終了（起動していた場合）

---

### テスト5: HTTPサーバー動作確認（外部から）

1. 別のPowerShellウィンドウを開く
2. 以下のコマンドでHTTPリクエストを送信:

```powershell
# ステータス取得
Invoke-RestMethod -Uri "http://localhost:8080/api/status" -Method Get

# 再生/停止トグル
Invoke-RestMethod -Uri "http://localhost:8080/api/toggle" -Method Post

# 終了
Invoke-RestMethod -Uri "http://localhost:8080/api/quit" -Method Post
```

**期待される動作**:
- ✅ JSONレスポンスが返却される
- ✅ スクリプトウィンドウにコマンド受信ログが表示
- ✅ VLCが制御される

**レスポンス例**:
```json
{
  "status": "success",
  "action": "toggle",
  "message": "再生/停止コマンドを受信しました"
}
```

---

### テスト6: 再起動コマンド

1. `R`キーを押す

**期待される動作**:
```
======================================================
[HH:mm:ss] [INFO] Restart コマンドを受信しました
======================================================
[HH:mm:ss] [INFO] プロセスを停止しています...
[HH:mm:ss] [SUCCESS] Syncplayを停止しました
[HH:mm:ss] [SUCCESS] VLCを停止しました
[HH:mm:ss] [SUCCESS] Syncplay Serverを停止しました (ServerModeの場合)
[HH:mm:ss] [INFO] Syncplay Serverを起動しています... (ServerModeの場合)
[HH:mm:ss] [SUCCESS] Syncplay Serverが正常に起動しました
======================================================
```

---

## トラブルシューティング

### エラー1: HTTPサーバー起動失敗

**症状**:
```
[HH:mm:ss] [ERROR] HTTPサーバーの起動に失敗しました: アクセスが拒否されました
```

**原因**: ポート8080が既に使用中

**解決策**:
1. 既存のプロセスを確認:
   ```powershell
   netstat -ano | findstr :8080
   ```
2. 該当プロセスを終了するか、`main.ps1`の`$HttpServerPort`を変更

---

### エラー2: VLCキー送信失敗

**症状**:
```
[HH:mm:ss] [WARNING] VLCプロセスが見つかりません
```

**原因**: VLCが起動していない

**解決策**:
- VLCを手動起動してから再度テスト
- または定刻起動機能でSyncplayと一緒にVLCを起動

---

### エラー3: モジュール読み込みエラー

**症状**:
```
用語 'VLC-Send-Play' は、コマンドレット、関数、スクリプト ファイル、または操作可能なプログラムの名前として認識されません。
```

**原因**: モジュールファイルが見つからない

**解決策**:
- `http-server.ps1`と`vlc-controller.ps1`が`Script`フォルダ内にあることを確認
- ファイルパスにスペースが含まれていないか確認

---

## Phase 2への準備

Phase 1が正常に動作したら、Phase 2（クライアント配信機能）へ進みます。

### Phase 2で実装する内容:
- サーバーPCからクライアントPCへのHTTPコマンド配信
- クライアントPC側の受信処理
- エラーハンドリング強化

### Phase 2への移行チェックリスト:
- [ ] テスト1-6がすべて成功
- [ ] HTTPサーバーが安定動作
- [ ] VLC制御が確実に動作
- [ ] キーボード入力で全コマンド実行可能
