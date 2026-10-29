using System;
using EFT;
using EFT.Communications;
using EFT.UI;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace BetterFlea.Client;

// After a listing, the flip penalty on it (if any) from the server, shown on screen. The details go to the
// Messenger as well.
internal static class FlipNotice
{
    private const string Route = "/betterflea/flip-notice";

    private sealed class Notice
    {
        public string Tpl = "";
        public double Minutes;
        public double Bought;
        public int ClearsIn;
    }

    public static Action After(Action callback) => () =>
    {
        callback?.Invoke();
        Show();
    };

    private static void Show()
    {
        try
        {
            var notice = JsonConvert.DeserializeObject<Notice>(RequestHandler.GetJson(Route));
            if (notice == null || notice.Minutes < 1)
            {
                return;
            }

            var name = (notice.Tpl + " ShortName").Localized();
            var text = ClientText.Format("Flip penalty on {0}: bought {1} recently, so buyers take about {2} min longer.",
                name, notice.Bought.ToString("0"), notice.Minutes.ToString("0"));
            if (notice.ClearsIn > 0)
            {
                text += " " + ClientText.Format("Listed in {0} min, there'd be no wait.", notice.ClearsIn);
            }

            NotificationManager.DisplayWarningNotification(text);
        }
        catch (Exception)
        {
            // An older server without the route, or it's restarting: the Messenger still has it.
        }
    }
}
