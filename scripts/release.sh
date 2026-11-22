#!/usr/bin/env bash
# 一键发版：bump Directory.Build.props <Version> → commit → tag vX.Y.Z → push；
# CI（.github/workflows/ci.yml）随后跑三个测试腿、打 Windows（Velopack）与 macOS（.app）包、
# 做完装/卸与 Xvfb 启动冒烟，再创建 GitHub Release 并上传资产。
#
# 用法：scripts/release.sh <x.y.z> [--skip-test] [--watch]
#   --skip-test   跳过本地测试（CI 仍会跑；测试挂了不会出 Release）
#   --watch       push 后盯 CI 到完成，并核对 Release 资产齐全（需要 gh 且已登录）
#
# 恢复路径：CI 挂了时 tag 已经推上去了。修好之后：
#   git push origin :refs/tags/vX.Y.Z && git tag -d vX.Y.Z
# 再重跑本脚本（<Version> 已在目标版本时会跳过 bump 提交，直接重打 tag）。
# 版本号必须等于 Directory.Build.props <Version>；workflow 在 tag push 时强制这一条。
#
# 注意：echo 里紧挨全角字符的变量一律写 ${VAR}。macOS 自带的 bash 3.2 会把多字节字符
# 的高位字节当成 isalpha 的一部分折进变量名，于是死于 unbound variable。
set -eu
cd "$(dirname "$0")/.."

usage() { echo "用法: scripts/release.sh <x.y.z> [--skip-test] [--watch]" >&2; exit 1; }

VERSION=""
SKIP_TEST=false
WATCH=false
for arg in "$@"; do
  case "$arg" in
    --skip-test) SKIP_TEST=true ;;
    --watch) WATCH=true ;;
    *) VERSION=$arg ;;
  esac
done
[ -n "$VERSION" ] || usage

# 三段数字，与应用内更新比较的 System.Version 语义对齐；不接前导零
# （01.02.003 不可能是有人故意要的版本号，而 tag 同时驱动 <Version>、资产名与更新比较）。
# 刻意不做单调性检查：降级也必须把 <Version> 一起降下来才能过 tag 断言——那是一次可见的显式提交，
# 而更新比较把更低的远端版本当「已是最新」，写错的降级很响却不伤人。
echo "$VERSION" | grep -Eq '^([0-9]|[1-9][0-9]*)\.([0-9]|[1-9][0-9]*)\.([0-9]|[1-9][0-9]*)$' || { echo "版本号须为 x.y.z 三段数字：${VERSION}" >&2; exit 1; }

PROPS=Directory.Build.props
TAG=v$VERSION
NOTES="docs/release/notes/${TAG}.md"

[ "$(git symbolic-ref --short HEAD)" = main ] || { echo "不在 main 分支" >&2; exit 1; }
git fetch --quiet
BEHIND=$(git rev-list --count HEAD..@{u})
[ "$BEHIND" -eq 0 ] || { echo "main 落后远端 ${BEHIND} 个提交，先 pull" >&2; exit 1; }
git diff --quiet && git diff --cached --quiet || { echo "工作树有未提交改动，先提交或暂存" >&2; exit 1; }
UNTRACKED=$(git ls-files --others --exclude-standard | wc -l | tr -d ' ')
[ "$UNTRACKED" -eq 0 ] || echo "注意：有 ${UNTRACKED} 个未跟踪文件，不会进入本次发布" >&2

# 「已发布」的真义是 tag 存在；<Version> 恰好等于目标版本只表示不用 bump
# （预先写好的首版版本号、以及上一次发布失败后重跑，都会走到这条路上）。
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && { echo "本地已存在 ${TAG}" >&2; exit 1; }
[ -z "$(git ls-remote --tags origin "refs/tags/$TAG")" ] || { echo "远端已存在 ${TAG}" >&2; exit 1; }

read_version() {
  sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$PROPS" | head -1 | tr -d '[:space:]'
}

CUR=$(read_version)
[ -n "$CUR" ] || { echo "无法从 ${PROPS} 读取 <Version>" >&2; exit 1; }

# 手写发版说明：CI 的 release 步骤优先 --notes-file，缺文件才回落 --generate-notes。
# 首个版本（没有上一个 tag）缺文件时，Release 正文会把全部提交历史抖出来，所以这里早提示。
[ -f "$NOTES" ] || echo "注意：没有 ${NOTES}，CI 将回落 --generate-notes（无上一个 tag 时=全部提交历史）" >&2

