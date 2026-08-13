using System.Text.Json;

namespace ErpGpt.Agent.Services;

public class GraphQLValidatorAndBuilder
{
    public string BuildGraphQLQuery(string queryPlanJson, EndpointSearchResult? matchedDoc = null)
    {
        using var doc = JsonDocument.Parse(queryPlanJson);
        var root = doc.RootElement;

        var endpoint = root.TryGetProperty("endpoint", out var epProp) ? epProp.GetString() : null;
        if (string.IsNullOrEmpty(endpoint) && matchedDoc != null)
        {
            endpoint = matchedDoc.EndpointName;
        }

        if (string.IsNullOrEmpty(endpoint))
        {
            throw new Exception("Query plan missing 'endpoint'");
        }

        // Special handling for topCustomers aggregation query
        if (endpoint.Equals("topCustomers", StringComparison.OrdinalIgnoreCase))
        {
            int limit = 10;
            if (root.TryGetProperty("limit", out var limitProp) && limitProp.ValueKind == JsonValueKind.Number)
            {
                limit = limitProp.GetInt32();
            }
            else if (root.TryGetProperty("take", out var takeProp) && takeProp.ValueKind == JsonValueKind.Number)
            {
                limit = takeProp.GetInt32();
            }

            // Dataset date range: AdventureWorks database records span 2022 to 2025
            string from = "2022-01-01";
            string to = "2025-12-31";

            // Extract date range from top-level or nested filters
            if (root.TryGetProperty("from", out var fromProp) && fromProp.ValueKind == JsonValueKind.String)
            {
                var val = fromProp.GetString();
                if (DateTime.TryParse(val, out _)) from = val;
            }
            if (root.TryGetProperty("to", out var toProp) && toProp.ValueKind == JsonValueKind.String)
            {
                var val = toProp.GetString();
                if (DateTime.TryParse(val, out _)) to = val;
            }

            if (root.TryGetProperty("filters", out var filtersObj) && filtersObj.ValueKind == JsonValueKind.Object)
            {
                if (filtersObj.TryGetProperty("from", out var fProp) && fProp.ValueKind == JsonValueKind.String)
                {
                    var val = fProp.GetString();
                    if (DateTime.TryParse(val, out _)) from = val;
                }
                if (filtersObj.TryGetProperty("to", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                {
                    var val = tProp.GetString();
                    if (DateTime.TryParse(val, out _)) to = val;
                }
            }

            return $@"query {{
  topCustomers(limit: {limit}, from: ""{from}"", to: ""{to}"") {{
    customerId
    customerName
    territory
    revenue
    orderCount
  }}
}}";
        }

        // Default handling for standard offset-paged collection queries (e.g. customers, products)
        int take = 10;
        if (root.TryGetProperty("take", out var standardTakeProp) && standardTakeProp.ValueKind == JsonValueKind.Number)
        {
            take = standardTakeProp.GetInt32();
        }

        string whereClause = "";
        if (root.TryGetProperty("filters", out var filtersProp) && filtersProp.ValueKind == JsonValueKind.Object)
        {
            if (filtersProp.TryGetProperty("field", out var fieldProp) && filtersProp.TryGetProperty("value", out var valProp))
            {
                var field = fieldProp.GetString();
                var val = valProp.GetString();
                if (!string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(val))
                {
                    whereClause = $", where: {{ {field}: {{ eq: \"{val}\" }} }}";
                }
            }
        }

        string orderClause = "";
        if (root.TryGetProperty("sort", out var sortProp) && sortProp.ValueKind == JsonValueKind.Object)
        {
            if (sortProp.TryGetProperty("field", out var sortFieldProp) && sortProp.TryGetProperty("direction", out var sortDirProp))
            {
                var field = sortFieldProp.GetString();
                var dir = sortDirProp.GetString() ?? "ASC";
                if (!string.IsNullOrEmpty(field))
                {
                    orderClause = $", order: [{{ {field}: {dir.ToUpper()} }}]";
                }
            }
        }

        return $@"query {{
  {endpoint}(take: {take}{whereClause}{orderClause}) {{
    totalCount
    items {{
      id
      displayName
    }}
  }}
}}";
    }
}
