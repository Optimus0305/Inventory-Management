using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Exceptions;
using System.Net;
using System.Text.Json;

namespace InventoryHold.WebApi.Middleware;

/// <summary>
/// Global exception handler middleware that converts domain exceptions to
/// structured error responses with appropriate HTTP status codes.
/// Prevents raw exception details from leaking to clients.
/// </summary>
public sealed class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;

    public ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger)
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
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteErrorResponseAsync(context, ex);
        }
    }

    private static Task WriteErrorResponseAsync(HttpContext context, Exception ex)
    {
        var (statusCode, code) = ex switch
        {
            HoldNotFoundException       => (HttpStatusCode.NotFound,    "HOLD_NOT_FOUND"),
            HoldAlreadyReleasedException => (HttpStatusCode.Conflict,   "HOLD_ALREADY_RELEASED"),
            HoldAlreadyExpiredException => (HttpStatusCode.Conflict,    "HOLD_ALREADY_EXPIRED"),
            InsufficientInventoryException => (HttpStatusCode.Conflict, "INSUFFICIENT_INVENTORY"),
            DomainException             => (HttpStatusCode.BadRequest,  "DOMAIN_ERROR"),
            _                           => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR")
        };

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var error = new ErrorResponse
        {
            Code = code,
            Message = statusCode == HttpStatusCode.InternalServerError
                ? "An unexpected error occurred."
                : ex.Message
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(error));
    }
}
