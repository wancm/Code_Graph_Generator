namespace Shop.App;

public static class PriceFormatter
{
    public static string Format(decimal price) => price.ToString("N2");
}
