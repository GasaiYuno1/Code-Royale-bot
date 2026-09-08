namespace Royale
{
    public struct UnitInfo
    {
        public int X, Y;
        public int Owner;   // 0 мой, 1 чужой
        public UnitType Type;
        public int Health;

        public bool IsQueen { get { return Type == UnitType.Queen; } }
        public bool IsOwn { get { return Owner == 0; } }

        public override string ToString()
        {
            return Type + " (" + X + "," + Y + ") owner " + Owner + " hp " + Health;
        }
    }
}
