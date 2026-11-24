"""產生 icon.png（NuGet 套件圖示與 README 用）。

概念：兩顆一樣的按鈕。上面那顆有焦點框、旁邊有聲波（到得了、念得出來）；
下面那顆什麼都沒有，用警告色 —— 這支工具找的就是它。

風格跟家族其他套件一致（XamlFit、XamlKeys、Parity、XamlContrast）：圓角方塊、扁平、單一概念圖形、同一個底色。

重新產生：py -3 scripts/make-icon.py（以 4 倍大小繪製再縮小，邊緣才會平滑）
"""

from PIL import Image, ImageDraw

SIZE = 256
S = 4
N = SIZE * S

BG = (24, 27, 33, 255)          # 深炭灰（跟 XamlKeys、XamlFit 同一個底）
FOCUS = (120, 190, 230, 255)    # 焦點框：淡天藍
OK = (235, 238, 242, 255)       # 念得出來的：近白
BAD = (255, 112, 84, 255)       # 念不出來的：珊瑚紅


def main():
    out = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)
    d.rounded_rectangle([0, 0, N - 1, N - 1], radius=int(N * 0.19), fill=BG)

    x0, x1 = int(N * 0.17), int(N * 0.55)
    h = int(N * 0.20)

    # 上：有焦點框
    y = int(N * 0.35)
    pad = int(N * 0.055)
    d.rounded_rectangle([x0 - pad, y - h // 2 - pad, x1 + pad, y + h // 2 + pad],
                        radius=int(N * 0.075), outline=FOCUS, width=int(N * 0.03))
    d.rounded_rectangle([x0, y - h // 2, x1, y + h // 2], radius=int(N * 0.04), fill=OK)

    # 聲波：三段弧
    cx = int(N * 0.60)
    for i, r in enumerate((0.085, 0.155, 0.225)):
        rr = int(N * r)
        d.arc([cx - rr, y - rr, cx + rr, y + rr], start=-42, end=42, fill=OK, width=int(N * 0.032))

    # 下：沒有焦點框、沒有聲音
    y2 = int(N * 0.70)
    d.rounded_rectangle([x0, y2 - h // 2, x1, y2 + h // 2], radius=int(N * 0.04), fill=BAD)
    # 靜音：一條橫線
    d.rounded_rectangle([int(N * 0.66), y2 - int(N * 0.017), int(N * 0.82), y2 + int(N * 0.017)],
                        radius=int(N * 0.017), fill=BAD)

    out = out.resize((SIZE, SIZE), Image.LANCZOS)
    out.save("icon.png", optimize=True)


if __name__ == "__main__":
    main()
