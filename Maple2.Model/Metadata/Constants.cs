// ReSharper disable InconsistentNaming

using Maple2.Model.Enum;

#pragma warning disable IDE1006 // Naming Styles


namespace Maple2.Model.Metadata;

public static class Constant {
    #region custom constants

    public const int ServerMaxCharacters = 8;
    public const int CharacterNameLengthMax = 12;
    public const long MaxMeret = long.MaxValue;
    public const long MaxMeso = long.MaxValue;
    public const long StarPointMax = 999999;
    public const long MesoTokenMax = 100000;
    public const int MaxSkillTabCount = 3;
    public const int BuddyMessageLengthMax = 25;
    public const int GemstoneGrade = 4;
    public const int LapenshardGrade = 3;
    public const int InventoryExpandRowCount = 6;
    public const int DefaultReturnMapId = 2000062; // Lith Harbor
    public const int DefaultHomeMapId = 62000000;  // Private Residence
    public const int DefaultHomeNumber = 1;
    public const byte MinHomeArea = 4;
    public const byte MaxHomeArea = 25;
    public const byte MinHomeHeight = 3;
    public const byte MaxHomeHeight = 15;
    public const short FurnishingStorageMaxSlot = 1024;
    public const int ConstructionCubeItemId = 50200183;
    public const int HomeNameMaxLength = 16;
    public const int HomeMessageMaxLength = 100;
    public const int HomePasscodeLength = 6;
    public const int HomeMaxLayoutSlots = 5;
    public const int PerformanceMapId = 2000064; // Queenstown
    public static readonly TimeSpan MaxPerformanceDuration = TimeSpan.FromMinutes(10);
    public const int BaseStorageCount = 36;
    public const float MesoMarketTaxRate = 0.1f;
    public const float MesoMarketRangeRate = 0.2f;
    public const int MesoMarketSellEndDay = 2;
    public const int MesoMarketListLimit = 5;
    public const int MesoMarketListLimitDay = 5;
    public const int MesoMarketPurchaseLimitMonth = 30;
    public const int MesoMarketPageSize = 50;
    public const int MesoMarketMinToken = 100;
    public const int MesoMarketMaxToken = 1000;
    public const int FishingMasteryMax = 2990;
    public const int PerformanceMasteryMax = 10800;
    public const int MiningMasteryMax = 81440;
    public const int ForagingMasteryMax = 81440;
    public const int RanchingMasteryMax = 81440;
    public const int FarmingMasteryMax = 81440;
    public const int SmithingMasteryMax = 81440;
    public const int HandicraftsMasteryMax = 81440;
    public const int AlchemyMasteryMax = 81440;
    public const int CookingMasteryMax = 81440;
    public const int PetTamingMasteryMax = 100000;
    public const int ChangeAttributesMinLevel = 50;
    public const int ChangeAttributesMinRarity = 4;
    public const int ChangeAttributesMaxRarity = 6;
    public const double FishingMasteryAdditionalExpChance = 0.05;
    public const int FishingMasteryIncreaseFactor = 2;
    public const int FishingRewardsMaxCount = 1;
    public const double FishingItemChance = 0.03;
    public const float FishingBigFishExpModifier = 1.5f;
    public const int MaxMottoLength = 20;
    public const ItemTag BeautyHairSpecialVoucherTag = ItemTag.beauty_hair_special;
    public const ItemTag BeautyHairStandardVoucherTag = ItemTag.beauty_hair;
    public const ItemTag BeautyFaceVoucherTag = ItemTag.beauty_face;
    public const ItemTag BeautyMakeupVoucherTag = ItemTag.beauty_makeup;
    public const ItemTag BeautySkinVoucherTag = ItemTag.beauty_skin;
    public const ItemTag BeautyItemColorVoucherTag = ItemTag.beauty_itemcolor;
    public const int HairPaletteId = 2;
    public const int MaxBuyBackItems = 12;
    public const DayOfWeek ResetDay = DayOfWeek.Thursday;
    public const int PartyMaxCapacity = 10;
    public const int PartyMinCapacity = 4;
    public const int GroupChatMaxCapacity = 20;
    public const int GroupChatMaxCount = 3;
    public const long ClientGraceTimeTick = 500; // max time to allow client to go past loop & sequence end
    public const long MaxNpcControlDelay = 500;
    public const float BlackMarketPremiumClubDiscount = 0.2f;
    public const double PetAttackMultiplier = 0.394;
    public const double AttackDamageFactor = 4; // Unconfirmed
    public const double CriticalConstant = 5.3;
    public const double CriticalPercentageConversion = 0.015;
    public const double MaxCriticalRate = 0.4;
    public const int MaxClubMembers = 10;
    public const string PetFieldAiPath = "Pet/AI_DefaultPetTaming.xml";
    public const string DefaultAiPath = "AI_Default.xml";
    public const int GuildCoinId = 30000861;
    public const int GuildCoinRarity = 4;
    public const int BlueprintId = 35200000;
    public const int EmpowermentNpc = 11003416;
    public const int OpheliaNpc = 11000508;
    public const int PeachyNpc = 11000510;
    public const int InteriorPortalCubeId = 50400158;
    public const int PortalEntryId = 50400190;
    public const int Grade1WeddingCouponItemId = 20303166;
    public const int Grade2WeddingCouponItemId = 20303167;
    public const int Grade3WeddingCouponItemId = 20303168;
    public const int MinStatIntervalTick = 100;
    public const int HomePollMaxCount = 5;
    public const int DummyNpcMale = 2040998;
    public const int DummyNpcFemale = 2040999;
    public const int NextStateTriggerDefaultTick = 100;
    public const int MaxMentees = 3;
    public const long FurnishingBaseId = 2870000000000000000;
    public const bool AllowWaterOnGround = false;
    public const int HomeDecorationMaxLevel = 10;
    public const bool EnableRollEverywhere = false;
    public const bool HideHomeCommands = true;
    public const int MaxAllowedLatency = 2000;
    public const bool DebugTriggers = false; // Set to true to enable debug triggers. (It'll write triggers to files and load triggers from files instead of DB)
    public const bool AllowUnicodeInNames = false; // Allow Unicode characters in character and guild names
    public const bool MailQuestItems = false; // Mail quest item rewards if inventory is full
    public const int MaxClosetMaxCount = 5;
    public const int MaxClosetTabNameLength = 10;
    public const int CharacterNameLengthMin = 2;
    public const int BlockSize = 150;
    public const float SouthEast = 0;
    public const float NorthEast = 90;
    public const float NorthWest = -180;
    public const float SouthWest = -90;
    public const short HairSlotCount = 30;
    public const ShopCurrencyType InitialTierExcessRestockCurrency = ShopCurrencyType.Meso;
    public const float UGCShopProfitFee = 0.25f;
    public const int UGCShopProfitDelayDays = 10;
    public const int PartyFinderListingsPageCount = 12;
    public const int ProposalItemId = 11600482;
    public const int bagSlotTabPetEquipCount = 48;
    public const int MeretAirTaxiPrice = 15;
    public const int ClubMaxCount = 3;

