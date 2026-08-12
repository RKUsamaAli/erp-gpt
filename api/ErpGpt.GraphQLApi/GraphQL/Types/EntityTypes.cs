using ErpGpt.GraphQLApi.Domain;
using HotChocolate.Data;
using HotChocolate.Types.Pagination;

namespace ErpGpt.GraphQLApi.GraphQL;

// Projection reads ONLY the columns a query asks for, which is exactly what
// we want — except for computed fields like `displayName`, whose inputs are
// other columns the caller never mentioned. IsProjected(true) tells
// HotChocolate to fetch those inputs anyway, so a computed field cannot
// silently return a wrong answer built from unloaded data.

public class CustomerType : ObjectType<Customer>
{
    protected override void Configure(IObjectTypeDescriptor<Customer> d)
    {
        // isStore is built from storeId — a plain column, so projecting it is enough.
        d.Field(c => c.StoreId).IsProjected(true);
        d.Field(c => c.CustomerId).IsProjected(true);

        // displayName depends on the store/person RELATIONSHIPS, which
        // projection will not fetch unless the caller selects them. Marking
        // them IsProjected does not work for navigations, so the field is
        // resolved through a batched DataLoader instead — always correct,
        // and one extra query per page rather than one per row.
        d.Field(c => c.DisplayName)
            .Resolve(ctx => ctx
                .DataLoader<CustomerNameDataLoader>()
                .LoadAsync(ctx.Parent<Customer>().CustomerId, ctx.RequestAborted));
    }
}

public class PersonType : ObjectType<Person>
{
    protected override void Configure(IObjectTypeDescriptor<Person> d)
    {
        // fullName is built from first + last name.
        d.Field(p => p.FirstName).IsProjected(true);
        d.Field(p => p.LastName).IsProjected(true);
    }
}

public class SalesOrderLineType : ObjectType<SalesOrderLine>
{
    protected override void Configure(IObjectTypeDescriptor<SalesOrderLine> d)
    {
        // lineTotal is built from quantity, price and discount.
        d.Field(l => l.OrderQty).IsProjected(true);
        d.Field(l => l.UnitPrice).IsProjected(true);
        d.Field(l => l.UnitPriceDiscount).IsProjected(true);
    }
}

public class ProductType : ObjectType<Product>
{
    protected override void Configure(IObjectTypeDescriptor<Product> d)
    {
        // isCurrentlySold is built from the two end dates.
        d.Field(p => p.SellEndDate).IsProjected(true);
        d.Field(p => p.DiscontinuedDate).IsProjected(true);

        // A popular product appears on up to 4,688 order lines, so exposing
        // this navigation would let products(take:100){ orderLines } return
        // hundreds of thousands of rows in one response. Paging the nested
        // field does not work here — HotChocolate's paging middleware returns
        // an empty page once the parent resolver has been projected — so the
        // navigation is hidden instead. The top-level endpoint covers the
        // same need and IS paged:
        //
        //   orderLines(where: { productId: { eq: 782 } }, take: 25)
        d.Ignore(p => p.OrderLines);
    }
}
