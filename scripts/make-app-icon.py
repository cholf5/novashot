#!/usr/bin/env python3
"""R-1/E-14: 生成 Novashot 品牌图标 —— assets/app.ico（vpk --icon）与 assets/app-icon.png（macOS icns）。

品牌语言对齐截图编辑器默认背景渐变（EditorConfig.BackgroundColor 默认值
linear-gradient(135deg, #667eea 0%, #764ba2 100%)）：靛蓝→紫渐变圆角方块 + 白色相机剪影。

可复现：python3 scripts/make-app-icon.py
依赖：Pillow（PIL）。
输出：
  assets/app.ico       ICO 内含 16/32/48/64/128/256 六档（从 1024 主图缩放）
  assets/app-icon.png  1024×1024 满幅主图（Windows/通用）
  assets/app-icon-macos.png
                       Apple 图标网格版：830px 内容居中于 1024 画布、四周 97px 透明边距。
                       macOS 的 Dock 图标自带这个留白，满幅图会被画得比邻居大一圈。
                       scripts/pack-macos.sh 用它现场生成 .icns。
"""
from PIL import Image, ImageDraw

SIZE = 1024  # 主图尺寸（ICO 多档与 .icns 都从这里缩放）
SS = 2       # 超采样倍数，抗锯齿

GRAD_FROM = (102, 126, 234)  # #667eea
GRAD_TO = (118, 75, 162)     # #764ba2
WHITE = (255, 255, 255, 255)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def gradient(draw_rect, samples=256):
    """对角渐变（135°，左上→右下）。

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


def main():
    s = SIZE * SS
    canvas = gradient(s)

    draw = ImageDraw.Draw(canvas)

    # 圆角方块底
    radius = int(s * 0.22)
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=radius, fill=255)

    # 白色相机剪影（居中）：机身圆角矩形 + 镜头圆环 + 顶部取景器凸起
    white = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    wd = ImageDraw.Draw(white)
    m = int(s * 0.20)                      # 机身边距
    body = [m, int(s * 0.30), s - m, s - m]
    wd.rounded_rectangle(body, radius=int(s * 0.07), fill=WHITE)
    hump = int(s * 0.34)                   # 取景器凸起宽度
    cx = s // 2
    wd.rounded_rectangle([cx - hump // 2, int(s * 0.22), cx + hump // 2, int(s * 0.36)],
                         radius=int(s * 0.04), fill=WHITE)
    # 镜头：抠出底色再叠环
    lens_r = int(s * 0.165)
    hole = ImageDraw.Draw(white)
    hole.ellipse([cx - lens_r, s // 2 - lens_r + int(s * 0.02),
                  cx + lens_r, s // 2 + lens_r + int(s * 0.02)], fill=(0, 0, 0, 0))
    ring_r = int(s * 0.115)
    hole.ellipse([cx - ring_r, s // 2 - ring_r + int(s * 0.02),
                  cx + ring_r, s // 2 + ring_r + int(s * 0.02)], fill=WHITE)
    # 闪光灯小点
    dot_r = int(s * 0.02)
    hole.ellipse([int(s * 0.30) - dot_r, int(s * 0.38) - dot_r,
                  int(s * 0.30) + dot_r, int(s * 0.38) + dot_r], fill=(0, 0, 0, 0))

    canvas.alpha_composite(white)

    # 圆角裁切 + 缩回主图尺寸
    canvas.putalpha(mask)
    master = canvas.resize((SIZE, SIZE), Image.LANCZOS)

    ico = "assets/app.ico"
    master.save(ico, format="ICO",
                sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"wrote {ico}")

    png = "assets/app-icon.png"
    master.save(png, format="PNG")
    print(f"wrote {png}")

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
