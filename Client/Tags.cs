using EFT.InventoryLogic;

namespace BetterFlea.Client;

// The one line of text shown for an item, wherever it's shown. A ban wins over a trader purchase:
// a banned item can't be listed no matter where it came from.
internal static class Tags
{
    public const string FleaBannedText = "Banned at the Flea Market";
    public const string BoughtFromTraderText = "Trader item - can't sell at the Flea Market";

    public const string FleaBanned = "<color=#c84b4b>" + FleaBannedText + "</color>";
    public const string BoughtFromTrader = "<color=#d9a441>" + BoughtFromTraderText + "</color>";

    public static string For(Item item)
    {
        if (item == null)
        {
            return null;
        }

        return FleaStatus.IsFleaBanned(item) ? FleaBanned
            : FleaStatus.IsBoughtFromTrader(item) ? BoughtFromTrader
            : null;
    }
}
