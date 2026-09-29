using UnityEngine;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Анимированное лицо на визоре робота. Рисуется в маленькую текстуру (пиксельный стиль):
    /// глаза, моргание, «рот»-эквалайзер при разговоре, испуг, крестики при смерти.
    /// Экран старый: пыль и царапины приглушают свечение, бывают глитчи и битые строки.
    /// </summary>
    public class RobotFace
    {
        public enum Mood { Normal, Talking, Scared, Dead }

        public const int W = 64, H = 40;

        public Texture2D Texture { get; }
        /// <summary>Общая для всех «грязная» подложка стекла (базовый цвет визора).</summary>
        public static Texture2D DirtyGlass => dirtyGlass ??= MakeDirtyGlass();

        static Texture2D dirtyGlass;
        static float[] dustMask;

        readonly Color32[] px = new Color32[W * H];
        readonly System.Random rng;
        readonly int deadColumn;
        float blinkTimer, blinkLeft, timer, lookX, lookTargetX, lookTimer;

        public RobotFace(int seed)
        {
            rng = new System.Random(seed);
            deadColumn = rng.Next(4, W - 4);
            Texture = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "RobotFace",
            };
            blinkTimer = 1f + (float)rng.NextDouble() * 3f;
            EnsureDust();
        }

        public void Dispose()
        {
            if (Texture != null) Object.Destroy(Texture);
        }

        /// <summary>Обновляет лицо ~12 раз в секунду. fear: 0..1.</summary>
        public void Update(float dt, Mood mood, float fear, float talkLevel)
        {
            timer -= dt;
            blinkTimer -= dt;
            lookTimer -= dt;
            if (lookTimer <= 0f)
            {
                lookTimer = 1.5f + (float)rng.NextDouble() * 3f;
                lookTargetX = ((float)rng.NextDouble() - 0.5f) * (mood == Mood.Scared ? 10f : 5f);
            }
            lookX = Mathf.Lerp(lookX, lookTargetX, dt * 6f);
            if (timer > 0f) return;
            timer = 1f / 12f;

            if (blinkTimer <= 0f)
            {
                blinkLeft = 0.14f;
                blinkTimer = (mood == Mood.Scared ? 0.6f : 2.2f) + (float)rng.NextDouble() * 3f;
            }
            blinkLeft -= 1f / 12f;

            Draw(mood, fear, talkLevel, blinkLeft > 0f);
        }

        void Draw(Mood mood, float fear, float talk, bool blink)
        {
            var bg = new Color32(4, 14, 8, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var fg = mood == Mood.Dead ? new Color32(255, 70, 50, 255)
                : mood == Mood.Scared ? new Color32(255, 225, 120, 255)
                : new Color32(110, 255, 150, 255);

            int ox = Mathf.RoundToInt(lookX) + (mood == Mood.Scared ? rng.Next(-1, 2) : 0);
            int eyeY = 22;
            if (mood == Mood.Dead)
            {
                Cross(20 + ox, eyeY, 5, fg);
                Cross(44 + ox, eyeY, 5, fg);
                Line(24, 10, 40, 10, fg);
            }
            else
            {
                int ew = mood == Mood.Scared ? 4 : 6;
                int eh = blink ? 1 : mood == Mood.Scared ? 5 : 10;
                RoundRect(20 + ox, eyeY, ew, eh, fg);
                RoundRect(44 + ox, eyeY, ew, eh, fg);
                if (mood == Mood.Scared)
                {
                    // капля пота и дрожащий рот
                    Rect(53 + ox, 30, 1, 3, fg);
                    for (int x = 26; x < 38; x += 2) Rect(x, 9 + (x / 2 % 2), 2, 1, fg);
                }
                else if (mood == Mood.Talking || talk > 0.05f)
                {
                    // эквалайзер вместо рта
                    for (int b = 0; b < 7; b++)
                    {
                        float amp = Mathf.Clamp01(talk * (0.5f + (float)rng.NextDouble()));
                        int h = 1 + Mathf.RoundToInt(amp * 6f);
                        Rect(25 + b * 2, 10 - h / 2, 1, h, fg);
                    }
                }
            }

            // сканлайны, пыль, битые пиксели
            for (int y = 0; y < H; y++)
            {
                float scan = y % 2 == 0 ? 0.72f : 1f;
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float k = scan * dustMask[i];
                    var c = px[i];
                    px[i] = new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);
                }
            }
            for (int y = 0; y < H; y++) // вертикальная полоса выгоревших пикселей
            {
                var c = px[y * W + deadColumn];
                px[y * W + deadColumn] = new Color32((byte)(c.r / 3), (byte)(c.g / 3), (byte)(c.b / 3), 255);
            }

            // глитч: сдвиг строк, чаще при страхе
            if (rng.NextDouble() < 0.05 + fear * 0.5 || mood == Mood.Dead && rng.NextDouble() < 0.3)
            {
                int rows = 1 + rng.Next(0, 4 + (int)(fear * 8));
                var line = new Color32[W];
                for (int g = 0; g < rows; g++)
                {
                    int row = rng.Next(0, H);
                    int shift = rng.Next(-8, 9);
                    for (int x = 0; x < W; x++) line[x] = px[row * W + ((x - shift) % W + W) % W];
                    for (int x = 0; x < W; x++) px[row * W + x] = line[x];
                }
            }

            Texture.SetPixels32(px);
            Texture.Apply(false);
        }

        // ------------------------------------------------------------ рисование

        void Set(int x, int y, Color32 c)
        {
            if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c;
        }

        void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                Set(xx, yy, c);
        }

        void RoundRect(int cx, int cy, int hw, int hh, Color32 c)
        {
            for (int y = -hh / 2; y <= hh / 2; y++)
            for (int x = -hw; x <= hw; x++)
            {
                bool corner = (Mathf.Abs(x) == hw) && (Mathf.Abs(y) == hh / 2) && hh > 2;
                if (!corner) Set(cx + x, cy + y, c);
            }
        }

        void Cross(int cx, int cy, int r, Color32 c)
        {
            for (int i = -r; i <= r; i++)
            {
                Set(cx + i, cy + i, c); Set(cx + i + 1, cy + i, c);
                Set(cx + i, cy - i, c); Set(cx + i + 1, cy - i, c);
            }
        }

        void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int x = x0; x <= x1; x++) Set(x, y0 + (y1 - y0) * (x - x0) / Mathf.Max(1, x1 - x0), c);
        }

        // ------------------------------------------------------------ грязь

        static void EnsureDust()
        {
            if (dustMask != null) return;
            dustMask = new float[W * H];
            var r = new System.Random(77);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.09f + 3.1f, y * 0.12f + 7.7f);
                float dust = Mathf.SmoothStep(0.45f, 0.8f, n) * 0.55f;
                float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y)) / 6f);
                dustMask[y * W + x] = Mathf.Clamp01((1f - dust) * Mathf.Lerp(0.55f, 1f, edge));
            }
            for (int s = 0; s < 5; s++) // царапины
            {
                int x = r.Next(0, W), y = r.Next(0, H), len = r.Next(8, 24);
                int dx = r.Next(0, 2) == 0 ? 1 : -1;
                for (int i = 0; i < len; i++, x += dx, y += (i % 3 == 0 ? 1 : 0))
                    if (x >= 0 && x < W && y >= 0 && y < H) dustMask[y * W + x] *= 0.35f;
            }
        }

        static Texture2D MakeDirtyGlass()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "DirtyGlass", wrapMode = TextureWrapMode.Clamp };
            var p = new Color32[n * n];
            var r = new System.Random(5);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float smudge = Mathf.SmoothStep(0.5f, 0.85f, Mathf.PerlinNoise(x * 0.05f + 11f, y * 0.07f + 3f));
                float speck = r.NextDouble() < 0.012 ? 0.5f : 0f;
                float v = 0.03f + smudge * 0.16f + speck * 0.2f;
                byte b = (byte)(Mathf.Clamp01(v) * 255);
                p[y * n + x] = new Color32(b, (byte)(b * 0.97f), (byte)(b * 0.9f), 255);
            }
            for (int s = 0; s < 14; s++)
            {
                int x = r.Next(0, n), y = r.Next(0, n), len = r.Next(10, 50);
                for (int i = 0; i < len; i++)
                {
                    int xx = x + i, yy = y + i / 4;
                    if (xx < n && yy < n) p[yy * n + xx] = new Color32(70, 68, 62, 255);
                }
            }
            tex.SetPixels32(p);
            tex.Apply(true);
            return tex;
        }
    }
}
