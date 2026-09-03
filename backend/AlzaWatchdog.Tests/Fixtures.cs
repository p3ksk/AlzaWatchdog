namespace AlzaWatchdog.Tests;

/// <summary>Pages captured from the live site during development.</summary>
public static class Fixtures
{
    /// <summary>A real product page for https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm</summary>
    public static string Product() => Load("cudy-n300-d10818009.html");

    /// <summary>A real product page carrying a discounted AlzaPlus+ members' price.</summary>
    public static string AlzaPlusProduct() => Load("alzapower-magcore-d6969310.html");

    /// <summary>A real product page whose price drops further with a discount code.</summary>
    public static string CouponProduct() => Load("alzaergo-chair-d9785218.html");

    /// <summary>Cloudflare's "we blocked you" interstitial.</summary>
    public static string Blocked() => Load("cloudflare-blocked.html");

    private static string Load(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
