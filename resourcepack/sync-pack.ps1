# リソースパックをクライアントへ同期する。
#
# Minecraft はシンボリックリンクのパックを既定で拒否するため、実体をコピーする。
# git pull のあとにこれを実行すれば、パックが最新になる。
#
#   powershell -ExecutionPolicy Bypass -File E:\raid-dev\resourcepack\sync-pack.ps1
#
# 置き場所が違う環境では $packs を書き換える。
#
# 注意: このファイルは UTF-8 (BOM 付き) で保存すること。BOM が無いと Windows
# PowerShell 5.1 が ANSI として読み、日本語のコメントが化けて構文が壊れる。

$packs = "C:\Users\junem\AppData\Roaming\.minecraft\mods\MultiMC\instances\1.21.4\.minecraft\resourcepacks"
$dest = Join-Path $packs "raid-dev"

if (-not (Test-Path $packs)) {
    Write-Error "resourcepacks フォルダが見つかりません: $packs"
    exit 1
}

# 既存がリンクなら、リンクだけを消してから置き直す。
# Remove-Item -Recurse はリンク先の中身まで消すことがあるため使わない。
if (Test-Path $dest) {
    $item = Get-Item $dest -Force
    if ($item.LinkType) {
        Write-Host "リンクを実体に置き換えます"
        [System.IO.Directory]::Delete($dest, $false)
    }
}

# /MIR で余分なファイルも消し、完全な写しにする
robocopy $PSScriptRoot $dest /MIR /NFL /NDL /NJH /NJS /XF sync-pack.ps1 | Out-Null

if (Test-Path (Join-Path $dest "pack.mcmeta")) {
    Write-Host "同期しました: $dest"
    Write-Host "ゲーム内のリソースパック画面で「選択済み」へ移してください (F3+T で再読み込み)"
} else {
    Write-Error "コピーに失敗しました"
    exit 1
}
