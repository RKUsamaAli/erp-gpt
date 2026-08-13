using System.Text.Json;

namespace ErpGpt.Agent.Services;

public class GraphQLValidatorAndBuilder
{
    public string BuildGraphQLQuery(string queryPlanJson)
    {
        using var doc = JsonDocument.Parse(queryPlanJson);
        var root = doc.RootElement;

        var endpoint = root.GetProperty("endpoint").GetString() ?? throw new Exception("Query plan missing 'endpoint'");
        
        int take = 10;
        if (root.TryGetProperty("take", out var takeProp) && takeProp.ValueKind == JsonValueKind.Number)
        {
            take = takeProp.GetInt32();
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

        // Construct standard GraphQL query for HotChocolate endpoint
        var graphQl = $@"query {{
  {endpoint}(take: {take}{whereClause}{orderClause}) {{
    totalCount
    items {{
      id
      displayName
    }}
  }}
}}";

        return graphQl;
    }
}
