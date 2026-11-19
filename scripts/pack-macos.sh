#!/usr/bin/env bash
# E-14: Prtsc macOS 打包 —— self-contained publish → .app bundle → ad-hoc 封签 → zip。
#
# 用法：scripts/pack-macos.sh [--version 1.0.0] [--rids "osx-arm64 osx-x64"]
#   --rids 缺省 osx-arm64（本机快速试跑）；CI 的 pack-macos 腿传两个。
# 产物：artifacts/pack/Prtsc-<版本>-osx-<arch>.zip（gitignore，不入库）
#
# 为什么不用 `vpk [osx] pack`：vpk 的 osx 通道产出的是 .pkg 安装器（或带 UpdateMac 助手的
# bundle），要么需要 Apple 开发者证书 + 公证（我们没有），要么封签在加壳之后失效——没有证书时
# 下载件会被 Gatekeeper 直接判「已损坏」，连「仍要尝试打开」这条逃生口都没有。自己组 .app +
# ad-hoc 封签，拿到的是标准的「未验证开发者」提示，用户还能放行；代价是没有安装器，解压即用。
#
# 更新语义：这个 zip 不参与应用内更新（Velopack 的更新检查要求 UpdateManager.IsInstalled，
# 只有 Windows 安装版满足）。macOS 用户换版本 = 下新的 zip 替换 .app，文档已如实写明。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TFM="net10.0"
PACK_ID="Prtsc"
BUNDLE_ID="com.cholf5.prtsc"
MIN_MACOS="12.0"
OUT_DIR="$ROOT/artifacts/pack"
ICON_PNG="$ROOT/assets/app-icon-macos.png"

VERSION=""
RIDS="osx-arm64"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --rids) RIDS="$2"; shift 2 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done
if [[ -z "$VERSION" ]]; then
  VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$ROOT/Directory.Build.props" | head -1 | tr -d '[:space:]')"
fi
[[ -n "$VERSION" ]] || { echo "ERROR: no version (pass --version or check Directory.Build.props)" >&2; exit 2; }

# RID 白名单：rid 会被拼进 rm -rf 与输出路径，"../.." 绝不能流到 rm -rf。
# 正则放变量里——bash 3.2（macOS 自带）对未加引号的 =~ 字面量不稳。
rid_re='^osx-[a-z0-9]+$'
for rid in $RIDS; do
  [[ "$rid" =~ $rid_re ]] || { echo "非法 RID: ${rid}（本脚本只打 osx-arm64 / osx-x64）" >&2; exit 2; }
done

