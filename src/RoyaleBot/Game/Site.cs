namespace Royale
{
    /// <summary>Сайт: постоянная часть из стартового ввода и состояние на текущий ход.</summary>
    public struct Site
    {
        public int Id, X, Y, Radius;

        // Как во вводе: -1, если сайт не виден (не моя постройка и дальше 300 от моей королевы).
        public int Gold, MaxMineSize;
        // Последнее виденное значение (SiteMemory); -1, если ни разу не видели.
        public int KnownGold, KnownMaxMineSize;

        public StructureType Structure;
        public int Owner;    // -1 ничей, 0 мой, 1 чужой
        public int Param1;   // шахта: доход (-1 если не видно); башня: HP; казарма: ходов до конца тренировки (0 — свободна)
        public int Param2;   // башня: радиус атаки; казарма: тип крипа

        public bool IsEmpty { get { return Structure == StructureType.None; } }
        public bool IsOwn { get { return Owner == 0 && Structure != StructureType.None; } }
        public bool IsEnemy { get { return Owner == 1 && Structure != StructureType.None; } }
        public bool IsOwnTower { get { return IsOwn && Structure == StructureType.Tower; } }
        public bool IsOwnMine { get { return IsOwn && Structure == StructureType.Mine; } }
        public bool BarracksIdle { get { return Structure == StructureType.Barracks && Param1 == 0; } }
        /// <summary>-1 = не видели ни разу, считаем, что золото есть (все сайты стартуют с 200+).</summary>
        public bool MayHaveGold { get { return KnownGold != 0; } }

        public bool IsOwnBarracks(UnitType creep)
        {
            return IsOwn && Structure == StructureType.Barracks && Param2 == (int)creep;
        }

        public override string ToString()
        {
            return "site " + Id + " (" + X + "," + Y + ") r" + Radius + " " + Structure + " owner " + Owner + " p1 " + Param1 + " p2 " + Param2 + " gold " + Gold + "/" + KnownGold;
        }
    }
}
