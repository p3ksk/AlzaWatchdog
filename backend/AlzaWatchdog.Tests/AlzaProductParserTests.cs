using AlzaWatchdog.Api.Scraping;

namespace AlzaWatchdog.Tests;

public class AlzaProductParserTests
{
    [Fact]
    public void Reads_product_data_from_a_real_page()
    {
        var result = AlzaProductParser.Parse(Fixtures.Product());

        Assert.Equal(ScrapeStatus.Success, result.Status);
        Assert.Equal("CUDY N300 WiFi Router", result.Name);
        Assert.Equal(18.90m, result.Price);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal("InStock", result.Availability);   // schema.org prefix stripped
        Assert.StartsWith("https://image.alza.cz/", result.ImageUrl);
    }

    [Fact]
    public void Reads_the_alzaplus_members_price()
    {
        var result = AlzaProductParser.Parse(Fixtures.AlzaPlusProduct());

        Assert.Equal(ScrapeStatus.Success, result.Status);
        Assert.Equal("AlzaPower MagCore USB-C to USB-C 100W 1m čierny", result.Name);
        Assert.Equal(12.20m, result.Price);
        Assert.Equal(10.98m, result.PlusPrice);
    }

    [Fact]
    public void Reports_no_plus_price_when_the_page_offers_none()
    {
        // The Cudy page mentions "AlzaPlus+" once, in the site navigation. A plain
        // text search would find it and invent a discount that does not exist.
        var result = AlzaProductParser.Parse(Fixtures.Product());

        Assert.Equal(18.90m, result.Price);
        Assert.Null(result.PlusPrice);
    }

    [Fact]
    public void Ignores_a_plus_price_that_is_not_actually_cheaper()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {"@type":"Product","name":"Widget",
             "offers":{"@type":"Offer","price":10.00,"priceCurrency":"EUR"}}
            </script></head>
            <body>
              <div>
                <div data-slot="pb-title">-0 % s AlzaPlus+</div>
                <div><span data-slot="pb-price">10,00 €</span></div>
              </div>
            </body></html>
            """;

        var result = AlzaProductParser.Parse(html);

        Assert.Equal(10.00m, result.Price);
        Assert.Null(result.PlusPrice);
    }

    [Fact]
    public void Reads_the_discount_code_price()
    {
        var result = AlzaProductParser.Parse(Fixtures.CouponProduct());

        Assert.Equal(ScrapeStatus.Success, result.Status);
        Assert.Equal("AlzaErgo Chair Wave 1 Mesh čierna", result.Name);
        Assert.Equal(203.90m, result.Price);        // offers.price / RegularPrice
        Assert.Equal(142.73m, result.CouponPrice);  // the "s kódom" SalePrice
    }

    [Fact]
    public void Reports_no_coupon_when_the_sale_price_equals_the_normal_price()
    {
        // Pages with no code offer still carry a SalePrice — it just matches
        // offers.price, so there is no discount to report.
        foreach (var html in new[] { Fixtures.Product(), Fixtures.AlzaPlusProduct() })
        {
            Assert.Null(AlzaProductParser.Parse(html).CouponPrice);
        }
    }

    [Fact]
    public void Detects_the_cloudflare_interstitial()
    {
        var result = AlzaProductParser.Parse(Fixtures.Blocked());

        // The block page is sometimes served with a 200, so the body itself has to
        // give it away — otherwise it would look like an ordinary parse failure and
        // the worker would keep hammering instead of backing off.
        Assert.Equal(ScrapeStatus.Blocked, result.Status);
    }

    [Fact]
    public void Reports_a_parse_failure_when_there_is_no_json_ld()
    {
        var result = AlzaProductParser.Parse("<html><body><h1>Nothing here</h1></body></html>");

        Assert.Equal(ScrapeStatus.ParseFailed, result.Status);
    }

    [Fact]
    public void Ignores_json_ld_blocks_that_are_not_products()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">{"@context":"https://schema.org","@type":"BreadcrumbList"}</script>
            <script type="application/ld+json">not valid json at all</script>
            <script type="application/ld+json">
            {"@context":"https://schema.org","@type":"Product","name":"Widget",
             "offers":{"@type":"Offer","price":9.5,"priceCurrency":"EUR",
                       "availability":"https://schema.org/OutOfStock"}}
            </script>
            </head><body></body></html>
            """;

        var result = AlzaProductParser.Parse(html);

        Assert.Equal(ScrapeStatus.Success, result.Status);
        Assert.Equal("Widget", result.Name);
        Assert.Equal(9.5m, result.Price);
        Assert.Equal("OutOfStock", result.Availability);
    }

    [Fact]
    public void Handles_offers_given_as_an_array()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {"@type":"Product","name":"Widget",
             "offers":[{"@type":"Offer","price":"12.34","priceCurrency":"EUR"}]}
            </script></head><body></body></html>
            """;

        var result = AlzaProductParser.Parse(html);

        Assert.Equal(ScrapeStatus.Success, result.Status);
        Assert.Equal(12.34m, result.Price);   // quoted numbers parse too
    }

    [Fact]
    public void Falls_back_to_price_specification_when_the_offer_has_no_flat_price()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {"@type":"Product","name":"Widget",
             "offers":{"@type":"Offer","priceCurrency":"EUR",
                       "priceSpecification":[{"@type":"UnitPriceSpecification","price":41.5}]}}
            </script></head><body></body></html>
            """;

        var result = AlzaProductParser.Parse(html);

        Assert.Equal(41.5m, result.Price);
    }
}
