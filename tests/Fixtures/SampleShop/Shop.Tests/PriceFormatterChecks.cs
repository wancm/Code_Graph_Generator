using Shop.App;

namespace Shop.Tests;

public sealed class PriceFormatterChecks
{
    public string Run() => PriceFormatter.Format(1m);
}
