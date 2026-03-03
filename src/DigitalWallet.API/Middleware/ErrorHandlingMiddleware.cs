using ValidationException = DigitalWallet.Application.Common.Exceptions.ValidationException;


namespace DigitalWallet.API.Middleware;
/// <summary>
/// Global exception handling middleware that catches all unhandled exceptions
/// and returns a consistent JSON error response.
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "An unhandled exception occurred.");

        var response = context.Response;
        response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            TraceId = context.TraceIdentifier
        };

        switch (exception)
        {
            case ValidationException validationEx:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Title = "Validation Failed";
                errorResponse.Status = response.StatusCode;
                errorResponse.Errors = validationEx.Errors;
                break;

            case NotFoundException notFoundEx:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                errorResponse.Title = "Resource Not Found";
                errorResponse.Status = response.StatusCode;
                errorResponse.Detail = notFoundEx.Message;
                break;

            case UnauthorizedException:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Title = "Unauthorized";
                errorResponse.Status = response.StatusCode;
                errorResponse.Detail = "You are not authenticaticated";
                break;

            case ForbiddenAccessException:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Title = "Unauthorized";
                errorResponse.Status = response.StatusCode;
                errorResponse.Detail = "You do not have permission to perform this action.";
                break;

            case DomainException domainEx:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Title = "Business Rule Violation";
                errorResponse.Status = response.StatusCode;
                errorResponse.Detail = domainEx.Message;
                break;

            default:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                errorResponse.Title = "An unexpected error occured.";
                errorResponse.Status = response.StatusCode;
                errorResponse.Detail = "Please try again later or contact support";
                break;
        }

        var json = JsonSerializer.Serialize(errorResponse);
        await response.WriteAsync(json);
    }
}

public class ErrorResponse
{
    public string TraceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public int Status { get; set; }
    public string? Detail { get; set; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; set; }
}