# 本地测试按宿主跑「自己平台能跑的那几条腿」——App.Tests 是双 TFM，它在 mac/linux 上跑 net10.0 腿、
# 在 Windows 上跑 windows TFM 腿；直接把两条腿都在本机跑会在另一条腿上挂（WinRT / macOS P/Invoke）。
# Linux.Tests 用 Fake X11，不碰真实 X server，mac 上也能跑。
run_tests() {
  dotnet test tests/Novashot.Core.Tests/Novashot.Core.Tests.csproj --nologo -v q
  dotnet test tests/Novashot.Linux.Tests/Novashot.Linux.Tests.csproj --nologo -v q
  case "$OS" in
    Darwin) dotnet test tests/Novashot.Mac.Tests/Novashot.Mac.Tests.csproj --nologo -v q
            dotnet test tests/Novashot.App.Tests/Novashot.App.Tests.csproj -f net10.0 --nologo -v q ;;
    *)      dotnet test tests/Novashot.App.Tests/Novashot.App.Tests.csproj -f net10.0-windows10.0.19041.0 --nologo -v q ;;
  esac
}

OS=$(uname -s)
if [ "$SKIP_TEST" != true ]; then
  echo "本地跑测试（--skip-test 可跳过）..."
  run_tests || { echo "本地测试失败，中止" >&2; exit 1; }
fi

if [ "$CUR" != "$VERSION" ]; then
  # 替换 <Version>：写临时文件绕开 BSD/GNU sed 的 -i 差异。
  # mktemp 建在 props 旁边（不是 /tmp），最后一步 mv 才是同卷原子改名；chmod 把 mktemp 的 0600
  # 拉回 644。EXIT trap 在 sed/校验失败时清掉残留临时文件——改名成功之后它是空操作。
  TMP=$(mktemp "$PROPS.tmp.XXXXXX")
  trap 'rm -f "$TMP"' EXIT
  sed "s#\(<Version>\)[^<]*\(</Version>\)#\1$VERSION\2#" "$PROPS" > "$TMP"
  chmod 644 "$TMP"
  mv "$TMP" "$PROPS"
  NEW=$(read_version)
  [ "$NEW" = "$VERSION" ] || { echo "版本替换校验失败：期望 ${VERSION}，实得 ${NEW}" >&2; exit 1; }

  git add "$PROPS"
  git commit -m "chore: 版本提升至 ${VERSION}（scripts/release.sh 自动提交，发版 tag ${TAG} 与之配套，CI 校验两者一致）"
else
  echo "<Version> 已是 ${VERSION}，跳过 bump 提交，直接打 tag"
fi

git tag -a "$TAG" -m "Novashot $TAG"
git push origin main "$TAG"

SLUG=$(git remote get-url origin | sed -E 's#.*github\.com[:/]##; s#\.git$##')
RUN_URL="https://github.com/$SLUG/actions"
REL_URL="https://github.com/$SLUG/releases/tag/$TAG"

if [ "$WATCH" != true ]; then
  echo "已推送 ${TAG}，CI 会自动打包并发布："
  echo "  Actions: $RUN_URL"
  echo "  Release: ${REL_URL}（流水线跑完后出现）"
  exit 0
fi

command -v gh >/dev/null 2>&1 || { echo "未安装 gh，无法 watch，手动看 $RUN_URL" >&2; exit 1; }

# gh 调用一律包超时。脚本声明「最长 45 分钟」，但 gh 自己没有超时：网络卡住、代理挂死或凭据失效时
# `gh run view` 会无限期挂着，watch 就永远不返回。这里用后台看门狗 kill，不用 coreutils 的 timeout：
# macOS 自带的 sh/bash 3.2 环境没有它，与脚本已有的「macOS 可移植」约束一致。
gh_with_timeout() {
  _gh_secs=$1
  shift
  "$@" &
  _gh_pid=$!
  ( sleep "$_gh_secs"; kill -9 "$_gh_pid" 2>/dev/null ) &
  _gh_watchdog=$!
  if wait "$_gh_pid"; then _gh_rc=0; else _gh_rc=$?; fi
  kill -9 "$_gh_watchdog" 2>/dev/null || true
  wait "$_gh_watchdog" 2>/dev/null || true
  return "$_gh_rc"
}

