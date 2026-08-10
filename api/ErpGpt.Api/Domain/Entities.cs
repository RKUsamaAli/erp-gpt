namespace ErpGpt.Api.Domain;

/// <summary>
/// Attaches a plain-English meaning to an entity, property, or enum member.
/// The Metadata Generator harvests these into the AI knowledge base, so
/// meanings live NEXT TO the code and change in the same PR — they cannot
/// drift the way a separate document would.
/// Write them for a salesperson, not a developer.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SemanticAttribute(string meaning) : Attribute
{
    public string Meaning { get; } = meaning;
}

// ---------------------------------------------------------------- lookups

[Semantic("A sales region grouping customers geographically, e.g. Riyadh, Jeddah, Eastern Province.")]
public class Region
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<Customer> Customers { get; set; } = [];
}

[Semantic("A product category such as Electronics, Furniture, or Consumables.")]
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<Product> Products { get; set; } = [];
}

// ---------------------------------------------------------------- parties

[Semantic("A business customer that places orders and receives invoices.")]
public class Customer
{
    public int Id { get; set; }

    [Semantic("Trading name of the customer company.")]
    public string Name { get; set; } = null!;

    [Semantic("City where the customer is based. Used for city-level filters like 'customers in Riyadh'.")]
    public string City { get; set; } = null!;

    public int RegionId { get; set; }
    public Region Region { get; set; } = null!;

    [Semantic("False means the customer is dormant/closed and should be excluded when the user asks for 'active customers'.")]
    public bool IsActive { get; set; } = true;

    [Semantic("Date the customer account was opened.")]
    public DateOnly CreatedOn { get; set; }

    public List<Order> Orders { get; set; } = [];
}

[Semantic("A supplier we purchase stock from.")]
public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string City { get; set; } = null!;

    [Semantic("False means we no longer trade with this supplier.")]
    public bool IsActive { get; set; } = true;

    public List<Product> Products { get; set; } = [];
}

// ---------------------------------------------------------------- catalogue

[Semantic("An item we sell. Belongs to one category and one primary supplier.")]
public class Product
{
    public int Id { get; set; }

    [Semantic("Stock-keeping unit code, unique per product, e.g. SKU-1042.")]
    public string Sku { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    [Semantic("Current selling price per unit. Historical order lines keep the price at time of sale in UnitPrice.")]
    public decimal ListPrice { get; set; }

    [Semantic("False means discontinued — exclude when the user asks for 'active products'.")]
    public bool IsActive { get; set; } = true;

    public List<OrderLine> OrderLines { get; set; } = [];
    public List<StockItem> StockItems { get; set; } = [];
}

// ---------------------------------------------------------------- sales

[Semantic("Lifecycle status of an order.")]
public enum OrderStatus
{
    [Semantic("Placed but not yet confirmed.")] Pending = 0,
    [Semantic("Confirmed and being processed.")] Confirmed = 1,
    [Semantic("Left the warehouse.")] Shipped = 2,
    [Semantic("Received by the customer. A completed sale.")] Delivered = 3,
    [Semantic("Voided. Cancelled orders must be EXCLUDED from all revenue and sales figures.")] Cancelled = 4,
}

[Semantic("A customer order. Revenue figures are the sum of its lines, excluding Cancelled orders.")]
public class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    [Semantic("Date the order was placed. All date-range questions about sales use this column.")]
    public DateOnly OrderDate { get; set; }

    public OrderStatus Status { get; set; }

    public List<OrderLine> Lines { get; set; } = [];
    public Invoice? Invoice { get; set; }
}

[Semantic("One product line on an order. Line value = Quantity × UnitPrice.")]
public class OrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    [Semantic("Price per unit AT TIME OF SALE — may differ from the product's current ListPrice.")]
    public decimal UnitPrice { get; set; }
}

[Semantic("The bill issued for an order. Outstanding/overdue questions use DueDate and PaidOn.")]
public class Invoice
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [Semantic("Date the invoice was issued.")]
    public DateOnly IssuedOn { get; set; }

    [Semantic("Payment deadline. An invoice is overdue when DueDate is past and PaidOn is null.")]
    public DateOnly DueDate { get; set; }

    [Semantic("Null means unpaid. 'Outstanding' = PaidOn is null.")]
    public DateOnly? PaidOn { get; set; }

    public decimal Amount { get; set; }
}

// ---------------------------------------------------------------- inventory

[Semantic("Stock of one product held in one warehouse.")]
public class StockItem
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Semantic("Warehouse identifier, e.g. WH-RUH, WH-JED.")]
    public string WarehouseCode { get; set; } = null!;

    [Semantic("Units currently on hand.")]
    public int QuantityOnHand { get; set; }

    [Semantic("When QuantityOnHand falls at or below this, the product is 'low stock'.")]
    public int ReorderLevel { get; set; }
}

[Semantic("Direction and cause of a stock movement.")]
public enum MovementType
{
    [Semantic("Stock received from a supplier (+).")] Purchase = 0,
    [Semantic("Stock sold to a customer (−).")] Sale = 1,
    [Semantic("Manual correction (+/−).")] Adjustment = 2,
}

[Semantic("A single change to stock levels: purchases in, sales out, adjustments either way.")]
public class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [Semantic("When the movement happened.")]
    public DateTime MovedAt { get; set; }

    [Semantic("Signed quantity: positive into stock, negative out.")]
    public int Quantity { get; set; }

    public MovementType Type { get; set; }
}
