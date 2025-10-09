using System.Text.Json;
using Repository.IRepositories;
using Utility.ExceptionCustom;

namespace Product_Sale_API.Middleware
{
    /// <summary>
    /// 
    /// </summary>
    public class CustomExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CustomExceptionHandlerMiddleware> _logger;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="next"></param>
        /// <param name="logger"></param>
        public CustomExceptionHandlerMiddleware(RequestDelegate next, ILogger<CustomExceptionHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="unitOfWork"></param>
        /// <returns></returns>
        public async Task Invoke(HttpContext context, IUOW unitOfWork)
        {
            try
            {
                await _next(context);
            }
            catch (CoreException ex)
            {
                _logger.LogError(ex, ex.Message);
                context.Response.StatusCode = ex.StatusCode;
                var result = JsonSerializer.Serialize(new { ex.Code, ex.Message, ex.AdditionalData });
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(result);
            }
            catch (ErrorException ex)
            {
                _logger.LogError(ex, ex.ErrorDetail.ErrorMessage?.ToString());
                context.Response.StatusCode = ex.StatusCode;
                var result = JsonSerializer.Serialize(ex.ErrorDetail);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred.");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                var result = JsonSerializer.Serialize(new { error = $"An unexpected error occurred. Detail{ex.Message}" });
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(result);
            }
        }
    }
}