# 找 tag 触发的那条 run（push 之后注册要几秒）。按 tag 指向的 commit 过滤：同一版本重发时，
# 还存在一条更老的、branch 名同样是 tag 的 run，只按 branch 取第一条会抓到那条过期的。
TAG_SHA=$(git rev-parse "$TAG^{commit}")
RUN_ID=
i=0
while [ $i -lt 12 ]; do
  RUN_ID=$(gh_with_timeout 25 gh run list --branch "$TAG" --limit 10 --json databaseId,headSha \
    --jq "[.[] | select(.headSha == \"$TAG_SHA\")][0].databaseId" 2>/dev/null || true)
  [ -n "$RUN_ID" ] && [ "$RUN_ID" != null ] && break
  i=$((i + 1)); sleep 5
done
if [ -z "$RUN_ID" ] || [ "$RUN_ID" = null ]; then
  echo "未找到 ${TAG} 触发的 CI run，手动看 $RUN_URL" >&2
  exit 1
fi
echo "等待 CI（run ${RUN_ID}，最长 45 分钟）..."

# 截止时间驱动（不是计数驱动）：每次轮询的 gh 调用有 25 秒上限，按次数算会把总时长乘胖。
DEADLINE=$(( $(date +%s) + 45 * 60 ))
while :; do
  STATE=$(gh_with_timeout 25 gh run view "$RUN_ID" --json status,conclusion --jq '.status+"/"+(.conclusion // "-")' 2>/dev/null || echo unknown)
  case "$STATE" in
    completed/success) break ;;
    completed/*)
      echo "CI 失败（${STATE}）。修复后删除 tag 重跑：git push origin :refs/tags/$TAG && git tag -d $TAG" >&2
      echo "日志：$RUN_URL/$RUN_ID" >&2
      exit 1
      ;;
  esac
  if [ "$(date +%s)" -ge "$DEADLINE" ]; then
    echo "等待超时（45 分钟，最后状态 ${STATE}），手动看 $RUN_URL/$RUN_ID" >&2
    exit 1
  fi
  sleep 20
done

# 核对 Release 资产。这份清单是跨组件契约：scripts/pack-windows.sh（Velopack 出 Setup/Portable/nupkg/feed）、
# scripts/pack-macos.sh（.app zip）、.github/workflows/ci.yml（上传）与本脚本三处必须同步改。
# 「查询失败」与「资产缺失」分开报：查询失败过去会表现为空列表，把一次其实成功的发布打回去重跑。
ASSETS=
i=0
while [ "$i" -lt 3 ]; do
  if ASSETS=$(gh_with_timeout 25 gh release view "$TAG" --json assets --jq '[.assets[].name] | join(",")' 2>/dev/null); then
    break
  fi
  i=$((i + 1)); [ "$i" -lt 3 ] && sleep 5
done
if [ "$i" -eq 3 ]; then
  echo "无法核对 Release 产物：gh release view 连续 3 次失败（网络抖动或未 gh auth login）。" >&2
  echo "请手动核对 ${REL_URL}：产物齐全则无需任何补救动作；确有缺失再 Re-run release job 或删 tag 重跑。" >&2
  exit 1
fi

# 含 * 的条目按通配匹配（nupkg 的文件名带版本与 channel 段）。
has_asset() {
  case "$1" in
    *\**) for name in $(echo "$ASSETS" | tr ',' ' '); do
             case "$name" in $1) return 0 ;; esac  # shellcheck disable=SC2254 — 按 glob 匹配是刻意的
           done
           return 1 ;;
    *) case ",$ASSETS," in
         *",$1,"*) return 0 ;;
         *) return 1 ;;
       esac ;;
  esac
}

MISSING=
for want in "Novashot-win-Setup.exe" "Novashot-win-Portable.zip" "Novashot-*-full.nupkg" \
  "releases.win.json" "RELEASES" "SHA256SUMS.txt" \
  "Novashot-${VERSION}-osx-arm64.zip" "Novashot-${VERSION}-osx-x64.zip"; do
  has_asset "$want" || MISSING="${MISSING} ${want}"
done
if [ -n "$MISSING" ]; then
  echo "Release 产物缺失：${MISSING}。可在 Actions 页 Re-run release job，或删 tag 重跑脚本。" >&2
  exit 1
fi

echo "发版完成 ${TAG}：$REL_URL"
echo "产物：$ASSETS"
