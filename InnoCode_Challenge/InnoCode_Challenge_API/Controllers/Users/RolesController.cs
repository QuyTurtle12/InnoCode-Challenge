using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.ResponseModel;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers.Users
{
    [Route("api/roles")]
    [ApiController]
    public class RolesController : ControllerBase
    {
        [HttpGet("me")]
        [Authorize]
        public IActionResult Me()
        {
            var data = ReadJwtData(User);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "Authenticated user claims."
            ));
        }

        [HttpGet("admin")]
        [Authorize(Roles = RoleConstants.Admin)]
        public IActionResult CheckAdmin()
        {
            var data = ReadJwtData(User);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "You are Admin."
            ));
        }

        [HttpGet("staff")]
        [Authorize(Roles = RoleConstants.Staff)]
        public IActionResult CheckStaff()
        {
            var data = ReadJwtData(User);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "You are Staff."
            ));
        }

        [HttpGet("student")]
        [Authorize(Roles = RoleConstants.Student)]
        public IActionResult CheckStudent()
        {
            var data = ReadJwtData(User);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "You are Student."
            ));
        }

        [HttpGet("mentor")]
        [Authorize(Roles = RoleConstants.Mentor)]
        public IActionResult CheckMentor()
        {
            var data = ReadJwtData(User);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "You are Mentor."
            ));
        }

        [HttpGet("judge")]
        [Authorize(Roles = RoleConstants.Judge)]
        public IActionResult CheckJudge()
        {
            var data = ReadJwtData(User);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "You are Judge."
            ));
        }

        [HttpGet("organizer")]
        [Authorize(Roles = RoleConstants.ContestOrganizer)]
        public IActionResult CheckContestOrganizer()
        {
            var data = ReadJwtData(User);
            return Ok(new BaseResponseModel<object>(
                statusCode: StatusCodes.Status200OK,
                code: ResponseCodeConstants.SUCCESS,
                data: data,
                message: "You are Contest Organizer."
            ));
        }

        private static object ReadJwtData(ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = user.FindFirstValue(ClaimTypes.Email)
                        ?? user.FindFirstValue(JwtRegisteredClaimNames.Email);
            var role = user.FindFirstValue(ClaimTypes.Role);
            var fullName = user.FindFirstValue(ClaimTypes.Name);
            var jti = user.FindFirstValue(JwtRegisteredClaimNames.Jti);

            var iatStr = user.FindFirstValue(JwtRegisteredClaimNames.Iat) ?? user.FindFirst("iat")?.Value;
            var expStr = user.FindFirstValue(JwtRegisteredClaimNames.Exp) ?? user.FindFirst("exp")?.Value;
            DateTime? issuedAt = null, expiresAt = null;
            if (long.TryParse(iatStr, out var iatUnix))
                issuedAt = DateTimeOffset.FromUnixTimeSeconds(iatUnix).UtcDateTime;
            if (long.TryParse(expStr, out var expUnix))
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;

            var issuer = user.FindFirst("iss")?.Value;
            var audience = user.FindFirst("aud")?.Value;

            return new
            {
                UserId = userId,
                Email = email,
                Role = role,
                FullName = fullName,
                Jti = jti,
                Issuer = issuer,
                Audience = audience,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt
            };
        }
    }
}
