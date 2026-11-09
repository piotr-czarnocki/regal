using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Regal.Web.Features.Common;

public static class DbErrors
{
    /// <summary>Name of the violated unique index, or null for any other failure.</summary>
    public static string? UniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName
            : null;
}
