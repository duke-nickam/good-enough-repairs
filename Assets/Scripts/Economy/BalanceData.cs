using System;

namespace GoodEnoughRepairs
{
    // Mirrors Assets/StreamingAssets/balance.json key for key.
    // Plain data only: no Unity types, no game rules.

    [Serializable]
    public class PaymentRange
    {
        public int min;
        public int max;
    }

    [Serializable]
    public class JunkyardBalance
    {
        public int price;
        public int usableConditionMin;
        public int usableConditionMax;
    }

    [Serializable]
    public class DayBalance
    {
        public int day;
        public string type;
        public int ships;
        public int countdownSeconds;
        public int shipEverySeconds;
        public int junkParts;
        public int weakPartsMin;
        public int weakPartsMax;
        public int fighterWeakConditionMin;
        public int fighterWeakConditionMax;
        public int freighterWeakConditionMin;
        public int freighterWeakConditionMax;
        public int healthyConditionMin;
        public int healthyConditionMax;
    }

    [Serializable]
    public class BalanceData
    {
        public int startingCredits;
        public int newPartPrice;
        public int penaltyPercent;
        public int scrapPrice;
        public int upkeep;
        public int playerActionMs;
        public int shipTurnoverMs;
        public int dayStartLandingMs;
        public int slotsPerShip;
        public int requiredFreighterPercent;
        public int requiredFighterPercent;
        public PaymentRange freighterPayment;
        public PaymentRange fighterPayment;
        public JunkyardBalance junkyard;
        public DayBalance[] days;
    }
}
