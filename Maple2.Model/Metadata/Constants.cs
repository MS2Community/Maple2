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
    public const int MaxBuddyCount = 100;
    public const int MaxBlockCount = 100;
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

    public static readonly TimeSpan WorldBossIdleWarningThreshold = TimeSpan.FromMinutes(4);
    public static readonly TimeSpan WorldBossDespawnThreshold = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan WorldBossMonitorInterval = TimeSpan.FromSeconds(30);

    public const int MaxMentees = 3;

    public const long FurnishingBaseId = 2870000000000000000;
    public const bool AllowWaterOnGround = false;

    public const int HomeDecorationMaxLevel = 10;

    public const bool EnableRollEverywhere = false;
    public const bool HideHomeCommands = true;

    public const int MaxAllowedLatency = 2000;

    public const bool DebugTriggers = false; // Set to true to enable debug triggers. (It'll write triggers to files and load triggers from files instead of DB)

    public const bool AllowUnicodeInNames = false; // Allow Unicode characters in character and guild names

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

    public const bool MailQuestItems = false; // Mail quest item rewards if inventory is full

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

    #endregion

    #region client constants
    public const int MaxClosetMaxCount = 5;
    public const int MaxClosetTabNameLength = 10;
    public const int WeddingProposeItemID = 11600482;
    public const int WeddingInvitationMaxCount = 70;
    public const int WeddingProposeCooltime = 2;
    public const int WeddingDivorceFieldID = 84000002;
    public const int WeddingInvitationMeso = 1000;
    public const int WeddingDivorceMeso = 1000000;
    public const int WeddingCoolingOffDay = 7;
    public const int WeddingPromiseLimitDay = 7;
    public const int WeddingHallModifyLimitHour = 3;
    public const int WeddingDivorceRequireMarriageDay = 30;
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
    #endregion

    #region table/constants.xml

    public const float NPCColorScale = 2.0f;
    public const float NPCDuration = 0.2f;
    public const float PCColorScale = 2.0f;
    public const float PCDuration = 0.2f;
    public const float GetEXPColorScale = 0.5f;
    public const float GetEXPDuration = 0.2f;
    public const float AccumulationRatio = 0.1f;
    public const float NPCCliffHeight = 50.0f;
    public const float NPCRandomDeadPushRate = 0.2f;
    public const float CustomizingRotationSpeed = 75.0f;
    public const float CustomizingWheelSpeed_Morph = 0.1f;
    public const float CustomizingWheelSpeed_Item = 0.1f;
    public const float CustomizingWheelSpeed_Makeup = 0.1f;
    public const float CustomizingRotationSpeed_Makeup = 1.0f;
    public const float CustomizingHairFirstPartHighlight = 0.1f;
    public const float CustomizingHairSecondPartHighlight = 1.0f;
    public const float LookAtInterval = 15.0f;
    public const float LookAtDistanceNPC = 500.0f;
    public const float LookAtDistanceCry = 500.0f;
    public const bool EnableSkillJumpDown = true;
    public const bool EscapeHitMethodSkill = false;
    public const bool EscapeHitMethodJump = false;
    public const bool EscapeHitMethodMove = false;
    public const bool EscapeHitMoveKeyIsDown = false;
    public const bool AllowComboAtComboPoint = true;
    public const bool CancelSwing_KeyIsDown = true;
    public const bool SkillGlobalCooldown = false;
    public const bool SkillGlobalCooldown_CheckSameSkill = true;
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
    public const int DamageDistance = 2500;
    public const int TalkableDistance = 150;
    public const bool TalkableFrontOnly = true;
    public const int DropIconVisibleDistance = 400;
    public const int ChatBalloonDistance = 2000;
    public const int HpBarDistance = 9999999;
    public const int EmoticonVisibleDistance = 2500;
    public const int RegisterUgcDistance = 150;
    public const int RegisterUgcDistanceClose = 300;
    public const int ConstructUgcDistance = 150;
    public const int FunctionCubeDistance = 125;
    public const int InteractionDistance = 155;
    public const int HouseMarkShowDistance = 2000;
    public const int HouseMarkShowClippingUp = 1000;
    public const int HouseMarkShowClippingDown = 500;
    public const int HouseMarkPopupDistance = 160;
    public const int UgcBoundaryStartDistance = 1;
    public const int UgcBoundaryEndDistance = 7;
    public const int DurationForBoundaryDisplay = 3000;
    public static TimeSpan UgcHomeSaleWaitingTime = TimeSpan.FromSeconds(259200);
    public const int UgcContainerExpireDurationNormal = 90;
    public const int UgcContainerExpireDurationCash = 365;
    public const int UgcContainerExpireDurationMeret = 365;
    public const int UgcHomeExtensionNoticeDate = 30;
    public const int UgcHomePasswordExpireDuration = 86400;
    public const bool CubeLiftHeightLimitUp = true;
    public const bool CubeLiftHeightLimitDown = true;
    public const int CubeCraftSafetyCapID = 11300053;
    public const int CubeCraftLightStickLeftID = 13100014;
    public const int CubeCraftLightStickRightID = 13100046;
    public const float DropIconDistance = 200.0f;
    public const int DropIconHeadOffset = 40;
    public const int DropItemMaxLength = 300;
    public const int DropMoneyMaxLength = 300;
    public const float DropItemTargetZPos = 200.0f;
    public const float DropItemPickUpVel = 200.0f;
    public const float DropItemPickUpGravity = -38.0f;
    public const float DropItemPickUpCompleteRotateTime = 0.1f;
    public const int DropItemPickUpCompleteRotateVel = 5;
    public const int DropMoneyActiveProbability = 0;
    public const int DropMoneyProbability = 0;
    public const int ChatBalloonDuration = 5000;
    public const int BoreWaitingTick = 50000;
    public const int OffsetPcHpBar = 32;
    public const int OffsetPcNameTag = 30;
    public const int OffsetPcChatBalloon = -30;
    public const int OffsetPcDamageNumber = 0;
    public const int OffsetPcMissionIndicator = 20;
    public const int OffsetPcProfileTag = 0;
    public const float fOffsetOnTombstoneNameTag = -5.0f;
    public const int OffsetNpcHpBar = 5;
    public const int OffsetNpcNameTag = 5;
    public const int OffsetNpcEmoticon = -30;
    public const int OffsetNpcChatBalloon = -30;
    public const int OffsetNpcDamageNumber = 0;
    public const int OffsetNpcMonologue = 40;
    public const int OffsetActionTooltipX = 70;
    public const int OffsetActionTooltipY = -40;
    public const int OffsetPcPopupMenu = 60;
    public const int DamageGap = 30;
    public const int DamageRenderCount = 3;
    public const int DamageRenderTotalCount = 25;
    public const float DamageOtherScale = 0.5f;
    public const float DamageOtherAlpha = 0.3f;
    public const int DamageEffectMinHPPercent = 30;
    public const int DamageEffectCriticalPercent = 10;
    public const int questHideTime = 30;
    public const int questIntervalTime = 60;
    public const int ShopResetChance = 10;
    public const int ShopSeedResetTime = 60;
    public const int ShopRepurchaseMax = 12;
    public const int ShopSellConfirmPrice = 10000;
    public const int ShopBuyConfirmPrice = 0;
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
    public const float ClimbVelocityV = 3.0f;
    public const float ClimbVelocityH = 1.5f;
    public const int StoreExpandMaxSlotCount = 144;
    public const int StoreExpandPrice1Row = 330;
    public const int StoreDepositMax = 2000000000;
    public const int StoreWithdrawMax = 2000000000;
    public const int CameraExtraMoveScaleByMonster = 3;
    public const int CameraExtraMoveScaleByMap = 2;
    public const int CameraExtraDistance = 200;
    public const float CameraFinalLoose = 0.08f;
    public const float CameraCurrentLoose = 0.002f;
    public const float CameraUpdateLoose = 0.03f;
    public const int CameraVelocityInPortalMove = 6000;
    public const int ConsumeCritical = 5;
    public const int MonologueInterval = 15;
    public const int MonologueRandom = 10;
    public const int MonologueShowTime = 5;
    public const int ShowKillCountMin = 3;
    public const int UserRevivalInvincibleTick = 5000;
    public const int UserRevivalPenaltyPercent = 15;
    public const string UserRevivalIconPath = "./data/resource/image/skill/icon/deathPenalty.png";
    public const string UserRevivalInvincibleIconPath = "./data/resource/image/skill/icon/deathInvincible.png";
    public const int GetExpMinVelocity = 250;
    public const int GetExpVelocityPer1Length = 2;
    public const string GetExpControlValue0 = "-0.5,0,0.25";
    public const string GetExpControlValue1 = "0.5,-0.25,0.5";
    public const string GetExpTargetPCDummyName = "Eff_Body";
    public const float GetExpTimeAcceleration = 1.02f;
    public const float GetExpCollisionRadius = 15.0f;
    public const int DayToNightTime = 10000;
    public const float MyPCDayTiming = 0.5f;
    public const float MyPCNightTiming = 0.5f;
    public const float BGMTiming = 0.5f;
    public const int dayBaseMinute = 1;
    public const int dayMinute = 1439;
    public const int nightMinute = 1;
    public const int SkipFrameGameObject = 5;
    public const int SkipFrameDistanceGameObject = 2000;
    public const float RegionSkillFadeOutDuration = 0.3f;
    public const int PassengerProfileImageSize = 50;
    public const int PassengerProfileImageLifeTime = 3;
    public const int PassengerProfileImageShowNumber = 3;
    public const int PassengerProfileImageShowCooldown = 57;
    public const int PassengerProfileImageShowCooldownParty = 57;
    public const int PassengerProfileImageShowRange = 400;
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
    public const int UGCCameraDefaultSize = 320;
    public const int UGCCameraMinSize = 160;
    public const int UGCCameraMaxSize = 640;
    public const int UGCCameraSnapshotPreviewTime = 3000;
    public const int UGCImgUploadSizeLimit = 1024;
    public const int UGCImgFileCountCheck = 200;
    public const int WindAmp2Cloak = 1500;
    public const float WindPeriod2Cloak = 0.7f;
    public const float WindPeriodVar2Cloak = 0.4f;
    public const int autoTargetingMaxDegree = 210;
    public const float VolumeMyPcToNpc = 1.0f;
    public const float VolumeMyPcToObject = 0.5f;
    public const float VolumeMyPcToBreakableObject = 0.8f;
    public const float VolumeNpcToMyPc = 0.7f;
    public const float VolumePcToNpc = 0.3f;
    public const float VolumePcToBreakableObject = 0.3f;
    public const float VolumeNpcToPc = 0.5f;
    public const float VolumeOtherPc = 0.9f;
    public const int ItemDropLevelMaxBoundary = 1;
    public const float moneyTreeDropHeight = 300.0f;
    public const float moneyTreeDropBase = 150.0f;
    public const int moneyTreeDropRandom = 200;
    public const int WhisperIgnoreTime = 1000;
    public const int WhisperMaxCount = 3;
    public const int WhisperDurationTime = 3000;
    public const float BossHitVibrateFreq = 10.0f;
    public const float BossHitVibrateAmp = 5.5f;
    public const float BossHitVibrateDamping = 0.7f;
    public const float BossHitVibrateDuration = 0.1f;
    public const float BossHpBarAutoDetectRange = 1500.0f;
    public const float BossHpBarDuration = 5.0f;
    public const float FindHoldTargetRange = 230.0f;
    public const int FindGrabNodeRange = 2000;
    public const string UgcShopCharCameraLookat = "0,0,70";
    public const string UgcShopCharCameraPos = "220,0,0";
    public const int UgcShopCharCameraMinDistance = 150;
    public const int UgcShopCharCameraZoomVelocity = 700;
    public const string UgcShopCubeCameraLookat = "0,0,80";
    public const string UgcShopCubeCameraPos = "420,0,350";
    public const int UgcShopCubeCameraMinDistance = 450;
    public const int UgcShopCubeCameraZoomVelocity = 700;
    public const string UgcShopRideeCameraLookat = "10,-5,50";
    public const string UgcShopRideeCameraPos = "275,0,150";
    public const int UgcShopRideeCameraMinDistance = 250;
    public const int UgcShopRideeCameraZoomVelocity = 700;
    public const int FieldCachingCount = 2;
    public const float FieldCachingTime = 300.0f;
    public const int FieldCachingMaxCount = 4;
    public const int FieldUnloadThreshold = 10;
    public const float EffectLODOneStepDistance = 450.0f;
    public const float EffectLODTwoStepDistance = 500.0f;
    public const float EffectLODThreeStepDistance = 550.0f;
    public const int TelescopeFindDistance = 200;
    public const int BoatPrice = 500;
    public const int QuestGuidePageCount = 3;
    public const int QuestGuideMaxCount = 60;
    public const float CameraInterpolationTime = 0.4f;
    public const int OneTimeWeaponItemID = 15000001;
    public const int TransparencyCP = 11399999;
    public const int TransparencyEY = 11199999;
    public const int TransparencyCL = 11499999;
    public const int TransparencyPA = 11599999;
    public const int TransparencyMT = 11899999;
    public const int TransparencyEA = 11299999;
    public const int TransparencyFH = 11099999;
    public const int TransparencyGL = 11699999;
    public const int TransparencyRI = 12099999;
    public const int TransparencySH = 11799999;
    public const float DefaultDropItemAlpha = 0.3f;
    public const float DropItemPickFailHeight = 50.0f;
    public const float DropItemPickFailTime = 0.3f;
    public const int TaxiStationFindDistance = 200;
    public const int TaxiCallDuration = 3000;
    public const int TaxiCallBestDriverDuration = 1000;
    public const int TaxiCallBestDriverLevel = 25;
    public const int AirTaxiCashCallDuration = 500;
    public const int AirTaxiMesoCallDuration = 3000;
    public const int TradeRequestDuration = 20;
    public const int UserPortalInvincibleTick = 5000;
    public const string UserPortalInvincibleIconPath = "./data/resource/image/skill/icon/deathInvincible.png";
    public const int SummonRideeDuration = 1000;
    public const int WorldMapAdjustTileX = 0;
    public const int WorldMapAdjustTileY = 0;
    public const float TimeScalePCScale = 0.1f;
    public const float TimeScalePCDuration = 1.0f;
    public const int GoToHomeCastingTime = 0;
    public const int returnHomeSkill = 100000000;
    public const int returnHomeSkillMeret = 100000013;
    public const int TutorialIntroSkipTime = 5;
    public const string AvatarDefaultItemMale = "10200032,10300198";
    public const string AvatarDefaultItemFemale = "10200033,10300199";
    public const int ModelHouse = 62000027;
    public const int TalkCooldown = 1000;
    public const int AddressPopupDuration = 3000;
    public const int MaxFPS = 120;
    public const int UGCShopSellMinPrice = 150;
    public const int UGCShopSellMaxPrice = 3000;
    public const int UGCShopSaleDay = 90;
    public const int UGCShopAdFeeMeret = 30;
    public const int UGCShopAdHour = 72;
    public const int UGCShopSellingRestrictAmount = 200000;
    public const int MeretMarketHomeBannerShowTick = 6000;
    public const int BlackMarketSellMinPrice = 100;
    public const int BlackMarketSellMaxPrice = 500000000;
    public const int BlackMarketSellEndDay = 2;
    public const int ItemTransferBlackMarketGrade = 4;
    public const int UgcBannerCheckTime = 4;
    public const int FastChat_CheckTime = 2000;
    public const int FastChat_CheckCount = 5;
    public const int SameChat_CheckTime = 3000;
    public const int SameChat_CheckCount = 5;
    public const int SameChat_RestrictTime = 10000;
    public const int FastChat_RestrictTime = 30000;
    public const int RestrictChat_AddRestrictTime = 10000;
    public const int AccumWarning_AddRestrictTime = 60000;
    public const int RestrictWarning_ReleaseTime = 10000;
    public const int MaxChatLength = 100;
    public const int UsingNoPhysXModelUserCount = 10;
    public const int UsingNoPhysXModelActorCount = 10;
    public const int UsingNoPhysXModelJointCount = 10;
    public const int EmotionBoreAnimProbability = 100;
    public const float FallMoveSpeed = 1.0f;
    public const int GuildCreatePrice = 2000;
    public const int GuildCreateMinLevel = 0;
    public const int GuildNameLengthMin = 2;
    public const int GuildNameLengthMax = 25;
    public const int guildFundMax = 20000;
    public const float guildFundRate = 0.1f;
    public const int guildExpMaxCountForPlayTime = 2;
    public const int guildDonateMeso = 10000;
    public const string mirrorGuideMoviePath = "Common/Customize_Hat.usm";
    public const string hairGuideMoviePath = "Common/Customize_Hair.usm";
    public const string makeUpGuideMoviePath = "Common/Customize_MakeUp.usm";
    public const int FastShimmerRadius = 600;
    public const int FastShimmerHeight = 450;
    public const int SmartRecommendNotify_DurationTick = 15000;
    public const int BootyPopupDuration = 3000;
    public const bool EnableSoundMute = true;
    public const int BossKillSoundRange = 1500;
    public const string charCreateGuideMoviePath = "Common/Customize_Intro.usm";
    public const int monsterPeakTimeNotifyDuration = 300;
    public const int KeyIsDownSkill_MaxDurationTick = 30000;
    public const int shadowWorldBuffHpUp = 70000027;
    public const int shadowWorldBuffMoveProtect = 70000032;
    public const int AirTaxiItemID = 20300003;
    public const int PeriodOfMaidEmployment = 30;
    public const int MaidReadyToPay = 7;
    public const int MaidAffinityMax = 10;
    public const int MeretRevivalDebuffCode = 100000001;
    public const float MeretRevivalFeeReduceLimit = 0.5f;
    public const int MeretConsumeWorldChat = 30;
    public const int MeretConsumeChannelChat = 3;
    public const int MeretConsumeSuperChat = 200;
    public const int pvpBtiRewardItem = 90000006;
    public const int pvpBtiRewardWinnerCount = 30;
    public const int pvpBtiRewardLoserCount = 10;
    public const int PvpFFAReward1Count = 30;
    public const int PvpFFAReward2Count = 25;
    public const int PvpFFAReward3Count = 20;
    public const int PvpFFAReward4Count = 15;
    public const int PvpFFAReward5Count = 15;
    public const int PvpFFAReward6Count = 15;
    public const int PvpFFAReward7Count = 15;
    public const int PvpFFAReward8Count = 10;
    public const int PvpFFAReward9Count = 10;
    public const int PvpFFAReward10Count = 10;
    public const int PvpFFARewardItem = 90000006;
    public const int PvpFFAAdditionRewardRate = 0;
    public const int MailExpiryDays = 30;
    public const int WorldMapBossTooltipCount = 30;
    public const int ShowNameTagEnchantItemGrade = 4;
    public const int ShowNameTagEnchantLevel = 12;
    public const int BossNotifyAbsLevel = 1;
    public const int RoomExitWaitSecond = 10;
    public const int AdditionalMesoMaxRate = 7;
    public const int AdditionalExpMaxRate = 9;
    public const int HonorTokenMax = 30000;
    public const int KarmaTokenMax = 75000;
    public const int LuTokenMax = 2000;
    public const int HaviTokenMax = 35000;
    public const int ReverseCoinMax = 2000;
    public const int MentorTokenMax = 10000; // From KMS
    public const int MenteeTokenMax = 35000; // From KMS
    public const int CharacterDestroyDivisionLevel = 20;
    public const int CharacterDestroyWaitSecond = 86400;
    public const int BossShimmerScaleUpActiveDistance = 5000;
    public const float BossShimmerScaleUpSize = 3.0f;
    public const int ShowNameTagSellerTitle = 10000153;
    public const int ShowNameTagChampionTitle = 10000152;
    public const int ShowNameTagTrophy1000Title = 10000170;
    public const int ShowNameTagTrophy2000Title = 10000171;
    public const int ShowNameTagTrophy3000Title = 10000172;
    public const int ShowNameTagArchitectTitle = 10000158;
    public const float SwimDashSpeed = 5.4f;
    public const int UserTriggerStateMax = 10;
    public const int UserTriggerEnterActionMax = 3;
    public const int UserTriggerConditionMax = 3;
    public const int UserTriggerConditionActionMax = 3;
    public const int PCBangAdditionalEffectID = 100000006;
    public const int PCBangAdditionalEffectExp = 1;
    public const int PCBangAdditionalEffectMeso = 2;
    public const int PCBangItemDefaultPeriod = 1440;
    public const int ShadowWorldAutoReviveDeadAction = 1;
    public const int GoodInteriorRecommendUICloseTime = 15;
    public const string UGCInfoDetailViewPage = "http://www.nexon.net/en/legal/user-generated-content-policy";
    public const int UGCInfoStoryBookID = 39000038;
    public const int HomePasswordUsersKickDelay = 10;
    public const string TriggerEditorHelpURL = "http://maplestory2.nexon.net/en/news/article/32326";
    public const int QuestRewardSAIgnoreLevel = 10;
    public const int RecallCastingTime = 3000;
    public const int PartyRecallMeret = 30;
    public const float CashCallMedicLeaveDelay = 0.5f;
    public const int characterMaxLevel = 99; // Updated
    public const int DropSPEPBallMaxLength = 300;
    public const int DropSPEPBallTargetZPos = 100;
    public const int DropSPEPBallPickUpVel = 250;
    public const int DropSPEPBallPickUpGravity = -120;
    public const float DropSPEPBallPickUpCompleteRotateTime = 0.05f;
    public const int DropSPEPBallPickUpCompleteRotateVel = 5;
    public const int EnchantItemBindingRequireLevel = 1;
    public const int enchantSuccessBroadcastingLevel = 12;
    public const int EnchantEquipIngredientMaxCount = 1000;
    public const int EnchantFailStackUsingMaxCount = 100;
    public const int EnchantFailStackTakeMaxCount = 1000;
    public const int EnchantEquipIngredientOpenLevel = 11;
    public const int EnchantEquipIngredientOpenRank = 4;
    public const int EnchantEquipIngredientMaxSuccessProb = 3000;
    public const int EnchantFailStackOpenLevel = 1;
    public const int EnchantFailStackTakeMaxSuccessProb = 10000;
    public const int BankCallDuration = 500;
    public const string NoticeDialogUrl = "http://nxcache.nexon.net/maplestory2/ingame-banners/index.html";
    public const string NoticeDialogUrlPubTest = "maview:/Game/BannerTest";
    public const int NoticeDialogOpenSeconds = 5000;
    public const int RemakeOptionMaxCount = 10;
    public const int FisherBoreDuration = 10000;
    public const string fishingStartCastingBarText0 = "s_fishing_start_castingbar_text0";
    public const string fishingStartCastingBarText1 = "s_fishing_start_castingbar_text1";
    public const string fishingStartCastingBarText2 = "s_fishing_start_castingbar_text2";
    public const string fishingStartCastingBarText3 = "s_fishing_start_castingbar_text3";
    public const string fishingStartCastingBarText4 = "s_fishing_start_castingbar_text4";
    public const string fishingStartBalloonText0 = "s_fishing_start_balloon_text0";
    public const string fishingStartBalloonText1 = "s_fishing_start_balloon_text1";
    public const string fishingStartBalloonText2 = "s_fishing_start_balloon_text2";
    public const string fishingStartBalloonText3 = "s_fishing_start_balloon_text3";
    public const string fishingStartBalloonText4 = "s_fishing_start_balloon_text4";
    public const string fishingStartBalloonText5 = "s_fishing_start_balloon_text5";
    public const string fishingStartBalloonText6 = "s_fishing_start_balloon_text6";
    public const string fishingStartBalloonText7 = "s_fishing_start_balloon_text7";
    public const string fishingStartBalloonText8 = "s_fishing_start_balloon_text8";
    public const string fishingStartBalloonText9 = "s_fishing_start_balloon_text9";
    public const string fishFightingCastingBarText0 = "s_fishing_fishfighting_castingbar_text0";
    public const string fishFightingBalloonText0 = "s_fishing_fishfighting_balloon_text0";
    public const string fishFightingBalloonText1 = "s_fishing_fishfighting_balloon_text1";
    public const string fishFightingBalloonText2 = "s_fishing_fishfighting_balloon_text2";
    public const string fishFightingBalloonText3 = "s_fishing_fishfighting_balloon_text3";
    public const string fishFightingBalloonText4 = "s_fishing_fishfighting_balloon_text4";
    public const string fishFightingBalloonText5 = "s_fishing_fishfighting_balloon_text5";
    public const int WorldMapSpecialFunctionNpcID0 = 11001276;
    public const string WorldMapSpecialFunctionNpcFrame0 = "airship_enabled";
    public const string WorldMapSpecialFunctionNpcTooltip0 = "s_worldmap_special_function_npc0";
    public const int WorldMapSpecialFunctionNpcID1 = 11001403;
    public const string WorldMapSpecialFunctionNpcFrame1 = "airship_enabled";
    public const string WorldMapSpecialFunctionNpcTooltip1 = "s_worldmap_special_function_npc0";
    public const int WarpOpenContinent0 = 102;
    public const int WarpOpenContinent1 = 103;
    public const int WarpOpenContinent2 = 202;
    public const int WarpOpenContinent3 = 105;
    public const string WriteMusicDetailWebPage = "http://maplestory2.nexon.net/en/news/article/32329";
    public const int WriteMusicStoryBookID = 39000047;
    public const int MusicListenInRadius = 900;
    public const int MusicListenOutRadius = 2200;
    public const int DungeonRoomMaxRewardCount = 99;
    public const int DungeonMatchRecommendPickCount = 6;
    public const int DungeonSeasonRankMinLevel = 99;
    public const int LimitMeretRevival = 1;
    public const int MinimapScaleSkipDuration = 5000;
    public const int MinimapScaleSkipSplitPixel = 20;
    public const int TradeMinMeso = 100;
    public const int TradeMaxMeso = 500000000;
    public const int TradeFeePercent = 20;
    public const int DailyMissionRequireLevel = 50;
    public const int MesoMarketBasePrice = 5000000;
    public const int MesoMarketProductUnit0 = 5000000;
    public const int MesoMarketBuyPayType = 16;
    public const int MesoMarketIconType = 0;
    public const string MesoMarketTokenDetailUrl = "http://maplestory2.nexon.net/en/news/article/45213";
    public const int BeautyHairShopGotoFieldID = 52000008;
    public const int BeautyHairShopGotoPortalID = 1;
    public const int BeautyColorShopGotoFieldID = 52000009;
    public const int BeautyColorShopGotoPortalID = 1;
    public const int BeautyFaceShopGotoFieldID = 52000010;
    public const int BeautyFaceShopGotoPortalID = 1;
    public const int BeautyStyleExpandSlotPrice = 980;
    public const int BeautyStyleMaxSlotCount = 0;
    public const int BeautyStyleDefaultSlotCount = 30;
    public const int BeautyStyleExpandSlotCount1time = 3;
    public const string CashShopFigureAddressPage = "http://maplestory2.nexon.com/cashshop/address";
    public const int NxaCashChargeWebPageWidth = 650;
    public const int NxaCashChargeWebPageHeight = 650;
    public const int ItemUnLockTime = 259200;
    public const int PropertyProtectionTime = 60;
    public const string TencentSecurityWebPage = "http://mxd2.qq.com/safe/index.shtml";
    public const int HomeBankCallDuration = 1000;
    public const int HomeBankCallCooldown = 30000;
    public const string HomeBankCallSequence = "Object_React_A";
    public const int HomeDoctorCallDuration = 1000;
    public const int HomeDoctorCallCooldown = 30000;
    public const string HomeDoctorCallSequence = "EmergencyHelicopter_A";
    public const int HomeDoctorNpcID = 11001668;
    public const int HomeDoctorScriptID0 = 1;
    public const int HomeDoctorScriptID1 = 10;
    public const int EnchantMasterScriptID = 31;
    public const int RestExpAcquireRate = 10000;
    public const int RestExpMaxAcquireRate = 100000;
    public const int ApartmentPreviewRequireLevel = 50;
    public const int ApartmentPreviewRequireQuestID = 90000060;
    public const int KeyboardGuideShowLevel = 13;
    public const int extendAutoFishMaxCount = 8;
    public const int extendAutoPlayInstrumentMaxCount = 8;
    public const int ResetShadowBuffMeret = 100;
    public const int InventoryExpandPrice1Row = 390;
    public const int VIPServicePeriodLimitDay = 100000000;
    public const int VIPMarketCommissionSale = 20;
    public const int BreedDuration = 767;
    public const int HarvestDuration = 767;
    public const int RestartQuestStartField = 52000056;
    public const int RestartQuestStartFieldRuneblader = 63000006;
    public const int RestartQuestStartFieldStriker = 63000015;
    public const int RestartQuestStartFieldSoulBinder = 63000035;
    public const int QuestPortalKeepTime = 300;
    public const string QuestPortalKeepNif = "Eff_Com_Portal_E_Quest";
    public const int QuestPortalDimensionY = 50;
    public const int QuestPortalDimensionZ = 350;
    public const int QuestPortalSummonTime = 600;
    public const int QuestPortalDistanceFromNpc = 200;
    public const int PetChangeNameMeret = 100;
    public const int PetRunSpeed = 350;
    public const int PetPickDistance = 1050;
    public const int PetSummonCastTime = 800;
    public const int PetBoreTime = 60000;
    public const int PetIdleTime = 70000;
    public const int PetTiredTime = 10000;
    public const int PetSkillTime = 13000;
    public const string PetEffectUse = "Pet/Eff_Pet_Use.xml";
    public const string PetEffectSkill = "Pet/Eff_Pet_Skill.xml";
    public const string PetEffectHappy = "Pet/Eff_Pet_Happy.xml";
    public const string PetGemChatBalloon = "pet";
    public const int PetTrapAreaDistanceEasy = 150;
    public const int PetTrapAreaDistanceNormal = 150;
    public const int PetTrapAreaDistanceHard = 150;
    public const string PetTrapAreaEffectEasy = "Pet/Eff_Pet_TrapInstallArea_easy.xml";
    public const string PetTrapAreaEffectNormal = "Pet/Eff_Pet_TrapInstallArea_normal.xml";
    public const string PetTrapAreaEffectHard = "Pet/Eff_Pet_TrapInstallArea_hard.xml";
    public const string PetTrapAreaEffectOtherUser = "Pet/Eff_Pet_TrapArea_OtherUser.xml";
    public const string PetTamingMaxPointEffect = "Pet/Eff_PetTaming_MaxPoint.xml";
    public const string PetTamingAttackMissEffect = "Pet/Eff_PetTaming_Attack_Miss.xml";
    public const string PetTrapDropItemEffect = "Pet/Eff_PetTrap_DropItem.xml";
    public const int TamingPetEscapeTime = 300;
    public const int TamingPetMaxPoint = 10000;
    public const int PetNameLengthMin = 2;
    public const int PetNameLengthMax = 25;
    public const int PetTrapDropVisibleDelay = 2000;
    public const int PetMaxLevel = 50;
    public const string VisitorBookURL = "";
    public const int OneShotSkillID = 19900061;
    public const int BagSlotTabGameCount = 48;
    public const int BagSlotTabSkinCount = 150;
    public const int BagSlotTabSummonCount = 48;
    public const int BagSlotTabMaterialCount = 48;
    public const int BagSlotTabMasteryCount = 126;
    public const int BagSlotTabLifeCount = 48;
    public const int BagSlotTabQuestCount = 48;
    public const int BagSlotTabGemCount = 48;
    public const int BagSlotTabPetCount = 60;
    public const int BagSlotTabPetEquipCount = 48;
    public const int BagSlotTabActiveSkillCount = 84;
    public const int BagSlotTabCoinCount = 48;
    public const int BagSlotTabBadgeCount = 60;
    public const int BagSlotTabMiscCount = 84;
    public const int BagSlotTabLapenshardCount = 48;
    public const int BagSlotTabPieceCount = 48;
    public const int BagSlotTabGameCountMax = 48;
    public const int BagSlotTabSkinCountMax = 150;
    public const int BagSlotTabSummonCountMax = 48;
    public const int BagSlotTabMaterialCountMax = 48;
    public const int BagSlotTabMasteryCountMax = 48;
    public const int BagSlotTabLifeCountMax = 48;
    public const int BagSlotTabQuestCountMax = 48;
    public const int BagSlotTabGemCountMax = 48;
    public const int BagSlotTabPetCountMax = 78;
    public const int BagSlotTabPetEquipCountMax = 48;
    public const int BagSlotTabActiveSkillCountMax = 48;
    public const int BagSlotTabCoinCountMax = 48;
    public const int BagSlotTabBadgeCountMax = 48;
    public const int BagSlotTabMiscCountMax = 48;
    public const int BagSlotTabLapenshardCountMax = 48;
    public const int BagSlotTabPieceCountMax = 48;
    public const int MasteryObjectInteractionDistance = 150;
    public const float GatheringObjectMarkOffsetX = 0.0f;
    public const float GatheringObjectMarkOffsetY = 0.0f;
    public const float BreedingObjectMarkOffsetX = 0.0f;
    public const float BreedingObjectMarkOffsetY = 0.0f;
    public const int UGCAttention = 0;
    public const int UGCInfringementCenter = 1;
    public const string CharacterSelectBoreIdleEffect_Ranger = "";
    public const string CharacterSelectBoreIdleEffect_SoulBinder = "";
    public const int DisableSoloPlayHighLevelDungeon = 0;
    public const int MergeSmithScriptID = 10;
    public const int AutoPressActionKeyDuration = 500;
    public const int WebBrowserSizeWidthMin = 438;
    public const int WebBrowserSizeWidthMax = 1700;
    public const int WebBrowserSizeHeightMin = 708;
    public const int WebBrowserSizeHeightMax = 1003;
    public const bool WebBrowserEnableSizingButton = true;
    public const int MeretAirTaxiPrice = 15;
    public const int GlobalPortalMinLevel = 10;
    public const int userMassiveExtraRewardMax = 5;
    public const int SkillBookTreeAddTabFeeMeret = 990;
    public const int MentorRequireLevel = 50;
    public const int MenteeRequireLevel = 30;
    public const int MentorMaxWaitingCount = 100;
    public const int MenteeMaxReceivedCount = 20;
    public const int FindDungeonHelpEasyDungeonLevel = 50;
    public const int CoupleEffectCheckTick = 5000;
    public const int CoupleEffectCheckRadius = 150;
    public const int FameContentsSkyFortressMapID0 = 02000421;
    public const int FameContentsSkyFortressMapID1 = 02000422;
    public const int FameContentsSkyFortressMapID2 = 52010039;
    public const int FameContentsSkyFortressMapID3 = 52010040;
    public const int AllianceQuestPickCount = 2;
    public const int FieldQuestPickCount = 1;
    public const int FameContentsSkyFortressGotoMapID = 02000422;
    public const int FameContentsSkyFortressGotoPortalID = 3;
    public const int FameContentsRequireQuestID = 91000013;
    public const int FameExpedContentsRequireQuestID = 50101050;
    public const int DailyPetEnchantMaxCount = 24;
    public const int MouseCursorHideTime = 30;
    public const int EnchantTransformScriptID = 10;
    public const float AutoHideGroupAlpha = 0.6f;
    public const int AutoHideGroupHitVisibleTick = 3000;
    public const int UgcShopCharRotateStartDegreeY = 178;
    public const int UgcShopCharRotateEndDegreeY = 8;
    public const int SurvivalScanAdditionalID = 71000052;
    public const int MapleSurvivalTopNRanking = 5;
    public const string MapleSurvivalSeasonRewardUrl =
        "http://maplestory2.nexon.net/en/news/article/32249/mushking-royale-championship-rewards";
    public const int TreeWateringEmotion = 10000;
    public const int AdventureLevelLimit = 10000;
    public const int AdventureLevelLvUpExp = 1000000;
    public const int AdventureLevelMaxExp = 1500000;
    public const float AdventureLevelFactor = 0.02f;
    public const int AdventureExpFactorElite = 10;
    public const int AdventureExpFactorBoss = 100;
    public const int AdventureLevelStartLevel = 50;
    public const int AdventureLevelLvUpRewardItem = 30001133;
    public const int NameColorDeadDuration = 2000;
    public const float MesoRevivalFeeReduceLimit = 0.5f;
    public const float IngredientFeeReduceLimit = 0.5f;
    public const int StatPointLimit_str = 100;
    public const int StatPointLimit_dex = 100;
    public const int StatPointLimit_int = 100;
    public const int StatPointLimit_luk = 100;
    public const int StatPointLimit_hp = 100;
    public const int StatPointLimit_cap = 60;
    public const float GamePadRumbleMultiple = 3.0f;
    public const int NurturingEatMaxCount = 0;
    public const int NurturingPlayMaxCount = 3;
    public const string NurturingQuestTag = "NurturingGhostCats";
    public const int NurturingDuration = 3000;
    public const int NurturingInteractionDistance = 150;
    public const int NurturingEatGrowth = 10;
    public const int NurturingPlayGrowth = 10;
    public const int NurturingPlayMailId = 19101804;
    public const int NurturingPlayMaxGrowth = 3;
    public const int NurturingHungryTime = 1000;
    public const int SkillPointLimitLevel1 = 80;
    public const int SkillPointLimitLevel2 = 70;
    public const int SellPriceNormalMax = 4628;
    public const int SellPriceRareMax = 5785;
    public const int SellPriceEliteMax = 7405;
    public const int SellPriceExcellentMax = 9256;
    public const int SellPriceLegendaryMax = 11339;
    public const int SellPriceArtifactMax = 13653;
    public const string RegionServerUrl_de = "http://ugc.maplestory2.nexon.net/region/region_DE.xml";
    public const string RegionServerUrl_en = "http://ugc.maplestory2.nexon.net/region/region_EN.xml";
    public const string RegionServerUrl_bpo = "http://ugc.maplestory2.nexon.net/region/region_BPO.xml";
    public const int HoldAttackSkillID = 10700252;
    public const int TooltipLabelMaxWidth = 408;
    public const int ClubNameLengthMin = 2;
    public const int ClubNameLengthMax = 25;
    public const int ClubMaxCount = 3;
    public const int UgcNameLengthMin = 3;
    public const int UgcNameLengthMax = 25;
    public const int UgcTagLengthMax = 12;
    public const int ChangeJobLevel = 60;
    public const int LapenshardOpenQuestID = 20002391;
    public const int MaidNameLengthMin = 1;
    public const int MaidNameLengthMax = 35;
    public const int MaidDescLengthMin = 1;
    public const int MaidDescLengthMax = 35;
    public const int GamePadStickMoveValue = 50;
    public const int HighlightMenuUsingLevel = 5;
    public const int PartyVoteReadyDurationSeconds = 20;
    public const int PartyVoteReadyTagExpireSeconds = 10;
    public const int ShieldBarOffsetY = -10;
    public const int MouseInteractLimitDistance = 2000;
    public const int AutoInstallEquipmentMinLevel = 5;
    public const int AutoInstallEquipmentMaxLevel = 49;
    public const string PartySearchRegisterComboValues = "4,6,10";
    public const int StatScaleMarkingAdditionalEffect = 70000174;
    public const string DungeonRewardFailEmotions = "90200001,90200009,90200005,90200018";
    public const int SummonPetSkillID = 82100001;
    public const int UGCMapSetItemEffectCountLimit = 10;
    public const string DiscordAppID = "555204064091045904";
    public const int ItemBoxMultiOpenMaxCount = 10;
    public const int ItemBoxMultiOpenLimitCount = 500;
    public const int BuffBalloonDistance = 3800;
    public const int PaybackStartDate = 20191024;
    public const int PaybackMailId = 50000020;
    public const int PaybackMailPeriodDay = 90;
    public const int PaybackMaxRewardMeret = 10000;
    public const string PaybackGuideUrl = "http://maplestory2.nexon.com/News/Events";
    public const int DummyNpcMale = 2040998;
    public const int DummyNpcFemale = 2040999;
    public static int DummyNpc(Gender gender) => gender is Gender.Female ? DummyNpcFemale : DummyNpcMale;

    #endregion

    #region server table/constants.xml
    public const int NextStateTriggerDefaultTick = 100;
    public const int UserRevivalPaneltyTick = 3600000;
    public const int UserRevivalPaneltyMinLevel = 10;
    public const int maxDeadCount = 3;
    public const byte hitPerDeadCount = 5;
    public const int FishFightingProp = 3000;

    public const float NpcLastSightRadius = 1800;
    public const float NpcLastSightHeightUp = 525;
    public const float NpcLastSightHeightDown = 225;

    public const int RecoveryHPWaitTick = 1000;
    public const int RecoverySPWaitTick = 1000;
    public const int RecoveryEPWaitTick = 1000;
    public const float FallBoundingAddedDistance = 750f;

    public const int UserBattleDurationTick = 5000;

    public const int SystemShopNPCIDConstruct = 11000486;
    public const int SystemShopNpcIDUGCDesign = 11000166;
    public const int SystemShopNPCIDHonorToken = 11001562;
    public const int SystemShopNPCIDFishing = 11001609;
    public const int SystemShopNPCIDMentor = 11003561;
    public const int SystemShopNPCIDMentee = 11003562;
    #endregion
}

#pragma warning restore IDE1006 // Naming Styles