    public static IReadOnlyDictionary<string, int> ContentRewards { get; } = new Dictionary<string, int> {
        {"miniGame", 1005},
        {"dungeonHelper", 1006},
        {"MiniGameType2",1007}, // Shanghai Runners
        {"UserOpenMiniGameExtraReward", 1008}, // Player hosted mini game extra rewards
        {"PrestigeRankUp", 1020},
        {"NormalHardDungeonBonusTier1", 10000001},
        {"NormalHardDungeonBonusTier2", 10000002},
        {"NormalHardDungeonBonusTier3", 10000003},
        {"NormalHardDungeonBonusTier4", 10000004},
        {"NormalHardDungeonBonusTier5", 10000005},
        {"NormalHardDungeonBonusTier6", 10000006},
        {"NormalHardDungeonBonusTier7", 10000007},
        {"NormalHardDungeonBonusTier8", 10000008},
        {"QueenBeanArenaRound1Reward", 10000009},
        {"QueenBeanArenaRound2Reward", 10000010},
        {"QueenBeanArenaRound3Reward", 10000011},
        {"QueenBeanArenaRound4Reward", 10000012},
        {"QueenBeanArenaRound5Reward", 10000013},
        {"QueenBeanArenaRound6Reward", 10000014},
        {"QueenBeanArenaRound7Reward", 10000015},
        {"QueenBeanArenaRound8Reward", 10000016},
        {"QueenBeanArenaRound9Reward", 10000017},
        {"QueenBeanArenaRound10Reward", 10000018},
    };

