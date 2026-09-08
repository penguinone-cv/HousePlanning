# 実装・検証手順

1. SPECIFICATION.mdの承認済仕様を確認。新しい高影響の不明点は実装前に確認する。
2. Godot 4.4.1 .NET / .NET SDK 9.0.310（対象ランタイム8.0.23）、Windows x64エクスポートテンプレート、PDFium Chromium 7999を固定し `.tools` に取得。依存物と配布ライセンスを記録する。`scripts/bootstrap.py`→`scripts/build.ps1 -Test -Export`を実行する。
3. `src/Core` はGodot非依存のC#。寸法・検証・履歴・ZIP保存をテストファーストで実装する。
4. `src/App` はGodotでUI、2D編集、3D生成、衝突、入出力を実装する。PDFiumはP/Invokeでページ描画のみ使用。
5. 各タスク：Red確認→最小実装→Green→整理→結果レビュー。受入条件を実装だけに似せたテストで代替しない。
6. 単体テスト→Godot headless統合テスト→実画面確認→Windows export→配布物起動の順に検証。
7. 性能測定は固定シーンと固定カメラ経路でCSV出力し、GPU/解像度/品質/測定時間を記録。GPU未確認なら性能達成としない。

仕様書→本手順書→TASKS.md→タスク実行→タスクごとのレビュー→全体レビューの順序を守る。レビュー結果はREVIEW.mdに記録する。
