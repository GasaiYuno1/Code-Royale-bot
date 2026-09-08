using System;

namespace Royale
{
    public static class Geom
    {
        public static double Dist(int x1, int y1, int x2, int y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static long Dist2(int x1, int y1, int x2, int y2)
        {
            long dx = x1 - x2, dy = y1 - y2;
            return dx * dx + dy * dy;
        }

        public static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
