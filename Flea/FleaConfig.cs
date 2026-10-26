using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterFlea.Flea;

public record FleaConfig
{
    [JsonPropertyName("enableBsgBlacklist")]
    public bool EnableBsgBlacklist { get; set; } = true;

    [JsonPropertyName("bannedCategories")]
    public BanFromFleaConfig BannedCategories { get; set; } = new();

    [JsonPropertyName("bannedItems")]
    public List<string> BannedItems { get; set; } = new();

    [JsonPropertyName("allowedItems")]
    public List<string> AllowedItems { get; set; } = new();

    // Mod name -> "all", or the item types (bannedCategories names like "weapons.guns") to ban from that mod.
    [JsonPropertyName("bannedModItems")]
    public Dictionary<string, List<string>> BannedModItems { get; set; } = new();

    [JsonPropertyName("levelLock")]
    public LevelLockConfig LevelLock { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

public record LevelLockConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("allowSellingBelowLevel")] public bool AllowSellingBelowLevel { get; set; }
    [JsonPropertyName("moddedItems")] public bool ModdedItems { get; set; } = true;

    // Item or category ID -> the level it unlocks at, in place of the real game's. 0 = no level needed.
    [JsonPropertyName("levels")] public Dictionary<string, int> Levels { get; set; } = new();
}

public record BanFromFleaConfig
{
    [JsonPropertyName("weapons")]
    public WeaponBans Weapons { get; set; } = new();

    [JsonPropertyName("ammo")]
    public AmmoBans Ammo { get; set; } = new();

    [JsonPropertyName("weaponParts")]
    public WeaponPartBans WeaponParts { get; set; } = new();

    [JsonPropertyName("gear")]
    public GearBans Gear { get; set; } = new();

    [JsonPropertyName("keys")]
    public KeyBans Keys { get; set; } = new();

    [JsonPropertyName("medical")]
    public MedicalBans Medical { get; set; } = new();

    [JsonPropertyName("other")]
    public OtherBans Other { get; set; } = new();
}

public record WeaponBans
{
    [JsonPropertyName("guns")] public bool Guns { get; set; }
    [JsonPropertyName("throwables")] public bool Throwables { get; set; }
    [JsonPropertyName("melee")] public bool Melee { get; set; }
}

public record AmmoBans
{
    [JsonPropertyName("rounds")] public bool Rounds { get; set; }
    [JsonPropertyName("ammoBoxes")] public bool AmmoBoxes { get; set; }
}

public record WeaponPartBans
{
    [JsonPropertyName("magazines")] public bool Magazines { get; set; }
    [JsonPropertyName("sights")] public bool Sights { get; set; }
    [JsonPropertyName("suppressors")] public bool Suppressors { get; set; }
    [JsonPropertyName("otherParts")] public bool OtherParts { get; set; }
}

public record GearBans
{
    [JsonPropertyName("bodyArmor")] public bool BodyArmor { get; set; }
    [JsonPropertyName("armoredRigs")] public bool ArmoredRigs { get; set; }
    [JsonPropertyName("chestRigs")] public bool ChestRigs { get; set; }
    [JsonPropertyName("backpacks")] public bool Backpacks { get; set; }
    [JsonPropertyName("armorPlates")] public bool ArmorPlates { get; set; }
    [JsonPropertyName("headwear")] public bool Headwear { get; set; }
    [JsonPropertyName("helmetAttachments")] public bool HelmetAttachments { get; set; }
    [JsonPropertyName("faceCovers")] public bool FaceCovers { get; set; }
    [JsonPropertyName("eyewear")] public bool Eyewear { get; set; }
    [JsonPropertyName("headphones")] public bool Headphones { get; set; }
    [JsonPropertyName("armbands")] public bool Armbands { get; set; }
    [JsonPropertyName("containers")] public bool Containers { get; set; }
}

public record KeyBans
{
    [JsonPropertyName("mechanicalKeys")] public bool MechanicalKeys { get; set; }
    [JsonPropertyName("markedKeys")] public bool MarkedKeys { get; set; }
    [JsonPropertyName("keycards")] public bool Keycards { get; set; }
}

public record MedicalBans
{
    [JsonPropertyName("medkits")] public bool Medkits { get; set; }
    [JsonPropertyName("bandagesAndSplints")] public bool BandagesAndSplints { get; set; }
    [JsonPropertyName("painkillers")] public bool Painkillers { get; set; }
    [JsonPropertyName("stimulants")] public bool Stimulants { get; set; }
}

public record OtherBans
{
    [JsonPropertyName("foodAndDrink")] public bool FoodAndDrink { get; set; }
    [JsonPropertyName("barterItems")] public bool BarterItems { get; set; }
    [JsonPropertyName("info")] public bool Info { get; set; }
    [JsonPropertyName("maps")] public bool Maps { get; set; }
    [JsonPropertyName("posters")] public bool Posters { get; set; }
    [JsonPropertyName("specialEquipment")] public bool SpecialEquipment { get; set; }
}
