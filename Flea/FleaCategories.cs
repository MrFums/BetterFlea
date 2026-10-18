using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace BetterFlea.Flea;

public sealed record FleaCategory(
    string Name,
    Func<BanFromFleaConfig, bool> IsBanned,
    MongoId[] BaseClasses,
    IReadOnlySet<MongoId>? Tpls = null,
    Func<TemplateItem, bool>? Filter = null);

// Every handbook item lands in exactly one category: the first match wins,
// so specific entries (marked keys, armored rigs, magazines...) sit above their parents.
public static class FleaCategories
{
    public static readonly MongoId Key = new("543be5e94bdc2df1348b4568");
    public static readonly MongoId Keycard = new("5c164d2286f774194c5e69fa");
    public static readonly MongoId Fuel = new("5d650c3e815116009f6201d2");

    public static readonly IReadOnlySet<MongoId> MarkedKeys = new HashSet<MongoId>
    {
        new("5780cf7f2459777de4559322"), // Dorm room 314 marked key
        new("5d80c62a86f7744036212b3f"), // RB-VO marked key
        new("5d80c60f86f77440373c4ece"), // RB-BK marked key
        new("5ede7a8229445733cb4c18e2"), // RB-PKPM marked key
        new("64ccc25f95763a1ae376e447"), // Mysterious room marked key
        new("63a3a93f8a56922e82001f5d"), // Abandoned factory marked key
        new("62987dfc402c7f69bf010923")  // Shared bedroom marked key
    };

    public static readonly IReadOnlyList<FleaCategory> All = new List<FleaCategory>
    {
        // Keys
        new("keys.markedKeys", b => b.Keys.MarkedKeys, [], Tpls: MarkedKeys),
        new("keys.keycards", b => b.Keys.Keycards, [Keycard]),
        new("keys.mechanicalKeys", b => b.Keys.MechanicalKeys, [Key]),

        // Gear: armored rigs are vests with plate/soft armor slots
        new("gear.armoredRigs", b => b.Gear.ArmoredRigs, [new("5448e5284bdc2dcb718b4567")],
            Filter: item => item.Properties?.Slots?.Any() == true),
        new("gear.chestRigs", b => b.Gear.ChestRigs, [new("5448e5284bdc2dcb718b4567")]),
        new("gear.backpacks", b => b.Gear.Backpacks, [new("5448e53e4bdc2d60728b4567")]),
        new("gear.bodyArmor", b => b.Gear.BodyArmor, [new("5448e54d4bdc2dcc718b4568")]),
        new("gear.armorPlates", b => b.Gear.ArmorPlates, [new("644120aa86ffbe10ee032b6f")]),
        new("gear.headwear", b => b.Gear.Headwear, [new("5a341c4086f77401f2541505")]),
        new("gear.faceCovers", b => b.Gear.FaceCovers, [new("5a341c4686f77469e155819e")]),
        new("gear.eyewear", b => b.Gear.Eyewear, [new("5448e5724bdc2ddf718b4568")]),
        new("gear.helmetAttachments", b => b.Gear.HelmetAttachments, [new("57bef4c42459772e8d35a53b")]),
        new("gear.headphones", b => b.Gear.Headphones, [new("5645bcb74bdc2ded0b8b4578")]),
        new("gear.armbands", b => b.Gear.Armbands, [new("5b3f15d486f77432d0509248")]),
        new("gear.containers", b => b.Gear.Containers,
        [
            new("5795f317245977243854e041"), // SimpleContainer
            new("5448bf274bdc2dfc2f8b456a"), // MobContainer
            new("5671435f4bdc2d96058b4569"), // LockableContainer
            new("62f109593b54472778797866")  // RandomLootContainer
        ]),

        // Weapons
        new("weapons.guns", b => b.Weapons.Guns, [new("5422acb9af1c889c16000029")]),
        new("weapons.throwables", b => b.Weapons.Throwables, [new("543be6564bdc2df4348b4568")]),
        new("weapons.melee", b => b.Weapons.Melee, [new("5447e1d04bdc2dff2f8b4567")]),

        // Weapon parts
        new("weaponParts.magazines", b => b.WeaponParts.Magazines, [new("5448bc234bdc2d3c308b4569")]),
        new("weaponParts.sights", b => b.WeaponParts.Sights, [new("5448fe7a4bdc2d6f028b456b")]),
        new("weaponParts.suppressors", b => b.WeaponParts.Suppressors, [new("550aa4cd4bdc2dd8348b456c")]),
        new("weaponParts.otherParts", b => b.WeaponParts.OtherParts, [new("5448fe124bdc2da5018b4567")]),

        // Ammo
        new("ammo.rounds", b => b.Ammo.Rounds, [new("5485a8684bdc2da71d8b4567")]),
        new("ammo.ammoBoxes", b => b.Ammo.AmmoBoxes, [new("543be5cb4bdc2deb348b4568")]),

        // Medical
        new("medical.medkits", b => b.Medical.Medkits, [new("5448f39d4bdc2d0a728b4568")]),
        new("medical.bandagesAndSplints", b => b.Medical.BandagesAndSplints, [new("5448f3ac4bdc2dce718b4569")]),
        new("medical.painkillers", b => b.Medical.Painkillers, [new("5448f3a14bdc2d27728b4569")]),
        new("medical.stimulants", b => b.Medical.Stimulants, [new("5448f3a64bdc2d60728b456a")]),

        // Other
        new("other.foodAndDrink", b => b.Other.FoodAndDrink, [new("543be6674bdc2df1348b4569")]),
        new("other.barterItems", b => b.Other.BarterItems, [new("5448eb774bdc2d0a728b4567")]),
        new("other.info", b => b.Other.Info, [new("5448ecbe4bdc2d60728b4568")]),
        new("other.maps", b => b.Other.Maps, [new("567849dd4bdc2d150f8b456e")]),
        new("other.posters", b => b.Other.Posters, [new("6759673c76e93d8eb20b2080")]),
        new("other.specialEquipment", b => b.Other.SpecialEquipment, [new("5447e0e74bdc2d3c308b4567")])
        // No money switch: the game never lets money onto the flea anyway.
    };

    public static readonly IReadOnlySet<string> Names = All.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static FleaCategory? Find(TemplateItem item, ItemHelper itemHelper)
    {
        foreach (var category in All)
        {
            var matches = category.Tpls?.Contains(item.Id) == true
                || (category.BaseClasses.Length > 0 && itemHelper.IsOfBaseclasses(item.Id, category.BaseClasses));

            if (matches && (category.Filter is null || category.Filter(item)))
            {
                return category;
            }
        }

        return null;
    }
}