    public static int DummyNpc(Gender gender) => gender is Gender.Female ? DummyNpcFemale : DummyNpcMale;

    #endregion

    #region Field
    public static readonly TimeSpan FieldUgcBannerRemoveAfter = TimeSpan.FromHours(4);
    public static readonly TimeSpan FieldDisposeLoopInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan FieldDisposeEmptyTime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DungeonDisposeEmptyTime = TimeSpan.FromMinutes(5);
    #endregion

    #region Character
    public static readonly int[] DefaultEmotes = [
        90200011, // Greet
        90200004, // Scheme
        90200024, // Reject
        90200041, // Sit
        90200042, // Ledge Sit
        90200057, // Possessed Fan Dance
        90200043, // Epiphany
        90200022, // Bow
        90200031, // Cry
        90200005, // Dejected
        90200006, // Like
        90200003, // Pout
        90200092, // High Five
        90200077, // Catch of the Day
        90200073, // Make It Rain
        90200023, // Surprise
        90200001, // Anger
        90200019, // Scissors
        90200020, // Rock
        90200021, // Paper
    ];
    #endregion

    #region Account
    public static readonly bool AutoRegister = true;
    public static readonly bool BlockLoginWithMismatchedMachineId = false;
    public static readonly int DefaultMaxCharacters = 4;
    #endregion

