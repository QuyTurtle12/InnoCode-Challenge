using AutoMapper;
using BusinessLogic.Services.Users;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using Repository.DTOs.AuthDTOs;
using Repository.IRepositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Utility.Constant;
using Utility.ExceptionCustom;
using Utility.Helpers;
using Utility.ConfigDTOs;
using Xunit;


namespace BusinessLogic.UnitTests.Users
{
    public class AuthServiceTests
    {
        private static JwtSettings CreateJwtSettings() => new JwtSettings
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Key = "THIS_IS_A_SUPER_LONG_TEST_KEY_1234567890_ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            ExpiryMinutes = 60,
            RefreshExpiryMinutes = 120
        };

        private static ClaimsPrincipal CreatePrincipal(Guid userId)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            }, "TestAuth");

            return new ClaimsPrincipal(identity);
        }

        private static AuthService CreateSut(
            List<User> users,
            List<Student> students,
            List<School> schools,
            out Mock<IUOW> uowMock,
            ClaimsPrincipal? principal = null)
        {
            // 1) Mock repositories
            var userRepo = new Mock<IGenericRepository<User>>();
            var studentRepo = new Mock<IGenericRepository<Student>>();
            var schoolRepo = new Mock<IGenericRepository<School>>();

            // 2) Build mock DbSet (async-enabled)
            var userSet = users.BuildMockDbSet();
            var studentSet = students.BuildMockDbSet();
            var schoolSet = schools.BuildMockDbSet();

            userRepo.Setup(r => r.Entities).Returns(userSet.Object);
            studentRepo.Setup(r => r.Entities).Returns(studentSet.Object);
            schoolRepo.Setup(r => r.Entities).Returns(schoolSet.Object);

            // 3) Setup InsertAsync để “thêm vào list” (giả lập insert)
            userRepo.Setup(r => r.InsertAsync(It.IsAny<User>()))
                .Callback<User>(u => users.Add(u))
                .Returns(Task.CompletedTask);

            studentRepo.Setup(r => r.InsertAsync(It.IsAny<Student>()))
                .Callback<Student>(s => students.Add(s))
                .Returns(Task.CompletedTask);

            // 4) Setup GetByIdAsync (AuthService dùng cho User)
            userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => users.FirstOrDefault(x => x.UserId == id));

            // 5) Mock UOW
            uowMock = new Mock<IUOW>();
            uowMock.Setup(u => u.GetRepository<User>()).Returns(userRepo.Object);
            uowMock.Setup(u => u.GetRepository<Student>()).Returns(studentRepo.Object);
            uowMock.Setup(u => u.GetRepository<School>()).Returns(schoolRepo.Object);

            uowMock.Setup(u => u.SaveAsync()).Returns(Task.CompletedTask);
            uowMock.Setup(u => u.BeginTransaction());
            uowMock.Setup(u => u.CommitTransaction());
            uowMock.Setup(u => u.RollBack());

            // 6) Options JwtSettings
            var jwtOptions = Options.Create(CreateJwtSettings());

            // 7) HttpContextAccessor
            var ctx = new DefaultHttpContext();
            if (principal != null) ctx.User = principal;

            var httpAccessor = new Mock<IHttpContextAccessor>();
            httpAccessor.Setup(a => a.HttpContext).Returns(ctx);

            // 8) Mapper (hiện chưa dùng)
            var mapper = Mock.Of<IMapper>();

            return new AuthService(uowMock.Object, jwtOptions, httpAccessor.Object);
        }

        // =========================
        // TESTS BẮT ĐẦU TỪ ĐƠN GIẢN
        // =========================

        [Fact]
        public async Task RegisterStudentStrictAsync_WhenConfirmPasswordMismatch_ShouldThrow400()
        {
            // Arrange
            var users = new List<User>();
            var students = new List<Student>();
            var schools = new List<School>();

            var sut = CreateSut(users, students, schools, out var uow);

            var dto = new RegisterStudentDTO
            {
                Email = "test@example.com",
                FullName = "Test User",
                Password = "123456",
                ConfirmPassword = "654321",
                SchoolId = Guid.NewGuid()
            };

            // Act
            var act = async () => await sut.RegisterStudentStrictAsync(dto);

            // Assert
            var ex = await act.Should().ThrowAsync<ErrorException>();
            // Nếu ErrorException của bạn có property StatusCode/ErrorCode thì assert như dưới:
            ex.Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            ex.Which.ErrorDetail.ErrorCode.Should().Be("CONFIRM_PASSWORD_MISMATCH");

            // Và vì fail sớm nên không đụng DB
            uow.Verify(x => x.BeginTransaction(), Times.Never);
        }

        [Fact]
        public async Task RegisterStudentStrictAsync_WhenEmailExists_ShouldThrow400()
        {
            // Arrange
            var schoolId = Guid.NewGuid();

            var users = new List<User>
            {
                new User
                {
                    UserId = Guid.NewGuid(),
                    Email = "test@example.com", // đã normalize lower
                    PasswordHash = PasswordHasher.Hash("anything"),
                    Role = RoleConstants.Student,
                    Status = UserStatusConstants.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                }
            };
            var students = new List<Student>();
            var schools = new List<School>
            {
                new School { SchoolId = schoolId, DeletedAt = null }
            };

            var sut = CreateSut(users, students, schools, out _);

            var dto = new RegisterStudentDTO
            {
                Email = "  TEST@EXAMPLE.COM  ", // cố tình viết hoa + khoảng trắng
                FullName = "New Student",
                Password = "123456",
                ConfirmPassword = "123456",
                SchoolId = schoolId
            };

            // Act
            var act = async () => await sut.RegisterStudentStrictAsync(dto);

            // Assert
            var ex = await act.Should().ThrowAsync<ErrorException>();
            ex.Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            ex.Which.ErrorDetail.ErrorCode.Should().Be("EMAIL_EXISTS");
        }

        [Fact]
        public async Task RegisterStudentStrictAsync_WhenValid_ShouldInsertUserAndStudent_AndReturnTokens()
        {
            // Arrange
            var schoolId = Guid.NewGuid();

            var users = new List<User>();
            var students = new List<Student>();
            var schools = new List<School>
            {
                new School { SchoolId = schoolId, DeletedAt = null }
            };

            var sut = CreateSut(users, students, schools, out var uow);

            var dto = new RegisterStudentDTO
            {
                Email = "  STUDENT@Example.com ",
                FullName = "  Nguyen Van A  ",
                Password = "123456",
                ConfirmPassword = "123456",
                SchoolId = schoolId,
                Grade = "  10  "
            };

            // Act
            var res = await sut.RegisterStudentStrictAsync(dto);

            // Assert: response basic
            res.Token.Should().NotBeNullOrWhiteSpace();
            res.RefreshToken.Should().NotBeNullOrWhiteSpace();
            res.Email.Should().Be("student@example.com");     // normalize
            res.FullName.Should().Be("Nguyen Van A");         // trim
            res.Role.Should().Be(RoleConstants.Student);
            res.EmailVerified.Should().BeFalse();

            // Assert: inserted data
            users.Should().HaveCount(1);
            students.Should().HaveCount(1);
            students[0].SchoolId.Should().Be(schoolId);
            students[0].UserId.Should().Be(users[0].UserId);

            // Transaction flow
            uow.Verify(x => x.BeginTransaction(), Times.Once);
            uow.Verify(x => x.CommitTransaction(), Times.Once);
            uow.Verify(x => x.RollBack(), Times.Never);

            // Decode access token, check some claims
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(res.Token);
            jwt.Claims.First(c => c.Type == "typ").Value.Should().Be("access");
            jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value.Should().Be(RoleConstants.Student);
            jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value.Should().Be("student@example.com");
        }

        [Fact]
        public async Task LoginAsync_WhenPasswordWrong_ShouldThrow401()
        {
            // Arrange
            var users = new List<User>
            {
                new User
                {
                    UserId = Guid.NewGuid(),
                    Email = "a@b.com",
                    PasswordHash = PasswordHasher.Hash("correct"),
                    Role = RoleConstants.Student,
                    Status = UserStatusConstants.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                }
            };
            var sut = CreateSut(users, new List<Student>(), new List<School>(), out _);

            var dto = new LoginDTO { Email = "A@B.COM", Password = "wrong" };

            // Act
            var act = async () => await sut.LoginAsync(dto);

            // Assert
            var ex = await act.Should().ThrowAsync<ErrorException>();
            ex.Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            ex.Which.ErrorDetail.ErrorCode.Should().Be("INVALID_CREDENTIALS");
        }

        [Fact]
        public async Task VerifyEmailAsync_Flow_GenerateTokenThenVerify_ShouldActivateUser()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var users = new List<User>
            {
                new User
                {
                    UserId = userId,
                    Email = "verify@demo.com",
                    PasswordHash = PasswordHasher.Hash("123"),
                    Role = RoleConstants.Student,
                    Status = UserStatusConstants.Unverified, // chưa verify
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    DeletedAt = null
                }
            };

            var principal = CreatePrincipal(userId);
            var sut = CreateSut(users, new List<Student>(), new List<School>(), out _, principal);

            // Act 1: generate verification token
            var token = await sut.GenerateVerificationTokenAsync();
            token.Should().NotBeNullOrWhiteSpace();

            // Act 2: verify email using that token
            await sut.VerifyEmailAsync(token);

            // Assert
            users[0].Status.Should().Be("Active"); // hoặc UserStatusConstants.Active nếu bạn dùng constant
        }
    }
}
