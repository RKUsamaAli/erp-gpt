using System.ComponentModel.DataAnnotations.Schema;

namespace ErpGpt.GraphQLApi.Domain;

// Tables in the `production` schema. Only the catalogue is in scope —
// manufacturing, work orders and inventory are deliberately not mapped.

[GraphQLDescription("An item we sell, e.g. 'Mountain-200 Black, 42'.")]
public class Product
{
    public int ProductId { get; set; }

    [GraphQLDescription("Product name as shown to customers.")]
    public string Name { get; set; } = null!;

    [GraphQLDescription("Stock-keeping code, unique per product, e.g. 'BK-M68B-42'.")]
    public string ProductNumber { get; set; } = null!;

    [GraphQLDescription("Current selling price. Order lines keep the price at time of sale in UnitPrice instead.")]
    public decimal ListPrice { get; set; }

    [GraphQLDescription("What the product costs us. ListPrice minus StandardCost is the margin.")]
    public decimal StandardCost { get; set; }

    [GraphQLDescription("Colour, e.g. 'Black', 'Red'. Null for products where colour does not apply.")]
    public string? Color { get; set; }

    [GraphQLDescription("Size as text, e.g. '42', 'L'. Null when not applicable.")]
    public string? Size { get; set; }

    [GraphQLDescription("Weight of the product. Null when not recorded.")]
    public decimal? Weight { get; set; }

    [GraphQLDescription("True when we manufacture it ourselves, false when we buy it in.")]
    public bool MakeFlag { get; set; }

    [GraphQLDescription("True when this is a finished item we sell, rather than a component.")]
    public bool FinishedGoodsFlag { get; set; }

    [GraphQLDescription("Date we started selling it.")]
    public DateTime SellStartDate { get; set; }

    [GraphQLDescription("Date we stopped selling it. Null means still on sale.")]
    public DateTime? SellEndDate { get; set; }

    [GraphQLDescription("Date the product was discontinued, if it was.")]
    public DateTime? DiscontinuedDate { get; set; }

    [GraphQLDescription("True when the product is still being sold today.")]
    [NotMapped]
    public bool IsCurrentlySold => SellEndDate == null && DiscontinuedDate == null;

    [GraphQLDescription("Sub-category such as 'Mountain Bikes'. Null for a few uncategorised products.")]
    public int? ProductSubcategoryId { get; set; }
    public ProductSubcategory? Subcategory { get; set; }

    public List<SalesOrderLine> OrderLines { get; set; } = [];
}

[GraphQLDescription("A product sub-category such as 'Road Bikes', 'Helmets' or 'Tires and Tubes'.")]
public class ProductSubcategory
{
    public int ProductSubcategoryId { get; set; }

    public string Name { get; set; } = null!;

    public int ProductCategoryId { get; set; }
    public ProductCategory Category { get; set; } = null!;

    public List<Product> Products { get; set; } = [];
}

[GraphQLDescription("A top-level product category. There are exactly four: Bikes, Components, Clothing, Accessories.")]
public class ProductCategory
{
    public int ProductCategoryId { get; set; }

    public string Name { get; set; } = null!;

    public List<ProductSubcategory> Subcategories { get; set; } = [];
}