    #region XML table/constants.xml
    public const float NPCCliffHeight = 50.0f;
    public const float CustomizingRotationSpeed = 75.0f;
    public const bool AllowComboAtComboPoint = true;
    public const int AttackRotationSpeed = 90;
    public const int ChaosModeTime = 20;
    public const int ChaosPointPerBlock = 20;
    public const int ChaosPointMaxBlock = 1;
    public const int ChaosPointGetLevel0 = 1;
    public const int ChaosPointGetPoint0 = 120;
    public const int ChaosActionGetLevel0 = 15;
    public const int ChaosActionGetLevel1 = 25;
    public const int ChaosActionGetLevel2 = 55;
    public const int ChaosActionGetLevel3 = 95;
    public const int ChaosActionGetLevel4 = 145;
    public const int OnEnterTriggerClientSideOnlyTick = 100;
    public const int OnEnterTriggerDefaultTick = 1000;
    public const int TalkTimeover = 60000;
    public const int DropMoneyActiveProbability = 0;
    public const int DropMoneyProbability = 0;
    public const int OffsetPcMissionIndicator = 20;
    public const int questHideTime = 30;
    public const int questIntervalTime = 60;
    public const int ShopResetChance = 10;
    public const int DashKeyInputDelay = 500;
    public const int DashSwimConsumeSP = 20;
    public const int DashSwimMoveVel = 2;
    public const float Glide_Gravity = 0.0f;
    public const float Glide_Height_Limit = 0.0f;
    public const float Glide_Horizontal_Accelerate = 0.0f;
    public const int Glide_Horizontal_Velocity = 500;
    public const float Glide_Vertical_Accelerate = 0.0f;
    public const int Glide_Vertical_Velocity = 150;
    public const int Glide_Vertical_Vibrate_Amplitude = 300;
    public const float Glide_Vertical_Vibrate_Frequency = 1500.0f;
    public const bool Glide_Effect = true;
    public const string Glide_Effect_Run = "CH/Common/Eff_Fly_Balloon_Run.xml";
    public const string Glide_Effect_Idle = "CH/Common/Eff_Fly_Balloon_Idle.xml";
    public const string Glide_Ani_Idle = "Fly_Idle_A";
    public const string Glide_Ani_Left = "Gliding_Left_A";
    public const string Glide_Ani_Right = "Gliding_Right_A";
    public const string Glide_Ani_Run = "Fly_Run_A";
    public const int ConsumeCritical = 5;
    public const int DayToNightTime = 10000;
    public const float myPCdayTiming = 0.5f;
    public const float myPCNightTiming = 0.5f;
    public const float BGMTiming = 0.5f;
    public const int dayBaseMinute = 1;
    public const int dayMinute = 1439;
    public const int nightMinute = 1;
    public const int QuestRewardSkillSlotQuestID1 = 1010002;
    public const int QuestRewardSkillSlotQuestID2 = 1010003;
    public const int QuestRewardSkillSlotQuestID3 = 1010004;
    public const int QuestRewardSkillSlotQuestID4 = 1010005;
    public const int QuestRewardSkillSlotQuestID5 = 1010010;
    public const int QuestRewardSkillSlotItemID1 = 40000000;
    public const int QuestRewardSkillSlotItemID2 = 40200001;
    public const int QuestRewardSkillSlotItemID3 = 20000001;
    public const int QuestRewardSkillSlotItemID4 = 40000055;
    public const int QuestRewardSkillSlotItemID5 = 40000056;
    public const int autoTargetingMaxDegree = 210;
    public const float BossHitVibrateFreq = 10.0f;
    public const float BossHitVibrateAmp = 5.5f;
    public const float BossHitVibrateDamping = 0.7f;
    public const float BossHitVibrateDuration = 0.1f;
    public const int OneTimeWeaponItemID = 15000001;
    public const int ModelHouse = 62000027;
    public const int UsingNoPhysXModelUserCount = 10;
    public const int UsingNoPhysXModelActorCount = 10;
    public const int UsingNoPhysXModelJointCount = 10;
    public const bool EnableSoundMute = true;
    public const int BossKillSoundRange = 1500;
    public const int monsterPeakTimeNotifyDuration = 300;
    public const int AirTaxiItemID = 20300003;
    public const int ShowNameTagSellerTitle = 10000153;
    public const int ShowNameTagChampionTitle = 10000152;
    public const int ShowNameTagTrophy1000Title = 10000170;
    public const int ShowNameTagTrophy2000Title = 10000171;
    public const int ShowNameTagTrophy3000Title = 10000172;
    public const int ShowNameTagArchitectTitle = 10000158;
    public const int characterMaxLevel = 99; // Updated
    public const int OneShotSkillID = 19900061;
    public const int FindDungeonHelpEasyDungeonLevel = 50;
    public const int FameContentsRequireQuestID = 91000013;
    public const int FameExpedContentsRequireQuestID = 50101050;
    public const int SurvivalScanAdditionalID = 71000052;
    public const int MapleSurvivalTopNRanking = 5;
    public const string MapleSurvivalSeasonRewardUrl =
        "http://maplestory2.nexon.net/en/news/article/32249/mushking-royale-championship-rewards";
    public const int HoldAttackSkillID = 10700252;
    public const string DiscordAppID = "555204064091045904";
    public const int GuildFundMax = 20000;
    public const int DropIconVisibleDistance = 400;
    public const string MesoMarketTokenDetailUrl = "http://maplestory2.nexon.net/en/news/article/45213";
    #endregion
}

#pragma warning restore IDE1006 // Naming Styles
