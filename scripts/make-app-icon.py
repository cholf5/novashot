#!/usr/bin/env python3
"""R-1: 生成 Prtsc 品牌 Windows 图标 assets/app.ico（vpk --icon 用）。

品牌语言对齐截图编辑器默认背景渐变（EditorConfig.BackgroundColor 默认值
linear-gradient(135deg, #667eea 0%, #764ba2 100%)）：靛蓝→紫渐变圆角方块 + 白色相机剪影。

可复现：python3 scripts/make-app-icon.py
依赖：Pillow（PIL）。输出 ICO 内含 16/32/48/64/128/256 六档（PIL 从 256 主图缩放）。
"""
from PIL import Image, ImageDraw

SIZE = 256  # 主图尺寸（绘制后再多档缩放入 ICO）
SS = 4      # 超采样倍数，抗锯齿

GRAD_FROM = (102, 126, 234)  # #667eea
GRAD_TO = (118, 75, 162)     # #764ba2
WHITE = (255, 255, 255, 255)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def gradient(draw_rect):
    """对角渐变（135°，左上→右下）。"""
    img = Image.new("RGBA", (draw_rect, draw_rect), (0, 0, 0, 0))
    px = img.load()
    for y in range(draw_rect):
        for x in range(draw_rect):
            t = (x + y) / (2 * draw_rect - 2)
            px[x, y] = lerp(GRAD_FROM, GRAD_TO, t) + (255,)
    return img


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

    # 圆角裁切 + 缩回 256
    canvas.putalpha(mask)
    master = canvas.resize((SIZE, SIZE), Image.LANCZOS)

    out = "assets/app.ico"
    master.save(out, format="ICO",
                sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
