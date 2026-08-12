using System.ComponentModel.DataAnnotations.Schema;

namespace ErpGpt.GraphQLApi.Domain;

// Tables in the `person` schema. These exist mainly to give customers and
// salespeople a human-readable name and a location to filter on.

[GraphQLDescription("A named individual. Customers who are people, and every salesperson, get their name from here.")]
public class Person
{
    [GraphQLDescription("Primary key. Shared with Customer.PersonId and SalesPerson.BusinessEntityId.")]
    public int BusinessEntityId { get; set; }

    [GraphQLDescription("Two-letter role code: IN = individual customer, SC = store contact, SP = salesperson, EM = employee.")]
    public string PersonType { get; set; } = null!;

    public string FirstName { get; set; } = null!;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = null!;

    [GraphQLDescription("Full name, first and last joined. Use this when the user asks for a customer or salesperson by name.")]
    [NotMapped]
    public string FullName => $"{FirstName} {LastName}";
}

[GraphQLDescription("A street address. Orders reference one for billing and one for shipping.")]
public class Address
{
    public int AddressId { get; set; }

    public string AddressLine1 { get; set; } = null!;
    public string? AddressLine2 { get; set; }

    [GraphQLDescription("City the address is in. Use for city-level questions like 'orders shipped to Seattle'.")]
    public string City { get; set; } = null!;

    public string PostalCode { get; set; } = null!;

    public int StateProvinceId { get; set; }
    public StateProvince StateProvince { get; set; } = null!;
}

[GraphQLDescription("A state or province, e.g. Washington or Ontario. Gives orders and customers a geographic grouping above city.")]
public class StateProvince
{
    public int StateProvinceId { get; set; }

    [GraphQLDescription("Full state or province name, e.g. 'Washington'.")]
    public string Name { get; set; } = null!;

    [GraphQLDescription("Short code, e.g. 'WA'.")]
    public string StateProvinceCode { get; set; } = null!;

    [GraphQLDescription("Three-letter country code, e.g. 'US', 'CA', 'GB'.")]
    public string CountryRegionCode { get; set; } = null!;

    public int TerritoryId { get; set; }
    public Territory Territory { get; set; } = null!;
}
