using System;

namespace Royale
{
    /// <summary>Арифметика как в JVM там, где .NET считает иначе.</summary>
    public static class JavaMath
    {
        /// <summary>
        /// Java Math.round(double) (его использует Kotlin roundToInt): половина вверх, целочисленно по битам,
        /// без ошибки floor(x + 0.5) на 0.49999999999999994. В .NET Math.Round — банковское округление.
        /// </summary>
        public static int Round(double a)
        {
            long bits = BitConverter.DoubleToInt64Bits(a);
            long biasedExp = (bits & 0x7FF0000000000000L) >> 52;
            long shift = (52 - 1 + 1023) - biasedExp;
            if ((shift & -64) == 0)
            {
                long r = (bits & 0x000FFFFFFFFFFFFFL) | 0x0010000000000000L;
                if (bits < 0) r = -r;
                return (int)(((r >> (int)shift) + 1) >> 1);
            }
            return (int)(long)a;
        }
    }
}
