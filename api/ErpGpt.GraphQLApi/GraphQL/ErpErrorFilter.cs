using HotChocolate.Execution;

namespace ErpGpt.GraphQLApi.GraphQL;

/// <summary>
/// Turns unexpected exceptions into errors that say something useful and
/// carry a stable code. Phase 4's retry loop feeds these back to the model,
/// so "Unexpected Execution Error" is not good enough — the message has to
/// tell the caller what to do differently.
///
/// Errors we raise deliberately (INVALID_DATE_RANGE and friends) already
/// have a code and are passed through untouched.
/// </summary>
public class ErpErrorFilter(ILoggerFactory loggerFactory) : IErrorFilter
{
    private readonly ILogger logger = loggerFactory.CreateLogger<ErpErrorFilter>();

    public IError OnError(IError error)
    {
        // Note: validation errors (cost limits HC0047, depth, unknown fields)
        // never reach this filter — they are raised before execution starts.
        // Only runtime failures are shaped here.
        if (error.Code is not null)
            return error;

        if (error.Exception is null)
            return error;

        logger.LogError(error.Exception, "Unhandled GraphQL error at {Path}", error.Path?.ToString());

        return error.Exception switch
        {
            Npgsql.NpgsqlException => error
                .WithMessage("The database could not be reached. Check that the ERP database container is running on port 5432.")
                .WithCode("DATABASE_UNAVAILABLE"),

            TimeoutException => error
                .WithMessage("The query took too long. Narrow the date range, or add a filter to reduce how many rows are read.")
                .WithCode("QUERY_TIMEOUT"),

            _ => error
                .WithMessage("The query could not be completed. Check the field names and argument types against the schema.")
                .WithCode("QUERY_FAILED"),
        };
    }
}
