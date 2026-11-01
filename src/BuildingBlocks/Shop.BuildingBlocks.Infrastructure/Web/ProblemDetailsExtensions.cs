using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.BuildingBlocks.Infrastructure.Web;

public static class ProblemDetailsExtensions
{
    public static ProblemDetails ToProblemDetails(this Error error, HttpContext? httpContext = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
            ErrorType.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            ErrorType.Failure => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        var title = error.Type switch
        {
            ErrorType.Validation => "Bad Request",
            ErrorType.NotFound => "Not Found",
            ErrorType.Conflict => "Conflict",
            ErrorType.Forbidden => "Forbidden",
            ErrorType.PreconditionFailed => "Precondition Failed",
            _ => "An error occurred while processing your request."
        };

        var traceId = Activity.Current?.Id ?? httpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = error.Description,
            Extensions =
            {
                ["code"] = error.Code,
                ["traceId"] = traceId
            }
        };

        return problemDetails;
    }

    public static IResult ToProblemResult(this Error error, HttpContext? httpContext = null)
    {
        var problemDetails = error.ToProblemDetails(httpContext);
        return Results.Problem(problemDetails);
    }
}
