#!/usr/bin/env python3
"""R-1/E-14: 生成 Novashot 品牌图标 —— assets/app.ico（vpk --icon / exe ApplicationIcon）与
assets/app-icon.png（macOS icns 源）。

设计源：assets/app-icon.svg（ChatGPT 独立设计、用户选定，方向「爆发」——三层爆发星 +
取景框四角，隐喻「用选框抓住一次新星爆发」）。本脚本是该 SVG 的忠实 PIL 转译：
几何坐标一律 SVG 原值 × k（k = 超采样边长/1024），颜色与透明度逐项对应，资产以脚本
产物为准（可复现，改图标先改 SVG 再同步到这里）。

品牌渐变对齐截图编辑器默认背景（EditorConfig.BackgroundColor 默认值
linear-gradient(135deg, #667eea 0%, #764ba2 100%)）。

可复现：python3 scripts/make-app-icon.py
依赖：Pillow（PIL）。
输出：
  assets/app.ico               ICO 16/32/48/64/128/256（vpk --icon + csproj ApplicationIcon）
  assets/app-icon.png          1024×1024 满幅主图（macOS icns 源）
  assets/app-icon-runtime.png  256×256 运行时版（AppIconFactory 嵌入，窗口/托盘图标）
  assets/app-icon-macos.png    Apple 图标网格版：830 内容居中于 1024 画布、四周 97 透明边距。
                               macOS 的 Dock 图标自带这个留白，满幅图会被画得比邻居大一圈。
                               scripts/pack-macos.sh 用它现场生成 .icns。
"""
from PIL import Image, ImageDraw

SIZE = 1024  # 主图尺寸（ICO 多档与 .icns 都从这里缩放）
SS = 2       # 超采样倍数，抗锯齿

GRAD_FROM = (102, 126, 234)  # #667EEA
GRAD_TO = (118, 75, 162)     # #764BA2
WHITE = (255, 255, 255, 255)

# —— assets/app-icon.svg 的几何与颜色原值（viewBox 1024）——
RADIUS = 224  # rect rx
STAR_OUTER = [(512, 190), (586, 412), (812, 512), (586, 586),
              (512, 834), (438, 586), (212, 512), (438, 412)]        # fill #fff
STAR_MID = [(512, 342), (552, 472), (682, 512), (552, 552),
            (512, 682), (472, 552), (342, 512), (472, 472)]          # fill #667EEA（平色）
STAR_INNER = [(512, 390), (541, 483), (634, 512), (541, 541),
              (512, 634), (483, 541), (390, 512), (483, 483)]        # fill #fff
BRACKETS = [  # 取景框四角，stroke #fff width=24 圆头，opacity .9
    [(236, 300), (236, 236), (300, 236)],
    [(724, 236), (788, 236), (788, 300)],
    [(788, 724), (788, 788), (724, 788)],
    [(300, 788), (236, 788), (236, 724)],
]
BRACKET_W = 24
BRACKET_OPACITY = 0.9


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def gradient(draw_rect, samples=256):
    """对角渐变（135°，左上→右下），与 SVG linearGradient(0,0 → 1,1) 在正方形上同口径。

    纯 Python 逐像素，按超采样尺寸（2048²）算要 4M 次迭代；渐变是线性的，先按 samples²
    算一遍再放大，肉眼与逐像素结果一致。
    """
    img = Image.new("RGBA", (samples, samples), (0, 0, 0, 0))
    px = img.load()
    for y in range(samples):
        for x in range(samples):
            t = (x + y) / (2 * samples - 2)
            px[x, y] = lerp(GRAD_FROM, GRAD_TO, t) + (255,)
    return img if samples == draw_rect else img.resize((draw_rect, draw_rect), Image.BICUBIC)


def scaled(points, k):
    return [(x * k, y * k) for x, y in points]


def draw_brackets(s, k):
    """取景框四角。独立图层先画满白再整体乘 opacity：段内圆帽与转折处的重叠
    不会像逐笔半透明绘制那样叠亮（0.9 叠 0.9 → 0.99 的脏斑）。"""
    layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    bd = ImageDraw.Draw(layer)
    w = BRACKET_W * k
    r = w / 2
    for pts in BRACKETS:
        p = scaled(pts, k)
        bd.line(p, fill=WHITE, width=int(w), joint="curve")
        for x, y in p:  # SVG 圆头端帽 + 圆角转折（cap/join = round）
            bd.ellipse([x - r, y - r, x + r, y + r], fill=WHITE)
    a = layer.getchannel("A").point(lambda v: v * int(BRACKET_OPACITY * 255) // 255)
    layer.putalpha(a)
    return layer


def main():
    s = SIZE * SS
    k = s / SIZE
    canvas = gradient(s)

    # 三层爆发星：白 → 靛蓝 → 白
    draw = ImageDraw.Draw(canvas)
    draw.polygon(scaled(STAR_OUTER, k), fill=WHITE)
    draw.polygon(scaled(STAR_MID, k), fill=GRAD_FROM + (255,))
    draw.polygon(scaled(STAR_INNER, k), fill=WHITE)

    # 取景框四角（SVG 中最后绘制 = 最上层）
    canvas.alpha_composite(draw_brackets(s, k))

    # 圆角裁切 + 缩回主图尺寸
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=RADIUS * k, fill=255)
    canvas.putalpha(mask)
    master = canvas.resize((SIZE, SIZE), Image.LANCZOS)

    ico = "assets/app.ico"
    master.save(ico, format="ICO",
                sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"wrote {ico}")

    png = "assets/app-icon.png"
    master.save(png, format="PNG")
    print(f"wrote {png}")

    runtime = "assets/app-icon-runtime.png"
    master.resize((256, 256), Image.LANCZOS).save(runtime, format="PNG")
    print(f"wrote {runtime}")

    # Apple 图标网格：内容 830 居中于 1024 画布，四周 97 透明边距
    mac = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    inner = 830
    pad = (SIZE - inner) // 2
    mac.alpha_composite(master.resize((inner, inner), Image.LANCZOS), (pad, pad))
    mac_png = "assets/app-icon-macos.png"
    mac.save(mac_png, format="PNG")
    print(f"wrote {mac_png}")


if __name__ == "__main__":
    main()
