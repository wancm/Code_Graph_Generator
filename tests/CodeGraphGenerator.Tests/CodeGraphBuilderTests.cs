using CodeGraphGenerator.Core;

namespace CodeGraphGenerator.Tests;

/// <summary>Reads the SampleShop fixture with Roslyn and checks the types, links and reference states it finds.</summary>
public sealed class CodeGraphBuilderTests(SampleShopGraph shop) : IClassFixture<SampleShopGraph>
{
    [Fact]
    public void Every_type_is_found_once_with_its_kind()
    {
        Assert.Equal(NodeKind.Record, shop.Node("Order").Kind);
        Assert.Equal(NodeKind.Enum, shop.Node("OrderStatus").Kind);
        Assert.Equal(NodeKind.Interface, shop.Node("IOrderRepository").Kind);
        Assert.Equal(NodeKind.Class, shop.Node("OrderService").Kind);

        string[] expected =
        [
            "Order", "OrderStatus", "IOrderRepository", "OrderService", "InMemoryOrderRepository", "UnusedHelper",
            "PriceFormatter", "Program", "Badge", "OrdersPage", "PriceFormatterChecks"
        ];
        Assert.Equal(expected.Order(), shop.Graph.Nodes.Select(n => n.Name).Order());
    }

    [Fact]
    public void Implementing_an_interface_is_an_implements_link()
    {
        Assert.Equal(EdgeKind.Implements, shop.Edge("InMemoryOrderRepository", "IOrderRepository")?.Kind);
    }

    [Fact]
    public void Calling_a_method_or_creating_an_object_is_a_calls_link()
    {
        Assert.Equal(EdgeKind.Calls, shop.Edge("OrderService", "IOrderRepository")?.Kind);
        Assert.Equal(EdgeKind.Calls, shop.Edge("OrderService", "Order")?.Kind);
        Assert.Equal(EdgeKind.Calls, shop.Edge("Program", "OrderService")?.Kind);
        Assert.Equal(EdgeKind.Calls, shop.Edge("Program", "InMemoryOrderRepository")?.Kind);
    }

    [Fact]
    public void Naming_a_type_or_reading_an_enum_value_is_a_uses_link()
    {
        Assert.Equal(EdgeKind.Uses, shop.Edge("Order", "OrderStatus")?.Kind);
        Assert.Equal(EdgeKind.Uses, shop.Edge("IOrderRepository", "Order")?.Kind);
    }

    [Fact]
    public void A_razor_page_that_renders_a_component_calls_it_and_maps_to_the_razor_file()
    {
        Assert.Equal(EdgeKind.Calls, shop.Edge("OrdersPage", "Badge")?.Kind);
        Assert.NotNull(shop.Edge("OrdersPage", "Order"));
        Assert.Equal("Shop.Web/Badge.razor", shop.Node("Badge").Files.Single().File);
        Assert.Equal("Shop.Web/OrdersPage.razor", shop.Node("OrdersPage").Files.Single().File);
    }

    [Fact]
    public void Each_link_says_where_it_happens()
    {
        SourceLocation where = shop.Edge("OrderService", "IOrderRepository")!.Where[0];

        Assert.Equal("Shop.App/OrderService.cs", where.File);
        Assert.Equal(5, where.Line);
    }

    [Fact]
    public void Using_directives_are_not_links()
    {
        // Program.cs starts with "using Shop.App;", which names a namespace, not a type, and sits outside any type.
        Assert.All(shop.Graph.Edges, e => Assert.NotEmpty(e.Where));
        Assert.Null(shop.Edge("PriceFormatterChecks", "OrderService"));
    }

    [Fact]
    public void Reference_states_find_dead_code_entry_points_and_test_only_types()
    {
        Assert.Equal(ReferenceState.EntryPoint, shop.Node("Program").Reference);
        Assert.Equal(ReferenceState.EntryPoint, shop.Node("OrdersPage").Reference);
        Assert.Equal(ReferenceState.Unreferenced, shop.Node("UnusedHelper").Reference);
        Assert.Equal(ReferenceState.TestsOnly, shop.Node("PriceFormatter").Reference);
        Assert.Equal(ReferenceState.TestCode, shop.Node("PriceFormatterChecks").Reference);
        Assert.Equal(ReferenceState.Referenced, shop.Node("Badge").Reference);
    }

    [Fact]
    public void Projects_are_named_and_test_projects_flagged()
    {
        Assert.Equal(
            ["Shop.App", "Shop.Domain", "Shop.Tests", "Shop.Web"],
            shop.Graph.Projects.Select(p => p.Name));
        Assert.True(shop.Graph.Projects.Single(p => p.Name == "Shop.Tests").IsTest);
        Assert.False(shop.Graph.Projects.Single(p => p.Name == "Shop.App").IsTest);
    }

    [Fact]
    public void A_restored_code_base_gives_no_warnings()
    {
        Assert.Empty(shop.Graph.Warnings);
    }
}
