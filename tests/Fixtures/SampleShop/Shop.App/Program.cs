using Shop.App;

var service = new OrderService(new InMemoryOrderRepository());
await service.PlaceAsync(42m);
