// QR codes for keys, so the phone app (Nevatch for Android, or v2rayNG, Hiddify, Streisand) can scan the key
// off this screen. Byte mode, error correction level M, the smallest version (1–40) that fits. C# 5, no dependencies.
using System;
using System.Collections.Generic;
using System.Text;

namespace Nevatch
{
    public static class Qr
    {
        // Per version 1–40 (index 0 unused), level M.
        static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26,
            28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 };
        static readonly int[] Blocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21,
            23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 };
        const int LevelM = 0; // format bits for level M

        // The modules, [y, x], true = dark, without the quiet zone around them. mask -1 picks the best one.
        public static bool[,] Encode(string text, int mask = -1)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int ver = 1;
            for (; ver <= 40; ver++) if (4 + (ver < 10 ? 8 : 16) + bytes.Length * 8 <= DataCodewords(ver) * 8) break;
            if (ver > 40) throw new ArgumentException("Too long for a QR code");

            var bits = new List<int>();
            Action<int, int> put = (v, n) => { for (int i = n - 1; i >= 0; i--) bits.Add((v >> i) & 1); };
            put(4, 4); // byte mode
            put(bytes.Length, ver < 10 ? 8 : 16);
            foreach (byte b in bytes) put(b, 8);
            int cap = DataCodewords(ver) * 8;
            put(0, Math.Min(4, cap - bits.Count));
            put(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) put(pad, 8);
            var data = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) data[i >> 3] |= (byte)(bits[i] << (7 - (i & 7)));
            byte[] codewords = AddEcc(data, ver);

            bool[,] best = null;
            int bestScore = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                if (mask >= 0 && m != mask) continue;
                bool[,] modules = Build(ver, codewords, m);
                int score = Penalty(modules);
                if (score < bestScore)
                {
                    best = modules;
                    bestScore = score;
                }
            }
            return best;
        }

        static int RawModules(int ver)
        {
            int n = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int align = ver / 7 + 2;
                n -= (25 * align - 10) * align - 55;
                if (ver >= 7) n -= 36;
            }
            return n;
        }

        static int DataCodewords(int ver) { return RawModules(ver) / 8 - EccPerBlock[ver] * Blocks[ver]; }

        // ---------- Reed–Solomon over GF(256) ----------
        static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        static int[] Divisor(int degree)
        {
            var r = new int[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    r[j] = Mul(r[j], root);
                    if (j + 1 < degree) r[j] ^= r[j + 1];
                }
                root = Mul(root, 0x02);
            }
            return r;
        }

        static int[] Remainder(List<int> data, int[] div)
        {
            var r = new int[div.Length];
            foreach (int b in data)
            {
                int factor = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < div.Length; i++) r[i] ^= Mul(div[i], factor);
            }
            return r;
        }

        // Data codewords split into blocks, each with its error correction, interleaved.
        static byte[] AddEcc(byte[] data, int ver)
        {
            int numBlocks = Blocks[ver], eccLen = EccPerBlock[ver];
            int raw = RawModules(ver) / 8;
            int numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
            int[] div = Divisor(eccLen);
            var blocks = new List<List<int>>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int len = shortLen - eccLen + (i < numShort ? 0 : 1);
                var dat = new List<int>();
                for (int j = 0; j < len; j++) dat.Add(data[k + j]);
                k += len;
                int[] ecc = Remainder(dat, div);
                if (i < numShort) dat.Add(0);
                dat.AddRange(ecc);
                blocks.Add(dat);
            }
            var result = new List<byte>();
            for (int i = 0; i < blocks[0].Count; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= numShort) result.Add((byte)blocks[j][i]);
            return result.ToArray();
        }

        static List<int> AlignmentPositions(int ver)
        {
            var r = new List<int>();
            if (ver == 1) return r;
            int num = ver / 7 + 2;
            int step = ver == 32 ? 26 : (ver * 4 + 4 + num * 2 - 3) / (num * 2 - 2) * 2;
            r.Add(6);
            for (int pos = ver * 4 + 17 - 7; r.Count < num; pos -= step) r.Insert(1, pos);
            return r;
        }

        static bool Masked(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (x / 3 + y / 2) % 2 == 0;
                case 5: return x * y % 2 + x * y % 3 == 0;
                case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
                default: return ((x + y) % 2 + x * y % 3) % 2 == 0;
            }
        }

        static bool Bit(int v, int i) { return ((v >> i) & 1) != 0; }

        static bool[,] Build(int ver, byte[] codewords, int mask)
        {
            int size = ver * 4 + 17;
            var m = new bool[size, size];
            var fn = new bool[size, size];
            Action<int, int, bool> set = (x, y, dark) => { m[y, x] = dark; fn[y, x] = true; };

            for (int i = 0; i < size; i++)
            {
                set(6, i, i % 2 == 0);
                set(i, 6, i % 2 == 0);
            }
            foreach (int[] c in new[] { new[] { 3, 3 }, new[] { size - 4, 3 }, new[] { 3, size - 4 } })
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = c[0] + dx, y = c[1] + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        if (x >= 0 && x < size && y >= 0 && y < size) set(x, y, d != 2 && d != 4);
                    }
            List<int> pos = AlignmentPositions(ver);
            int last = pos.Count - 1;
            for (int i = 0; i < pos.Count; i++)
                for (int j = 0; j < pos.Count; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            set(pos[j] + dx, pos[i] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }

            // Format bits (both copies) and the dark module.
            int data = (LevelM << 3) | mask, rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int fmt = ((data << 10) | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) set(8, i, Bit(fmt, i));
            set(8, 7, Bit(fmt, 6));
            set(8, 8, Bit(fmt, 7));
            set(7, 8, Bit(fmt, 8));
            for (int i = 9; i < 15; i++) set(14 - i, 8, Bit(fmt, i));
            for (int i = 0; i < 8; i++) set(size - 1 - i, 8, Bit(fmt, i));
            for (int i = 8; i < 15; i++) set(8, size - 15 + i, Bit(fmt, i));
            set(8, size - 8, true);

            if (ver >= 7)
            {
                int r = ver;
                for (int i = 0; i < 12; i++) r = (r << 1) ^ ((r >> 11) * 0x1F25);
                int vbits = (ver << 12) | r;
                for (int i = 0; i < 18; i++)
                {
                    int a = size - 11 + i % 3, b = i / 3;
                    set(a, b, Bit(vbits, i));
                    set(b, a, Bit(vbits, i));
                }
            }

            // Codewords in the zigzag, masked.
            int n = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (fn[y, x]) continue;
                        bool dark = false;
                        if (n < codewords.Length * 8)
                        {
                            dark = Bit(codewords[n >> 3], 7 - (n & 7));
                            n++;
                        }
                        m[y, x] = dark != Masked(mask, x, y);
                    }
            }
            return m;
        }

        // The usual penalty score; the mask with the lowest one is easiest to scan.
        static int Penalty(bool[,] m)
        {
            int size = m.GetLength(0), score = 0, dark = 0;
            var line = new bool[size];
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < size; a++)
                {
                    for (int b = 0; b < size; b++) line[b] = pass == 0 ? m[a, b] : m[b, a];
                    int run = 1;
                    for (int i = 1; i <= size; i++)
                    {
                        if (i < size && line[i] == line[i - 1]) run++;
                        else
                        {
                            if (run >= 5) score += run - 2;
                            run = 1;
                        }
                    }
                    for (int i = 0; i + 11 <= size; i++)
                    {
                        bool finderFirst = true, finderLast = true;
                        for (int k = 0; k < 11; k++)
                        {
                            if (line[i + k] != ("10111010000"[k] == '1')) finderFirst = false;
                            if (line[i + k] != ("00001011101"[k] == '1')) finderLast = false;
                        }
                        if (finderFirst) score += 40;
                        if (finderLast) score += 40;
                    }
                }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (m[y, x]) dark++;
                    if (x + 1 < size && y + 1 < size && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1])
                        score += 3;
                }
            int total = size * size;
            score += ((Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1) * 10;
            return score;
        }
    }
}
