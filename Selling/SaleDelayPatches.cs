using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace BetterFlea.Selling;

// After the database is loaded, since it holds on to the globals table.
[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public class SaleDelayPatchesLoad(
    ConfigManager configManager,
    GlobalTable globalTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(configManager.ModPath))
        {
            configManager.Load();
        }

        SaleDelayGate.Bind(configManager, globalTable);
        new SaleDelayPatch().Enable();
        return Task.CompletedTask;
    }
}

// SPT decides up front when each buyer of a player offer turns up. We push that whole timeline back
// by one rolled wait, so the order and spacing of buyers stay as SPT made them.
internal static class SaleDelayGate
{
    private static ConfigManager? _config;
    private static GlobalTable? _globals;

    public static void Bind(ConfigManager configManager, GlobalTable globalTable)
    {
        _config = configManager;
        _globals = globalTable;
    }

    public static void Delay(List<SellResult> sales)
    {
        var settings = _config?.Mod.PlayerSelling.SaleDelay;
        if (settings is null || !settings.Enabled || sales.Count == 0)
        {
            return;
        }

        // Flipped items never get the lucky instant sale.
        var flipMinutes = FlipPenaltyGate.ExtraWaitMinutes();
        if (flipMinutes <= 0 && Random.Shared.NextDouble() < settings.NoDelayChance)
        {
            return;
        }

        var waitMinutes = settings.MinMinutes + Random.Shared.NextDouble() * (settings.MaxMinutes - settings.MinMinutes) + flipMinutes;
        var waitSeconds = (long)Math.Round(waitMinutes * 60.0);
        if (waitSeconds <= 0)
        {
            return;
        }

        foreach (var sale in sales)
        {
            sale.SellTime += waitSeconds;
        }

        // A buyer pushed past the end of the listing never shows up; the offer just expires as usual.
        if (OfferEndsAt() is { } end)
        {
            sales.RemoveAll(sale => sale.SellTime > end);
        }
    }

    // Same window SPT uses when it rolls the sales: now + the flea's offer duration.
    private static long? OfferEndsAt()
    {
        var hours = _globals?.Configuration?.RagFair?.OfferDurationTimeInHour;
        if (hours is not { } duration || duration <= 0)
        {
            return null;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)TimeSpan.FromHours((int)duration).TotalSeconds;
    }
}

public class SaleDelayPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(RagfairSellHelper).GetMethod(
            nameof(RagfairSellHelper.RollForSale),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(List<SellResult> __result)
    {
        SaleDelayGate.Delay(__result);
    }
}
