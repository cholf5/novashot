#!/usr/bin/env bash
# R-1: Prtsc Windows 打包（Velopack/vpk）。
#
# 流程：dotnet publish (win-x64, self-contained) → vpk pack（安装器 Setup.exe + 便携包 + nupkg feed）。
# 裁决：self-contained——截图工具不给终端用户添 .NET 运行时依赖，体积代价可接受（~150MB）。
# 交叉打包：vpk 支持在 macOS/Linux 上打 Windows 产物（CI windows-latest 上同样可跑本脚本）。
# 更新通道：vpk feed 文件（releases.win.json / RELEASES）与 Prtsc-win-Setup.exe / Prtsc-win-portable.zip
#           落在输出目录，C-1 Updater 用 UpdateManager(feedUrl) 直连该目录的 http 托管（如 GitHub Releases）。
#
# 用法：scripts/pack-windows.sh [--version 0.1.0]   （缺省取 Directory.Build.props 的 <Version>）
# 产物：artifacts/pack/（gitignore，不入库）
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TFM="net10.0-windows10.0.19041.0"
RID="win-x64"
PACK_ID="Prtsc"
PACK_TITLE="Prtsc"
PACK_AUTHORS="cholf5"
CHANNEL="win"   # vpk 在 macOS 上 channel 默认 osx，必须显式钉成 win（决定 releases.{channel}.json 文件名）
PACK_DIR="$ROOT/artifacts/pack/publish"
OUT_DIR="$ROOT/artifacts/pack"
ICON="$ROOT/assets/app.ico"

# —— 版本：--version 参数优先，否则从 Directory.Build.props 读 <Version> ——
VERSION=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done
if [[ -z "$VERSION" ]]; then
  VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$ROOT/Directory.Build.props" | head -1 | tr -d '[:space:]')"
fi
if [[ -z "$VERSION" ]]; then
  echo "ERROR: no version (pass --version or check Directory.Build.props)" >&2
  exit 2
fi
echo "==> pack $PACK_ID $VERSION ($RID, self-contained)"

# —— vpk 定位：PATH 优先，退回 dotnet global tools 默认位置（Windows 宿主上是 vpk.exe，review M5）——
VPK="$(command -v vpk || command -v vpk.exe || true)"
if [[ -z "$VPK" && -x "$HOME/.dotnet/tools/vpk" ]]; then
  VPK="$HOME/.dotnet/tools/vpk"
elif [[ -z "$VPK" && -x "$HOME/.dotnet/tools/vpk.exe" ]]; then
  VPK="$HOME/.dotnet/tools/vpk.exe"
fi
if [[ -z "$VPK" ]]; then
  echo "ERROR: vpk not found. Install with: dotnet tool install --global vpk" >&2
  exit 2
fi

# —— publish（self-contained；EnableWindowsTargeting 已在 Directory.Build.props 打开，
#     mac/linux 上交叉 publish windows TFM 无需 Windows 机器）——
# 先清空 publish 目录：上次残留/已从项目移除的文件不清理会被 vpk 整目录打进包（review M6）
rm -rf "$PACK_DIR"
mkdir -p "$PACK_DIR"
dotnet publish "$ROOT/src/Prtsc.App" \
  -c Release -f "$TFM" -r "$RID" --self-contained true \
  -o "$PACK_DIR"

if [[ ! -f "$PACK_DIR/Prtsc.exe" ]]; then
  echo "ERROR: publish output missing Prtsc.exe at $PACK_DIR" >&2
  exit 2
fi

# —— vpk pack：安装器 + 便携包 + nupkg feed ——
# 关键：[win] 是 vpk 的目标平台 directive（System.CommandLine directive 语法，方括号字面量），
# 决定用 Windows 打包器（.ico 图标 / Setup.exe / releases.win.json）。不带它时按宿主 OS 选择——
# 在 macOS/Linux 上会误用 osx/linux 打包器。Windows 宿主上默认就是 win，带 directive 亦合法。
ICON_ARGS=()
if [[ -f "$ICON" ]]; then
  ICON_ARGS=(-i "$ICON")
else
  echo "WARN: $ICON missing, vpk will use its default icon (regenerate: python3 scripts/make-app-icon.py)" >&2
fi

"$VPK" '[win]' pack \
  -u "$PACK_ID" \
  -v "$VERSION" \
  -p "$PACK_DIR" \
  -e "Prtsc.exe" \
  --packTitle "$PACK_TITLE" \
  --packAuthors "$PACK_AUTHORS" \
  -c "$CHANNEL" \
  -r "$RID" \
  -o "$OUT_DIR" \
  ${ICON_ARGS[@]+"${ICON_ARGS[@]}"}  # 空数组 + set -u 在 macOS 自带 bash 3.2 会 unbound variable（review M4）

echo "==> artifacts:"
find "$OUT_DIR" -maxdepth 2 -type f \( -name "*.exe" -o -name "*.nupkg" -o -name "*.zip" -o -name "releases*.json" -o -name "RELEASES" \) -print
echo "==> done. (sha256 清单见 CI release job；本机手动: shasum -a 256 $OUT_DIR/*.exe)"
