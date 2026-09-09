# Windows PowerShell 5.1 の配布版テスト誤判定修正

## 仕様
アプリが終了コード0かつAPP_SELF_TEST_PASSを出力した場合に成功とする。非0終了、成功マーカーなし、タイムアウトは引き続き失敗させる。失敗時には終了コードと標準出力・標準エラーを表示する。

## 手順
終了を待つ前にProcess.Handleを取得して保持する。時間制限付きWaitForExit後に引数なしWaitForExitを呼び、リダイレクトされたログの処理完了を待ってから判定する。共通スクリプトに切り出し、Windows PowerShell 5.1上で回帰テストする。

## タスク
- [x] 遅延終了プロセスで旧実装のExitCodeがnullになることを確認
- [x] 成功・非0終了・マーカーなし・タイムアウトの回帰テスト
- [x] 終了待ち修正とビルドスクリプトへの組み込み
- [x] 配布EXEで検証、実行結果レビュー、全体レビュー

## レビュー
旧実装の再現結果は `Done=True ExitCode= IsNull=True`、修正後は `Done=True ExitCode=0 IsNull=False`。Windows PowerShell 5.1で4ケースすべて成功し、非0終了やマーカー欠落を誤って通さないことを確認した。配布アプリの階段昇降・編集・保存テストも成功。アプリ本体・保存形式の変更はない。

回帰テスト: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/BuildSmoke.Tests.ps1`
