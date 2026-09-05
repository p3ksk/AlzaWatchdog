using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AlzaWatchdog.Api.Scraping;

/// <summary>
/// Pulls product data out of an alza.sk detail page.
///
/// Only the schema.org JSON-LD block is read — name, image, price, currency,
/// availability. CSS selectors against the markup would break on the next redesign.
/// </summary>
public static class AlzaProductParser
{
    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// Markers that appear only in Cloudflare's "we blocked you" interstitial. That page
    /// is sometimes served with a 200, so the status code alone is not enough to detect it.
    /// </summary>
    private static readonly string[] BlockPageMarkers =
    [
        "cdn-cgi/challenge-platform",
        "id=\"footer-ray-id\"",
    ];

    private const string SchemaPrefix = "https://schema.org/";

    /// <summary>data-slot is a component contract, so it outlives the hashed CSS classes.</summary>
    private const string PriceBoxTitle = "[data-slot=\"pb-title\"]";
    private const string PriceBoxPrice = "[data-slot=\"pb-price\"]";

    public static bool LooksLikeBlockPage(string html) =>
        BlockPageMarkers.Any(m => html.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static ScrapeResult Parse(string html)
    {
        if (LooksLikeBlockPage(html))
            return ScrapeResult.Failure(ScrapeStatus.Blocked, "Cloudflare interstitial served instead of the product page.");

        var document = Parser.ParseDocument(html);

        foreach (var script in document.QuerySelectorAll("script[type=\"application/ld+json\"]"))
        {
            var payload = script.TextContent;
            if (string.IsNullOrWhiteSpace(payload))
                continue;

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(payload);
            }
            catch (JsonException)
            {
                // Some pages carry several JSON-LD blocks; one being malformed
                // should not stop us finding the Product block.
                continue;
            }

            using (json)
            {
                foreach (var node in EnumerateNodes(json.RootElement))
                {
                    if (!IsType(node, "Product"))
                        continue;

                    var result = ReadProduct(node);

                    // The AlzaPlus+ price is not in the JSON-LD — schema.org has no
                    // way to say "cheaper if you subscribe" — so it has to come from
                    // the rendered price box.
                    return result with { PlusPrice = ReadPlusPrice(document, result.Price) };
                }
            }
        }

        return ScrapeResult.Failure(ScrapeStatus.ParseFailed, "No Product JSON-LD block found on the page.");
    }

    /// <summary>Yields the root object, or each element if the root is an array.</summary>
    private static IEnumerable<JsonElement> EnumerateNodes(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                yield return item;
        }
        else
        {
            yield return root;
        }
    }

    private static bool IsType(JsonElement node, string expected)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("@type", out var type))
            return false;

        return type.ValueKind switch
        {
            JsonValueKind.String => string.Equals(type.GetString(), expected, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => type.EnumerateArray()
                .Any(t => t.ValueKind == JsonValueKind.String
                          && string.Equals(t.GetString(), expected, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    /// <summary>
    /// The AlzaPlus+ price, found by its own heading ("-10 % s AlzaPlus+") because
    /// the price boxes share data-slot names and a plain text search for
    /// "AlzaPlus" would match the navigation on every page.
    /// </summary>
    private static decimal? ReadPlusPrice(IDocument document, decimal? regularPrice)
    {
        foreach (var title in document.QuerySelectorAll(PriceBoxTitle))
        {
            if (!title.TextContent.Contains("alzaplus", StringComparison.OrdinalIgnoreCase))
                continue;

            var price = title.ParentElement?.QuerySelector(PriceBoxPrice);
            if (price is null || !PriceText.TryParse(price.TextContent, out var value))
                continue;

            // A "members' price" that is not below the normal one means we latched
            // onto the wrong box; report nothing rather than a misleading equal value.
            if (regularPrice is not null && value >= regularPrice)
                continue;

            return value;
        }

        return null;
    }

    private static ScrapeResult ReadProduct(JsonElement product)
    {
        var name = GetString(product, "name");
        var imageUrl = ReadImage(product);

        decimal? price = null;
        decimal? couponPrice = null;
        string? currency = null;
        string? availability = null;

        if (product.TryGetProperty("offers", out var offers))
        {
            // "offers" is an Offer object on most pages, but schema.org permits an
            // array (e.g. several sellers), so handle both.
            var offer = offers.ValueKind == JsonValueKind.Array
                ? offers.EnumerateArray().FirstOrDefault()
                : offers;

            if (offer.ValueKind == JsonValueKind.Object)
            {
                price = ReadPrice(offer);
                couponPrice = ReadCouponPrice(offer, price);
                currency = GetString(offer, "priceCurrency");
                availability = StripSchemaPrefix(GetString(offer, "availability"));
            }
        }

        if (name is null && price is null)
            return ScrapeResult.Failure(ScrapeStatus.ParseFailed, "Product JSON-LD block had neither a name nor a price.");

        return new ScrapeResult(
            ScrapeStatus.Success,
            Name: name,
            Price: price,
            CouponPrice: couponPrice,
            Currency: currency,
            Availability: availability,
            ImageUrl: imageUrl);
    }

    /// <summary>
    /// Finds the price a discount code buys ("s kódom od …"), which alza.sk
    /// publishes as a schema.org SalePrice alongside the RegularPrice.
    ///
    /// Pages with no code offer carry a single SalePrice equal to offers.price, so
    /// the discount is only real when the SalePrice is strictly lower.
    /// </summary>
    private static decimal? ReadCouponPrice(JsonElement offer, decimal? regularPrice)
    {
        if (!offer.TryGetProperty("priceSpecification", out var spec))
            return null;

        foreach (var entry in EnumerateNodes(spec))
        {
            var priceType = GetString(entry, "priceType");
            if (priceType is null || !priceType.EndsWith("SalePrice", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryReadDecimal(entry, "price", out var value))
                continue;

            if (regularPrice is not null && value >= regularPrice)
                continue;

            return value;
        }

        return null;
    }

    private static decimal? ReadPrice(JsonElement offer)
    {
        if (TryReadDecimal(offer, "price", out var direct))
            return direct;

        // Fall back to the priceSpecification list when the offer omits a flat price.
        if (offer.TryGetProperty("priceSpecification", out var spec))
        {
            foreach (var entry in EnumerateNodes(spec))
            {
                if (TryReadDecimal(entry, "price", out var fromSpec))
                    return fromSpec;
            }
        }

        return null;
    }

    private static bool TryReadDecimal(JsonElement node, string property, out decimal value)
    {
        value = default;

        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(property, out var raw))
            return false;

        return raw.ValueKind switch
        {
            JsonValueKind.Number => raw.TryGetDecimal(out value),
            // Prices are sometimes quoted as strings ("18.90").
            JsonValueKind.String => decimal.TryParse(
                raw.GetString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    /// <summary>"image" may be a URL string, an array of strings, or ImageObject nodes.</summary>
    private static string? ReadImage(JsonElement product)
    {
        if (!product.TryGetProperty("image", out var image))
            return null;

        foreach (var node in EnumerateNodes(image))
        {
            var url = node.ValueKind switch
            {
                JsonValueKind.String => node.GetString(),
                JsonValueKind.Object => GetString(node, "url"),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    /// <summary>Turns "https://schema.org/InStock" into "InStock".</summary>
    private static string? StripSchemaPrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.StartsWith(SchemaPrefix, StringComparison.OrdinalIgnoreCase)
            ? value[SchemaPrefix.Length..]
            : value;
    }

    private static string? GetString(JsonElement node, string property) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
