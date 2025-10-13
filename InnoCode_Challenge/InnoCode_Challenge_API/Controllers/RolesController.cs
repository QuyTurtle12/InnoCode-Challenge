using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Repository.ResponseModel;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Utility.Constant;

namespace InnoCode_Challenge_API.Controllers
{
    [Route("api/roles")]
    [ApiController]
    public class RolesController : ControllerBase
    {
        // GET: /api/roles/me  (any authenticated user)
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

        // GET: /api/roles/admin
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

        // GET: /api/roles/staff
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

        // GET: /api/roles/student
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

        // GET: /api/roles/mentor
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

        // GET: /api/roles/judge
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

        // GET: /api/roles/organizer
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

        // ===== helpers =====
        private static object ReadJwtData(ClaimsPrincipal user)
        {
            // Claims we commonly set in your AuthService.GenerateJwtToken()
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = user.FindFirstValue(ClaimTypes.Email)
                        ?? user.FindFirstValue(JwtRegisteredClaimNames.Email);
            var role = user.FindFirstValue(ClaimTypes.Role);
            var fullName = user.FindFirstValue(ClaimTypes.Name);
            var jti = user.FindFirstValue(JwtRegisteredClaimNames.Jti);

            // Optional: iat/exp/iss/aud (may or may not be present depending on token handler)
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
