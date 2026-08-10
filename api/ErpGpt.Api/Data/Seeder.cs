using ErpGpt.Api.Domain;

namespace ErpGpt.Api.Data;

/// <summary>
/// Deterministic seed: fixed Random(42), so every developer gets identical
/// data and eval expectations stay reproducible. Implements db/README rules:
/// realistic names, two full years of history, ~8% cancelled orders, nulls,
/// and deliberate edge cases (a customer with zero orders, a product never
/// sold, an order with a single line).
/// </summary>
public static class Seeder
{
    public static void Seed(ErpDbContext db)
    {
        if (db.Customers.Any()) return; // idempotent

        var rng = new Random(42);
        var today = new DateOnly(2026, 8, 10);
        var start = today.AddYears(-2);

        // ---- lookups
        var regions = new[] { "Riyadh", "Jeddah", "Eastern Province", "Asir", "Madinah" }
            .Select(n => new Region { Name = n }).ToList();
        var categories = new[] { "Electronics", "Furniture", "Consumables", "Networking", "Safety Equipment" }
            .Select(n => new Category { Name = n }).ToList();
        db.AddRange(regions); db.AddRange(categories);

        // ---- suppliers (10, one inactive)
        string[] supplierNames = ["Gulf Components", "Almasa Trading", "Nordwind Imports", "Falcon Industrial",
            "Red Sea Logistics", "Atlas Wholesale", "Petra Supplies", "Orbit Electronics", "Crescent Materials", "Zenith Partners"];
        string[] cities = ["Riyadh", "Jeddah", "Dammam", "Abha", "Madinah", "Khobar"];
        var suppliers = supplierNames.Select((n, i) => new Supplier
        {
            Name = n, City = cities[rng.Next(cities.Length)], IsActive = i != 9
        }).ToList();
        db.AddRange(suppliers);

        // ---- products (20; the LAST one is never sold — edge case)
        string[] productNames = ["Mesh Router X2", "Office Chair Pro", "Thermal Printer", "CAT6 Cable 305m",
            "Safety Helmet", "Standing Desk", "Barcode Scanner", "UPS 1500VA", "LED Panel 60x60", "Hand Pallet Truck",
            "Access Point AC", "Filing Cabinet", "Label Rolls (12pk)", "Fibre Patch Panel", "Hi-Vis Vest (10pk)",
            "Conference Table", "POS Terminal", "Rack Cabinet 42U", "Whiteboard 180cm", "Dormant Legacy Sensor"];
        var products = productNames.Select((n, i) => new Product
        {
            Sku = $"SKU-{1000 + i}",
            Name = n,
            Category = categories[i % categories.Count],
            Supplier = suppliers[i % suppliers.Count],
            ListPrice = Math.Round((decimal)(rng.NextDouble() * 950 + 50), 2),
            IsActive = i != 19, // the never-sold product is also discontinued
        }).ToList();
        db.AddRange(products);

        // ---- customers (50; index 0 has ZERO orders — edge case)
        string[] first = ["Alfa", "Basma", "Cedar", "Delta", "Emaar", "Fajr", "Golden", "Horizon", "Ibis", "Jasper",
            "Kite", "Lulu", "Meridian", "Nadir", "Oasis", "Pearl", "Qamar", "Rawabi", "Safa", "Tala",
            "Ummah", "Vega", "Wadi", "Xenon", "Yara"];
        string[] second = ["Trading", "Group", "Holdings", "Retail", "Logistics", "Foods", "Medical", "Contracting", "Motors", "Tech"];
        var customers = Enumerable.Range(0, 50).Select(i => new Customer
        {
            Name = $"{first[i % first.Length]} {second[(i / first.Length + i) % second.Length]}",
            City = cities[rng.Next(cities.Length)],
            Region = regions[rng.Next(regions.Count)],
            IsActive = rng.NextDouble() > 0.12, // some dormant
            CreatedOn = start.AddDays(rng.Next(0, 200)),
        }).ToList();
        db.AddRange(customers);

        // ---- orders (~2000 over two years; ~8% cancelled)
        var totalDays = today.DayNumber - start.DayNumber;
        var sellable = products.Take(19).ToList(); // product 20 never sold
        var orders = new List<Order>();

        for (var i = 0; i < 2000; i++)
        {
            var customer = customers[rng.Next(1, customers.Count)]; // customer 0 never orders
            var date = start.AddDays(rng.Next(totalDays));
            var status = rng.NextDouble() switch
            {
                < 0.08 => OrderStatus.Cancelled,
                < 0.12 => OrderStatus.Pending,
                < 0.20 => OrderStatus.Shipped,
                < 0.25 => OrderStatus.Confirmed,
                _ => OrderStatus.Delivered,
            };

            var lineCount = i == 0 ? 1 : rng.Next(1, 6); // first order: single line — edge case
            var order = new Order { Customer = customer, OrderDate = date, Status = status };
            foreach (var _ in Enumerable.Range(0, lineCount))
            {
                var p = sellable[rng.Next(sellable.Count)];
                order.Lines.Add(new OrderLine
                {
                    Product = p,
                    Quantity = rng.Next(1, 12),
                    UnitPrice = Math.Round(p.ListPrice * (decimal)(0.85 + rng.NextDouble() * 0.3), 2),
                });
            }

            // invoice for everything except cancelled/pending; ~15% still unpaid
            if (status is not (OrderStatus.Cancelled or OrderStatus.Pending))
            {
                var amount = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
                var due = date.AddDays(30);
                order.Invoice = new Invoice
                {
                    IssuedOn = date,
                    DueDate = due,
                    Amount = amount,
                    PaidOn = rng.NextDouble() < 0.85 && due < today
                        ? due.AddDays(rng.Next(-10, 25))
                        : null, // null = outstanding
                };
            }
            orders.Add(order);
        }
        db.AddRange(orders);

        // ---- stock (two warehouses; some items at/below reorder level)
        string[] warehouses = ["WH-RUH", "WH-JED"];
        foreach (var p in products)
        foreach (var w in warehouses)
        {
            var reorder = rng.Next(5, 30);
            db.Add(new StockItem
            {
                Product = p,
                WarehouseCode = w,
                ReorderLevel = reorder,
                QuantityOnHand = rng.NextDouble() < 0.2 ? rng.Next(0, reorder + 1) : rng.Next(reorder + 1, 200),
            });
        }

        // ---- stock movements (light history, last 90 days)
        foreach (var p in sellable)
            for (var i = 0; i < 12; i++)
            {
                var type = (MovementType)rng.Next(0, 3);
                var qty = rng.Next(1, 40);
                db.Add(new StockMovement
                {
                    Product = p,
                    MovedAt = DateTime.SpecifyKind(
                        today.AddDays(-rng.Next(0, 90)).ToDateTime(new TimeOnly(rng.Next(8, 18), 0)),
                        DateTimeKind.Utc),
                    Quantity = type == MovementType.Sale ? -qty
                             : type == MovementType.Purchase ? qty
                             : (rng.NextDouble() < 0.5 ? qty : -qty),
                    Type = type,
                });
            }

        db.SaveChanges();
    }
}
