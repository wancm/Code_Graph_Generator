namespace Shop.Domain;

public interface IOrderRepository
{
    Task SaveAsync(Order order);
}
