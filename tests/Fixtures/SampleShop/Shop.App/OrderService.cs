using Shop.Domain;

namespace Shop.App;

public sealed class OrderService(IOrderRepository repository)
{
    public async Task<Order> PlaceAsync(decimal total)
    {
        var order = new Order(1, total);
        await repository.SaveAsync(order);
        return order;
    }
}
