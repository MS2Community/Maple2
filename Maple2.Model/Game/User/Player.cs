using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;

namespace Maple2.Model.Game;

public class Player {
    public readonly Account Account;
    public readonly Character Character;
    public readonly int ObjectId;

    public required Home Home { get; set; }
    public required Currency Currency { get; init; }
    public required Unlock Unlock { get; init; }

    public Player(Account account, Character character, int objectId) {
        Account = account;
        Character = character;
        ObjectId = objectId;
    }
}

public class Unlock {
    public DateTime LastModified { get; set; }

    public IDictionary<InventoryType, short> Expand { get; init; } = new Dictionary<InventoryType, short>();
    public short HairSlotExpand;

    public readonly ISet<int> Maps = new SortedSet<int>();
    public readonly ISet<int> Taxis = new SortedSet<int>();
    public readonly ISet<int> Titles = new SortedSet<int>();
    public readonly IList<int> Emotes = new List<int>();
    public readonly IDictionary<int, long> StickerSets = new Dictionary<int, long>();
    public readonly IDictionary<int, bool> MasteryRewardsClaimed = new Dictionary<int, bool>();
    public readonly IDictionary<int, DungeonRankReward> DungeonRankRewards = new Dictionary<int, DungeonRankReward>();
    public readonly IDictionary<int, short> Pets = new SortedDictionary<int, short>();
    public readonly IDictionary<int, FishEntry> FishAlbum = new Dictionary<int, FishEntry>();

    // Used for trophies
    public readonly ISet<int> InteractedObjects = new SortedSet<int>();
    public readonly IDictionary<int, byte> CollectedItems = new Dictionary<int, byte>();
}

public class Currency {
    public long Meret;
    public long GameMeret;
    public long Meso;
    public long EventMeret;
    public long ValorToken;
    public long Treva;
    public long Rue;
    public long HaviFruit;
    public long ReverseCoin;
    public long MentorToken;
    public long MenteeToken;
    public long StarPoint;
    public long MesoToken;
};
