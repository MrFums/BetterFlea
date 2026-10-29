using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT.Settings;

namespace BetterFlea.Client;

// What the client shows, in the game's own language (Settings > Game > Language). English when the language
// isn't one Better Flea has, or the setting can't be read. "{0}" is filled in: a level or an item name.
internal static class ClientText
{
    private static string _language;

    // The game's language codes for the languages Better Flea has.
    private static readonly Dictionary<string, string> GameCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = "ru", ["ch"] = "zh", ["ge"] = "de", ["fr"] = "fr", ["es"] = "es", ["es-mx"] = "es",
        ["po"] = "pt", ["pl"] = "pl", ["jp"] = "ja", ["kr"] = "ko"
    };

    private static string Language
    {
        get
        {
            if (_language != null)
            {
                return _language;
            }

            try
            {
                var code = Singleton<SettingsManager>.Instance?.Game?.Settings?.Language?.Value;
                if (code == null)
                {
                    return "en";
                }

                _language = GameCodes.TryGetValue(code, out var ours) ? ours : "en";
            }
            catch (Exception)
            {
                _language = "en";
            }

            return _language;
        }
    }

    public static string Get(string english, object value = null)
    {
        var text = Texts.TryGetValue(english, out var byLanguage) && byLanguage.TryGetValue(Language, out var translated) ? translated : english;
        return value == null ? text : text.Replace("{0}", value.ToString());
    }

    // With several values: "{0}", "{1}" and so on.
    public static string Format(string english, params object[] values)
    {
        var text = Get(english);
        for (var i = 0; i < values.Length; i++)
        {
            text = text.Replace("{" + i + "}", values[i]?.ToString());
        }

        return text;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        ["Flip penalty on {0}: bought {1} recently, so buyers take about {2} min longer."] = new()
        {
            ["ru"] = "Штраф за перепродажу на {0}: недавно куплено {1}, поэтому покупатели появятся примерно на {2} мин позже.",
            ["zh"] = "{0} 倒卖惩罚：最近买了 {1} 个，买家会晚约 {2} 分钟出现。",
            ["de"] = "Weiterverkaufsstrafe auf {0}: kürzlich {1} gekauft, daher brauchen Käufer etwa {2} Min. länger.",
            ["fr"] = "Pénalité de revente sur {0} : {1} acheté(s) récemment, les acheteurs mettront environ {2} min de plus.",
            ["es"] = "Penalización por reventa en {0}: compraste {1} hace poco, así que los compradores tardarán unos {2} min más.",
            ["pt"] = "Penalidade de revenda em {0}: comprou {1} recentemente, então os compradores levarão cerca de {2} min a mais.",
            ["pl"] = "Kara za odsprzedaż na {0}: niedawno kupiono {1}, więc kupujący pojawią się około {2} min później.",
            ["ja"] = "{0} の転売ペナルティ：最近 {1} 個購入したため、買い手が来るまで約 {2} 分長くかかります。",
            ["ko"] = "{0} 되팔기 페널티: 최근 {1}개를 구매해 구매자가 약 {2}분 더 늦게 옵니다."
        },
        ["Listed in {0} min, there'd be no wait."] = new()
        {
            ["ru"] = "Если выставить через {0} мин, ожидания не будет.", ["zh"] = "{0} 分钟后再上架就不用等。",
            ["de"] = "In {0} Min. angeboten, gäbe es keine Wartezeit.", ["fr"] = "Mis en vente dans {0} min, il n'y aurait pas d'attente.",
            ["es"] = "Si lo publicas en {0} min, no habrá espera.", ["pt"] = "Anunciado em {0} min, não haveria espera.",
            ["pl"] = "Wystawione za {0} min nie miałoby oczekiwania.", ["ja"] = "{0} 分後に出品すれば待ち時間はありません。",
            ["ko"] = "{0}분 뒤에 등록하면 대기 시간이 없습니다."
        },
        ["Flea banned - cannot be listed"] = new()
        {
            ["ru"] = "Запрещено на Барахолке - нельзя выставить", ["zh"] = "跳蚤市场禁售 - 无法上架", ["de"] = "Auf dem Flohmarkt verboten - kann nicht angeboten werden",
            ["fr"] = "Interdit au Marché - mise en vente impossible", ["es"] = "Prohibido en el Mercadillo - no se puede vender", ["pt"] = "Proibido no Mercado - não pode ser anunciado",
            ["pl"] = "Zakazane na Pchlim Targu - nie można wystawić", ["ja"] = "フリーマーケット禁止 - 出品できません", ["ko"] = "플리마켓 금지 - 등록할 수 없음"
        },
        ["Player listings are turned off"] = new()
        {
            ["ru"] = "Продажа игроками отключена", ["zh"] = "玩家上架已关闭", ["de"] = "Angebote von Spielern sind deaktiviert",
            ["fr"] = "La vente par les joueurs est désactivée", ["es"] = "La venta de jugadores está desactivada", ["pt"] = "Anúncios de jogadores estão desativados",
            ["pl"] = "Wystawianie przez graczy jest wyłączone", ["ja"] = "プレイヤーの出品はオフです", ["ko"] = "플레이어 등록이 꺼져 있음"
        },
        ["Player listings are turned off at the Flea Market."] = new()
        {
            ["ru"] = "Продажа игроками на Барахолке отключена.", ["zh"] = "跳蚤市场已关闭玩家上架。", ["de"] = "Angebote von Spielern sind auf dem Flohmarkt deaktiviert.",
            ["fr"] = "La vente par les joueurs est désactivée au Marché.", ["es"] = "La venta de jugadores está desactivada en el Mercadillo.", ["pt"] = "Anúncios de jogadores estão desativados no Mercado.",
            ["pl"] = "Wystawianie przez graczy na Pchlim Targu jest wyłączone.", ["ja"] = "フリーマーケットでのプレイヤーの出品はオフです。", ["ko"] = "플리마켓에서 플레이어 등록이 꺼져 있습니다."
        },
        ["Bought from trader - cannot be listed"] = new()
        {
            ["ru"] = "Куплено у торговца - нельзя выставить", ["zh"] = "从商人处购买 - 无法上架", ["de"] = "Beim Händler gekauft - kann nicht angeboten werden",
            ["fr"] = "Acheté chez un marchand - mise en vente impossible", ["es"] = "Comprado a un vendedor - no se puede vender", ["pt"] = "Comprado de um comerciante - não pode ser anunciado",
            ["pl"] = "Kupione u handlarza - nie można wystawić", ["ja"] = "トレーダーから購入 - 出品できません", ["ko"] = "상인에게서 구매함 - 등록할 수 없음"
        },
        ["Unlocks on the Flea Market at level {0}"] = new()
        {
            ["ru"] = "Открывается на Барахолке с уровня {0}", ["zh"] = "{0} 级后在跳蚤市场解锁", ["de"] = "Auf dem Flohmarkt ab Stufe {0} freigeschaltet",
            ["fr"] = "Débloqué au Marché au niveau {0}", ["es"] = "Se desbloquea en el Mercadillo al nivel {0}", ["pt"] = "Desbloqueia no Mercado no nível {0}",
            ["pl"] = "Odblokowuje się na Pchlim Targu od poziomu {0}", ["ja"] = "フリーマーケットでレベル {0} から解放", ["ko"] = "플리마켓에서 레벨 {0}부터 해금"
        },
        ["Contains {0} - cannot be listed"] = new()
        {
            ["ru"] = "Содержит {0} - нельзя выставить", ["zh"] = "包含 {0} - 无法上架", ["de"] = "Enthält {0} - kann nicht angeboten werden",
            ["fr"] = "Contient {0} - mise en vente impossible", ["es"] = "Contiene {0} - no se puede vender", ["pt"] = "Contém {0} - não pode ser anunciado",
            ["pl"] = "Zawiera {0} - nie można wystawić", ["ja"] = "{0} が入っています - 出品できません", ["ko"] = "{0} 포함 - 등록할 수 없음"
        },
        ["This item can only be sold at the Flea Market from level {0}."] = new()
        {
            ["ru"] = "Этот предмет можно продать на Барахолке только с уровня {0}.", ["zh"] = "该物品需达到 {0} 级才能在跳蚤市场出售。", ["de"] = "Dieser Gegenstand kann erst ab Stufe {0} auf dem Flohmarkt verkauft werden.",
            ["fr"] = "Cet objet ne peut être vendu au Marché qu'à partir du niveau {0}.", ["es"] = "Este objeto solo se puede vender en el Mercadillo a partir del nivel {0}.", ["pt"] = "Este item só pode ser vendido no Mercado a partir do nível {0}.",
            ["pl"] = "Ten przedmiot można sprzedać na Pchlim Targu dopiero od poziomu {0}.", ["ja"] = "このアイテムはレベル {0} からフリーマーケットで売れます。", ["ko"] = "이 아이템은 레벨 {0}부터 플리마켓에서 판매할 수 있습니다."
        },
        ["Flea banned"] = new()
        {
            ["ru"] = "Запрещено на Барахолке", ["zh"] = "跳蚤市场禁售", ["de"] = "Flohmarkt-Verbot", ["fr"] = "Interdit au Marché", ["es"] = "Prohibido en el Mercadillo",
            ["pt"] = "Proibido no Mercado", ["pl"] = "Zakaz na Pchlim Targu", ["ja"] = "フリーマーケット禁止", ["ko"] = "플리마켓 금지"
        },
        ["Bought from trader"] = new()
        {
            ["ru"] = "Куплено у торговца", ["zh"] = "从商人处购买", ["de"] = "Beim Händler gekauft", ["fr"] = "Acheté chez un marchand", ["es"] = "Comprado a un vendedor",
            ["pt"] = "Comprado de um comerciante", ["pl"] = "Kupione u handlarza", ["ja"] = "トレーダーから購入", ["ko"] = "상인에게서 구매함"
        },
        ["Cannot be listed on the Flea Market"] = new()
        {
            ["ru"] = "Нельзя выставить на Барахолку", ["zh"] = "无法在跳蚤市场上架", ["de"] = "Kann nicht auf dem Flohmarkt angeboten werden",
            ["fr"] = "Ne peut pas être mis en vente au Marché", ["es"] = "No se puede vender en el Mercadillo", ["pt"] = "Não pode ser anunciado no Mercado",
            ["pl"] = "Nie można wystawić na Pchlim Targu", ["ja"] = "フリーマーケットに出品できません", ["ko"] = "플리마켓에 등록할 수 없음"
        },
        ["Unlocks on flea market"] = new()
        {
            ["ru"] = "Открывается на Барахолке", ["zh"] = "跳蚤市场解锁", ["de"] = "Flohmarkt-Freischaltung", ["fr"] = "Déblocage au Marché", ["es"] = "Desbloqueo en el Mercadillo",
            ["pt"] = "Desbloqueio no Mercado", ["pl"] = "Odblokowanie na Pchlim Targu", ["ja"] = "フリーマーケット解放", ["ko"] = "플리마켓 해금"
        },
        ["{0} Level"] = new()
        {
            ["ru"] = "Уровень {0}", ["zh"] = "{0} 级", ["de"] = "Stufe {0}", ["fr"] = "Niveau {0}", ["es"] = "Nivel {0}",
            ["pt"] = "Nível {0}", ["pl"] = "Poziom {0}", ["ja"] = "レベル {0}", ["ko"] = "레벨 {0}"
        },
        ["Can be bought and sold on the Flea Market from level {0}."] = new()
        {
            ["ru"] = "Можно покупать и продавать на Барахолке с уровня {0}.", ["zh"] = "达到 {0} 级后可在跳蚤市场买卖。", ["de"] = "Kann ab Stufe {0} auf dem Flohmarkt gekauft und verkauft werden.",
            ["fr"] = "Peut être acheté et vendu au Marché à partir du niveau {0}.", ["es"] = "Se puede comprar y vender en el Mercadillo a partir del nivel {0}.", ["pt"] = "Pode ser comprado e vendido no Mercado a partir do nível {0}.",
            ["pl"] = "Można kupować i sprzedawać na Pchlim Targu od poziomu {0}.", ["ja"] = "レベル {0} からフリーマーケットで売買できます。", ["ko"] = "레벨 {0}부터 플리마켓에서 사고팔 수 있습니다."
        },
        ["Trader items can't be sold at the Flea Market."] = new()
        {
            ["ru"] = "Предметы от торговцев нельзя продать на Барахолке.", ["zh"] = "从商人处购买的物品无法在跳蚤市场出售。", ["de"] = "Händlerware kann nicht auf dem Flohmarkt verkauft werden.",
            ["fr"] = "Les objets de marchand ne peuvent pas être vendus au Marché.", ["es"] = "Los objetos de vendedores no se pueden vender en el Mercadillo.", ["pt"] = "Itens de comerciantes não podem ser vendidos no Mercado.",
            ["pl"] = "Przedmiotów od handlarzy nie można sprzedać na Pchlim Targu.", ["ja"] = "トレーダーのアイテムはフリーマーケットで売れません。", ["ko"] = "상인 아이템은 플리마켓에서 판매할 수 없습니다."
        }
    };
}
