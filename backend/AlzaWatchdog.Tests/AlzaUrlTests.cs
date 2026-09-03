using AlzaWatchdog.Api.Scraping;

namespace AlzaWatchdog.Tests;

public class AlzaUrlTests
{
    private const string Sample = "https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm";

    [Fact]
    public void Parses_a_product_url()
    {
        Assert.True(AlzaUrl.TryParse(Sample, out var product));
        Assert.Equal("10818009", product.ProductCode);
        Assert.Equal(Sample, product.CanonicalUrl);
    }

    [Theory]
    [InlineData("https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm?utm_source=newsletter")]
    [InlineData("https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm#reviews")]
    [InlineData("https://alza.sk/cudy-n300-wifi-router-d10818009.htm")]
    [InlineData("www.alza.sk/cudy-n300-wifi-router-d10818009.htm")]
    [InlineData("  https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm  ")]
    public void Canonicalises_variants_to_one_url(string input)
    {
        Assert.True(AlzaUrl.TryParse(input, out var product));
        Assert.Equal(Sample, product.CanonicalUrl);
        Assert.Equal("10818009", product.ProductCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://www.amazon.de/some-product-d123.htm")]     // wrong host
    [InlineData("https://www.alza.sk/routery/18849337.htm")]        // category, no -d<code>
    [InlineData("https://www.alza.sk/cudy-n300-wifi-router.htm")]   // no product code
    [InlineData("ftp://www.alza.sk/cudy-n300-wifi-router-d10818009.htm")]
    public void Rejects_anything_that_is_not_an_alza_product_url(string? input)
    {
        Assert.False(AlzaUrl.TryParse(input, out _));
    }
}
