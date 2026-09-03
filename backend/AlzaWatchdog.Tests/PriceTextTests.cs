using AlzaWatchdog.Api.Scraping;

namespace AlzaWatchdog.Tests;

public class PriceTextTests
{
    [Theory]
    [InlineData("10,98 €", 10.98)]
    [InlineData("12,20 €", 12.20)]          // non-breaking space before the symbol
    [InlineData("1 149,00 €", 1149.00)]          // space as thousand separator
    [InlineData("1 149,00 €", 1149.00)]
    [InlineData("1 149,00 €", 1149.00)]     // narrow no-break space
    [InlineData("  \n 44,90 € \t ", 44.90)]      // markup whitespace
    [InlineData("899 €", 899)]                   // no decimals
    [InlineData("bez DPH 9,92 €", 9.92)]
    public void Parses_slovak_price_text(string input, double expected)
    {
        Assert.True(PriceText.TryParse(input, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("€")]
    [InlineData("zadarmo")]
    [InlineData("1,2,3")]
    public void Rejects_anything_that_is_not_a_price(string? input)
    {
        Assert.False(PriceText.TryParse(input, out _));
    }
}
