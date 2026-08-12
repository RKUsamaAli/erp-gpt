using System.ComponentModel.DataAnnotations.Schema;

namespace ErpGpt.GraphQLApi.Domain;

// Tables in the `sales` schema — the core of every revenue question.

[GraphQLDescription("Someone we sell to. A customer is EITHER a person (retail) OR a store (reseller), never both roles at once — the name lives in the linked Person or Store, which is why DisplayName exists.")]
public class Customer
{
    public int CustomerId { get; set; }

    [GraphQLDescription("Set when this customer is an individual shopper.")]
    public int? PersonId { get; set; }
    public Person? Person { get; set; }

    [GraphQLDescription("Set when this customer is a reseller/shop rather than an individual.")]
    public int? StoreId { get; set; }
    public Store? Store { get; set; }

    [GraphQLDescription("Sales region this customer belongs to.")]
    public int? TerritoryId { get; set; }
    public Territory? Territory { get; set; }

    [GraphQLDescription("The customer's name, whether they are a store or a person. Always use this for 'which customer' answers.")]
    [NotMapped]
    public string DisplayName =>
        Store is not null ? Store.Name
        : Person is not null ? $"{Person.FirstName} {Person.LastName}"
        : $"Customer {CustomerId}";

    [GraphQLDescription("True when this customer is a reseller/store rather than an individual shopper.")]
    [NotMapped]
    public bool IsStore => StoreId != null;

    public List<SalesOrder> Orders { get; set; } = [];
}

[GraphQLDescription("A reseller or shop that buys from us. Stores are customers with a company name.")]
public class Store
{
    [GraphQLDescription("Primary key. Matches Customer.StoreId.")]
    public int BusinessEntityId { get; set; }

    [GraphQLDescription("Trading name of the store, e.g. 'A Bike Store'.")]
    public string Name { get; set; } = null!;

    [GraphQLDescription("The salesperson who owns this account.")]
    public int? SalesPersonId { get; set; }
    public SalesPerson? SalesPerson { get; set; }
}

[GraphQLDescription("A member of the sales team. Revenue can be attributed to them through orders.")]
public class SalesPerson
{
    [GraphQLDescription("Primary key. Also the Person id — join to Person for their name.")]
    public int BusinessEntityId { get; set; }
    public Person Person { get; set; } = null!;

    [GraphQLDescription("The region this salesperson covers. Null means they are not tied to one region.")]
    public int? TerritoryId { get; set; }
    public Territory? Territory { get; set; }

    [GraphQLDescription("Their sales target. Null means no quota was set.")]
    public decimal? SalesQuota { get; set; }

    public decimal Bonus { get; set; }

    [GraphQLDescription("Commission rate as a fraction, e.g. 0.012 means 1.2%.")]
    public decimal CommissionPct { get; set; }

    [GraphQLDescription("Sales year-to-date, as stored by the ERP. This is a snapshot column, NOT computed from orders — for live figures use the aggregation endpoints.")]
    public decimal SalesYtd { get; set; }

    public decimal SalesLastYear { get; set; }
}

[GraphQLDescription("A sales region such as Northwest, Canada, or United Kingdom. This is the grouping used for 'sales by region' questions.")]
public class Territory
{
    public int TerritoryId { get; set; }

    [GraphQLDescription("Region name, e.g. 'Northwest', 'Canada', 'Australia'.")]
    public string Name { get; set; } = null!;

    [GraphQLDescription("Three-letter country code, e.g. 'US', 'CA'.")]
    public string CountryRegionCode { get; set; } = null!;

    [GraphQLDescription("Continent-level grouping, e.g. 'North America', 'Europe', 'Pacific'.")]
    public string Group { get; set; } = null!;

    [GraphQLDescription("Sales year-to-date as stored by the ERP — a snapshot column, not computed from orders.")]
    public decimal SalesYtd { get; set; }

    public decimal SalesLastYear { get; set; }
}

[GraphQLDescription("One customer order. TotalDue is the amount actually billed, and is what 'how much did we sell' should sum.")]
public class SalesOrder
{
    public int SalesOrderId { get; set; }

    [GraphQLDescription("Date the order was placed. Every date-range question about sales uses this column.")]
    public DateTime OrderDate { get; set; }

    [GraphQLDescription("Date payment/delivery is due.")]
    public DateTime DueDate { get; set; }

    [GraphQLDescription("Date the order left the warehouse. Null means not yet shipped.")]
    public DateTime? ShipDate { get; set; }

    [GraphQLDescription("Order lifecycle code: 1 In process, 2 Approved, 3 Backordered, 4 Rejected, 5 Shipped, 6 Cancelled. NOTE: every order in this database is 5 (Shipped).")]
    public short Status { get; set; }

    [GraphQLDescription("True when the customer ordered through the website rather than a salesperson.")]
    public bool OnlineOrderFlag { get; set; }

    [GraphQLDescription("The customer's own purchase-order reference, if they gave one.")]
    public string? PurchaseOrderNumber { get; set; }

    [GraphQLDescription("Order value before tax and freight.")]
    public decimal SubTotal { get; set; }

    [GraphQLDescription("Tax charged on this order.")]
    public decimal TaxAmt { get; set; }

    [GraphQLDescription("Shipping cost charged on this order.")]
    public decimal Freight { get; set; }

    [GraphQLDescription("Grand total billed: SubTotal + TaxAmt + Freight. SUM this for revenue questions.")]
    public decimal? TotalDue { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    [GraphQLDescription("Salesperson who took the order. Null for online orders.")]
    public int? SalesPersonId { get; set; }
    public SalesPerson? SalesPerson { get; set; }

    [GraphQLDescription("Sales region the order belongs to.")]
    public int? TerritoryId { get; set; }
    public Territory? Territory { get; set; }

    [GraphQLDescription("Where the order was shipped to.")]
    public int ShipToAddressId { get; set; }
    public Address ShipToAddress { get; set; } = null!;

    [GraphQLDescription("The individual product lines on this order.")]
    public List<SalesOrderLine> Lines { get; set; } = [];
}

[GraphQLDescription("One product line on an order. Line value = OrderQty x UnitPrice x (1 - UnitPriceDiscount).")]
public class SalesOrderLine
{
    public int SalesOrderId { get; set; }
    public SalesOrder Order { get; set; } = null!;

    [GraphQLDescription("Second half of this row's composite key — unique within the order.")]
    public int SalesOrderDetailId { get; set; }

    [GraphQLDescription("Units of this product ordered.")]
    public short OrderQty { get; set; }

    [GraphQLDescription("Price per unit AT TIME OF SALE — may differ from the product's current ListPrice.")]
    public decimal UnitPrice { get; set; }

    [GraphQLDescription("Discount as a fraction, e.g. 0.10 means 10% off. Usually 0.")]
    public decimal UnitPriceDiscount { get; set; }

    [GraphQLDescription("What this line is actually worth after discount.")]
    [NotMapped]
    public decimal LineTotal => OrderQty * UnitPrice * (1 - UnitPriceDiscount);

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [GraphQLDescription("Courier tracking reference, when the line has shipped.")]
    public string? CarrierTrackingNumber { get; set; }
}
