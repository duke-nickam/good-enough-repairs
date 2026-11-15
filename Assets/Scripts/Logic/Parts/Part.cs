namespace GoodEnoughRepairs
{
    /// <summary>
    /// An installed or stored part: a type and a condition of 0-100 percent.
    /// Immutable. Identity is by reference.
    /// </summary>
    public sealed class Part
    {
        public const int MinCondition = 0;
        public const int MaxCondition = 100;

        public PartType Type { get; private set; }
        public int Condition { get; private set; }

        /// <summary>Condition is clamped to 0-100.</summary>
        public Part(PartType type, int condition)
        {
            Type = type;
            Condition = Clamp(condition);
        }

        /// <summary>True when the condition is at or above the required level.</summary>
        public bool Passes(int requiredLevel)
        {
            return Condition >= requiredLevel;
        }

        public static int Clamp(int condition)
        {
            if (condition < MinCondition) return MinCondition;
            if (condition > MaxCondition) return MaxCondition;
            return condition;
        }
    }
}
