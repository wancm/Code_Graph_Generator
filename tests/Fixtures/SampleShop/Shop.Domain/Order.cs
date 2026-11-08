namespace Shop.Domain;

public sealed record Order(int Id, decimal Total)
{
    public OrderStatus Status { get; init; } = OrderStatus.New;
}