# ad-hoc 封签（`codesign -s -`）。为什么必须封：单文件 apphost 的内嵌签名只覆盖可执行文件本身，
# 组装成 bundle 之后不重新封，`codesign --verify` 就不过；而从网络下载的副本带 quarantine 属性，
# 封签不完整会被 Gatekeeper 判「已损坏」（这个提示没有「仍要打开」）。ad-hoc 让下载副本走标准的
# 「未验证开发者」提示，用户能放行。彻底消除提示需要付费开发者账号 + 公证，本版不做。
sign_app() {
  local app="$1"
  if ! command -v codesign >/dev/null 2>&1; then
    echo "警告: 本机无 codesign（非 macOS），${app} 未封签——本机可直接运行，但下载分发会被判 damaged" >&2
    return 0
  fi
  # 嵌套的 Mach-O 二进制（*.dylib、createdump）先逐个封，否则整包验签不过。
  # 跳过主可执行文件：它躺在 Contents/MacOS 里，codesign 会把这一路径认成 bundle 本身，
  # 单独签它反而在「bundle 里还有未签代码」上失败——主可执行文件由最后的 --deep 整包签名覆盖。
  local main_exe="$app/Contents/MacOS/${PACK_ID}"
  local nested
  for nested in "$app"/Contents/MacOS/*; do
    [[ -f "$nested" ]] || continue
    [[ "$nested" == "$main_exe" ]] && continue
    case "$(file -b "$nested")" in
      Mach-O*) codesign --force -s - "$nested" || { echo "ad-hoc 签名失败: ${nested}" >&2; exit 1; } ;;
    esac
  done
  # 整包封签要带 --deep：Contents/MacOS 里除了 Mach-O 还有 200 多个托管 *.dll（self-contained 的
  # apphost 要在同目录找它们），而 codesign 把它们也当「嵌套代码对象」看——不带 --deep 时封签
  # 直接报 `code object is not signed at all / In subcomponent: ...System.Xml.XPath.XDocument.dll`。
  # Apple 把 --deep 定位成应急手段（正解是发布方提供已签好的规范包），对 ad-hoc 分发这里就是正解。
  codesign --force --deep -s - "$app" || {
    echo "ad-hoc 签名失败: ${app}" >&2
    exit 1
  }
  codesign --verify --strict "$app" || { echo "签名校验失败（封签不完整）: ${app}" >&2; exit 1; }
  echo "    已 ad-hoc 封签并验签通过"
}

# .icns 由 Apple 网格版 PNG 现场生成（sips + iconutil 均为 macOS 自带）。
# 拿不到工具时只警告：图标缺失影响 Finder/Dock 观感，不影响可运行性。
make_icns() {
  local png="$1" out="$2"
  if ! command -v iconutil >/dev/null 2>&1 || ! command -v sips >/dev/null 2>&1; then
    echo "警告: 无 sips/iconutil（非 macOS），跳过 .icns 生成：${out}" >&2
    return 0
  fi
  if [[ ! -f "$png" ]]; then
    echo "警告: 缺 ${png}（python3 scripts/make-app-icon.py 可重建），跳过 .icns" >&2
    return 0
  fi
  local iconset="${out%.icns}.iconset"
  rm -rf "$iconset"; mkdir -p "$iconset"
  for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    local double=$((size * 2))
    sips -z "$double" "$double" "$png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset" -o "$out"
  rm -rf "$iconset"
}

write_plist() {
  local path="$1"
  cat > "$path" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>CFBundleName</key><string>${PACK_ID}</string>
    <key>CFBundleDisplayName</key><string>${PACK_ID}</string>
    <key>CFBundleExecutable</key><string>${PACK_ID}</string>
    <key>CFBundleIdentifier</key><string>${BUNDLE_ID}</string>
    <key>CFBundleShortVersionString</key><string>${VERSION}</string>
    <key>CFBundleVersion</key><string>${VERSION}</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>LSMinimumSystemVersion</key><string>${MIN_MACOS}</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSHumanReadableCopyright</key><string>Copyright (c) cholf5 — AGPL-3.0-or-later</string>
  </dict>
</plist>
PLIST
}

mkdir -p "$OUT_DIR"

for rid in $RIDS; do
  PUBLISH_DIR="$OUT_DIR/publish-$rid"
  STAGE_DIR="$OUT_DIR/stage-$rid"
  APP="$STAGE_DIR/${PACK_ID}.app"
  ZIP="$OUT_DIR/${PACK_ID}-${VERSION}-${rid}.zip"

  echo "==> publish ${rid} (${TFM}, self-contained)"
  # 先清空 publish 目录：上一轮的残留会被整目录打进 bundle
  rm -rf "$PUBLISH_DIR" "$STAGE_DIR"
  mkdir -p "$PUBLISH_DIR"
  dotnet publish "$ROOT/src/Prtsc.App" \
    -c Release -f "$TFM" -r "$rid" --self-contained true \
    -o "$PUBLISH_DIR"

  if [[ ! -f "$PUBLISH_DIR/${PACK_ID}" ]]; then
    echo "ERROR: publish output missing ${PACK_ID} at $PUBLISH_DIR" >&2
    exit 2
  fi

  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  # 整个 publish 目录搬进 Contents/MacOS，不猜哪些文件必需：
  # self-contained 的 apphost 不是单文件（没有 PublishSingleFile），宿主会在同目录找 `Prtsc.dll`
  # 与全部托管程序集；只拷可执行文件 + *.dylib 的后果是启动即报
  # 「The application to execute does not exist: .../Prtsc.dll」。原生依赖也因平台/库版本而异，
  # 漏一个就是启动即崩。
  cp -R "$PUBLISH_DIR"/. "$APP/Contents/MacOS"/
  rm -f "$APP/Contents/MacOS/"*.pdb
  chmod +x "$APP/Contents/MacOS/${PACK_ID}"

  write_plist "$APP/Contents/Info.plist"
  make_icns "$ICON_PNG" "$APP/Contents/Resources/AppIcon.icns"
  sign_app "$APP"

  echo "==> zip"
  # ditto 保留符号链接与扩展属性；zip 会把 .app 里的链接拍平（框架与 .dylib 的链接结构会坏）
  rm -f "$ZIP"
  ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
  echo "    $ZIP"

  rm -rf "$STAGE_DIR" "$PUBLISH_DIR"
done

echo
echo "=== artifacts/pack ==="
ls -lh "$OUT_DIR" | awk 'NR>1 {print "  " $9 "  (" $5 ")"}'

cat <<'NEXT'

=== 分发提示 ===
- 产物未公证（ad-hoc 签名）。用户首次打开会看到「无法验证开发者」；macOS 26 上右键「打开」已不再能绕过：
    xattr -cr /Applications/Prtsc.app
  或 系统设置 → 隐私与安全性 → 仍要打开 → 输开机密码。
- 首次截图要授权「屏幕录制」：系统设置 → 隐私与安全性 → 屏幕录制 → 勾选 Prtsc → 重启应用。
NEXT
