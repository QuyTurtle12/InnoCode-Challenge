using Json.Schema;
using Repository.IRepositories;
using System.Text.Json;
using Utility.ExceptionCustom;

namespace InnoCode_Challenge_API.Middleware
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
                context.Response.ContentType = "application/json";

                // force standard {errorCode,errorMessage}
                var result = JsonSerializer.Serialize(new
                {
                    errorCode = ex.Code,
                    errorMessage = ex.Message
                });
                await context.Response.WriteAsync(result);

                await context.Response.WriteAsync(result);
            }
            catch (ErrorException ex)
            {
                _logger.LogError(ex, ex.ErrorDetail.ErrorMessage?.ToString());
                context.Response.StatusCode = ex.StatusCode;
                context.Response.ContentType = "application/json";

                var errorCode = ex.ErrorDetail?.ErrorCode ?? "UNKNOWN_ERROR";
                var errorMessage = ex.ErrorDetail?.ErrorMessage?.ToString() ?? "An error occurred.";

                var result = JsonSerializer.Serialize(new {errorCode,errorMessage});

                await context.Response.WriteAsync(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred.");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";

                var result = JsonSerializer.Serialize(new
                {
                    errorCode = "INTERNAL_SERVER_ERROR",
                    errorMessage = "An unexpected error occurred."
                });

                await context.Response.WriteAsync(result);
            }
        }
    }
}
