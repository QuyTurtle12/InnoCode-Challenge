using Api.IntegrationTests.Infrastructure;
using DataAccess.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Repository.DTOs.AuthDTOs;
using Repository.DTOs.CertificateTemplateDTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Utility.Constant;
using Utility.Enums;
using Xunit;

namespace Api.IntegrationTests.Certificates
{
    public class CertificateTemplateApiTests : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public CertificateTemplateApiTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        private async Task<string> LoginAdminAsync()
        {
            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDTO
            {
                Email = TestSeed.AdminEmail,
                Password = TestSeed.AdminPassword
            });

            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.ReadOkAsync<AuthResponseDTO>();
            body.Data!.Token.Should().NotBeNullOrWhiteSpace();
            return body.Data!.Token;
        }

        private Guid SeedContest()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var now = DateTime.UtcNow;

            var contest = new Contest
            {
                ContestId = Guid.NewGuid(),
                Name = "Cert Template Contest",
                Year = now.Year,
                ImgUrl = "https://example.com/contest.png",
                Status = ContestStatusEnum.Ongoing.ToString(),
                CreatedAt = now,
                Start = now.AddHours(-1),
                End = now.AddHours(1),
                CreatedBy = Guid.NewGuid().ToString()
            };

            db.Contests.Add(contest);
            db.SaveChanges();

            return contest.ContestId;
        }

        private Guid SeedTemplate(Guid contestId)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();

            var tpl = new CertificateTemplate
            {
                TemplateId = Guid.NewGuid(),
                ContestId = contestId,
                Name = "Seed Template",
                FileUrl = "https://example.com/seed.png",
                TextX = 10,
                TextY = 20,
                DeletedAt = null
            };

            db.CertificateTemplates.Add(tpl);
            db.SaveChanges();

            return tpl.TemplateId;
        }

        private async Task<CertificateTemplateDTO> CreateTemplateAsync(Guid contestId, string token, TextLayoutDTO? text = null)
        {
            var dto = new CreateCertificateTemplateDTO
            {
                ContestId = contestId,
                Name = "Template A",
                FileUrl = "https://example.com/cert.png",
                Text = text ?? new TextLayoutDTO { X = 100, Y = 200 }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificate-templates");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(dto);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = await res.ReadOkAsync<CertificateTemplateDTO>();
            body.Data.Should().NotBeNull();
            return body.Data!;
        }

        [Fact]
        public async Task Create_ThenGetById_ShouldPersistOnlyXY_AndDefaultOtherTextFields()
        {
            var token = await LoginAdminAsync();
            var contestId = SeedContest();

            var customText = new TextLayoutDTO
            {
                X = 123,
                Y = 456,
                FontFamily = "Courier New",
                FontSize = 12f,
                ColorHex = "#000000",
                MaxWidth = 999,
                Align = "left"
            };

            var created = await CreateTemplateAsync(contestId, token, customText);

            var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/certificate-templates/{created.TemplateId:D}");
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var getRes = await _client.SendAsync(getReq);
            getRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await getRes.ReadOkAsync<CertificateTemplateDTO>();
            body.Data.Should().NotBeNull();
            body.Data!.Text.X.Should().Be(123);
            body.Data!.Text.Y.Should().Be(456);
            body.Data!.Text.FontFamily.Should().Be("Arial");
            body.Data!.Text.FontSize.Should().Be(64f);
            body.Data!.Text.ColorHex.Should().Be("#1F2937");
            body.Data!.Text.MaxWidth.Should().Be(1600);
            body.Data!.Text.Align.Should().Be("center");
        }

        [Fact]
        public async Task Update_ShouldChangeNameFileUrlAndXY()
        {
            var token = await LoginAdminAsync();
            var contestId = SeedContest();
            var created = await CreateTemplateAsync(contestId, token);

            var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/certificate-templates/{created.TemplateId:D}");
            updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            updateReq.Content = JsonContent.Create(new UpdateCertificateTemplateDTO
            {
                Name = "Updated Name",
                FileUrl = "https://example.com/cert2.png",
                Text = new UpdateTextLayoutDTO { X = 321, Y = 654 }
            });

            var updateRes = await _client.SendAsync(updateReq);
            updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/certificate-templates/{created.TemplateId:D}");
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var getRes = await _client.SendAsync(getReq);
            getRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await getRes.ReadOkAsync<CertificateTemplateDTO>();
            body.Data!.Name.Should().Be("Updated Name");
            body.Data!.FileUrl.Should().Be("https://example.com/cert2.png");
            body.Data!.Text.X.Should().Be(321);
            body.Data!.Text.Y.Should().Be(654);
            body.Data!.Text.FontFamily.Should().Be("Arial");
        }

        [Fact]
        public async Task SoftDelete_ShouldHideFromList_AndGetByIdReturnsNull()
        {
            var token = await LoginAdminAsync();
            var contestId = SeedContest();
            var created = await CreateTemplateAsync(contestId, token);

            var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/certificate-templates/{created.TemplateId:D}");
            deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var deleteRes = await _client.SendAsync(deleteReq);
            deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/certificate-templates/{created.TemplateId:D}");
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var getRes = await _client.SendAsync(getReq);
            getRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var getBody = await getRes.ReadOkAsync<CertificateTemplateDTO>();
            getBody.Data.Should().BeNull();

            var listReq = new HttpRequestMessage(HttpMethod.Get, $"/api/certificate-templates?contestId={contestId:D}&page=1&pageSize=10");
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var listRes = await _client.SendAsync(listReq);
            listRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var listBody = await listRes.ReadOkAsync<List<CertificateTemplateDTO>>();
            listBody.Data.Should().NotBeNull();
            listBody.Data!.Any(x => x.TemplateId == created.TemplateId).Should().BeFalse();
        }

        [Fact]
        public async Task Create_ShouldWriteActivityLog()
        {
            var token = await LoginAdminAsync();
            var contestId = SeedContest();
            var created = await CreateTemplateAsync(contestId, token);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var admin = db.Users.First(u => u.Email == TestSeed.AdminEmail.ToLowerInvariant());
            db.ActivityLogs.Any(l => l.UserId == admin.UserId
                                     && l.Action == ActivityActions.CertTemplateCreate
                                     && l.TargetType == TargetTypes.CertificateTemplate
                                     && l.TargetId == created.TemplateId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task Update_ShouldWriteActivityLog()
        {
            var token = await LoginAdminAsync();
            var contestId = SeedContest();
            var created = await CreateTemplateAsync(contestId, token);

            var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/certificate-templates/{created.TemplateId:D}");
            updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            updateReq.Content = JsonContent.Create(new UpdateCertificateTemplateDTO
            {
                Name = "Updated Name",
                FileUrl = "https://example.com/cert2.png"
            });

            var updateRes = await _client.SendAsync(updateReq);
            updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var admin = db.Users.First(u => u.Email == TestSeed.AdminEmail.ToLowerInvariant());
            db.ActivityLogs.Any(l => l.UserId == admin.UserId
                                     && l.Action == ActivityActions.CertTemplateUpdate
                                     && l.TargetType == TargetTypes.CertificateTemplate
                                     && l.TargetId == created.TemplateId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task SoftDelete_ShouldWriteActivityLog()
        {
            var token = await LoginAdminAsync();
            var contestId = SeedContest();
            var created = await CreateTemplateAsync(contestId, token);

            var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/certificate-templates/{created.TemplateId:D}");
            deleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var deleteRes = await _client.SendAsync(deleteReq);
            deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContestDbContext>();
            var admin = db.Users.First(u => u.Email == TestSeed.AdminEmail.ToLowerInvariant());
            db.ActivityLogs.Any(l => l.UserId == admin.UserId
                                     && l.Action == ActivityActions.CertTemplateDelete
                                     && l.TargetType == TargetTypes.CertificateTemplate
                                     && l.TargetId == created.TemplateId.ToString()).Should().BeTrue();
        }

        [Fact]
        public async Task Create_WhenContestNotFound_ShouldReturn404()
        {
            var token = await LoginAdminAsync();
            var dto = new CreateCertificateTemplateDTO
            {
                ContestId = Guid.NewGuid(),
                Name = "Template A",
                FileUrl = "https://example.com/cert.png",
                Text = new TextLayoutDTO { X = 100, Y = 200 }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificate-templates");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(dto);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("CONTEST_NOT_FOUND");
        }

        [Fact]
        public async Task Create_WhenUnauthorized_ShouldReturn401()
        {
            var contestId = SeedContest();
            var dto = new CreateCertificateTemplateDTO
            {
                ContestId = contestId,
                Name = "Template A",
                FileUrl = "https://example.com/cert.png",
                Text = new TextLayoutDTO { X = 100, Y = 200 }
            };

            var res = await _client.PostAsJsonAsync("/api/certificate-templates", dto);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Create_WhenInvalidFileUrl_ShouldReturn400_VALIDATION_ERROR()
        {
            var token = await LoginAdminAsync();
            var contestId = SeedContest();
            var dto = new CreateCertificateTemplateDTO
            {
                ContestId = contestId,
                Name = "Template A",
                FileUrl = "not-a-url",
                Text = new TextLayoutDTO { X = 100, Y = 200 }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificate-templates");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(dto);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("VALIDATION_ERROR");
        }

        [Fact]
        public async Task Create_WhenNameTooLong_ShouldReturn400_VALIDATION_ERROR()
        {
            var token = await LoginAdminAsync();
            var contestId = SeedContest();
            var dto = new CreateCertificateTemplateDTO
            {
                ContestId = contestId,
                Name = new string('a', 201),
                FileUrl = "https://example.com/cert.png",
                Text = new TextLayoutDTO { X = 100, Y = 200 }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/certificate-templates");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(dto);

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("VALIDATION_ERROR");
        }

        [Fact]
        public async Task Update_WhenNotFound_ShouldReturn404()
        {
            var token = await LoginAdminAsync();

            var req = new HttpRequestMessage(HttpMethod.Put, $"/api/certificate-templates/{Guid.NewGuid():D}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = JsonContent.Create(new UpdateCertificateTemplateDTO
            {
                Name = "Updated Name"
            });

            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var err = await res.ReadErrorAsync();
            err.ErrorCode.Should().Be("TEMPLATE_NOT_FOUND");
        }

        [Fact]
        public async Task GetById_WhenUnauthorized_ShouldReturn401()
        {
            var contestId = SeedContest();
            var templateId = SeedTemplate(contestId);

            var res = await _client.GetAsync($"/api/certificate-templates/{templateId:D}");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Update_WhenUnauthorized_ShouldReturn401()
        {
            var contestId = SeedContest();
            var templateId = SeedTemplate(contestId);

            var res = await _client.PutAsJsonAsync($"/api/certificate-templates/{templateId:D}", new UpdateCertificateTemplateDTO
            {
                Name = "Updated Name"
            });

            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task SoftDelete_WhenUnauthorized_ShouldReturn401()
        {
            var contestId = SeedContest();
            var templateId = SeedTemplate(contestId);

            var res = await _client.DeleteAsync($"/api/certificate-templates/{templateId:D}");
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
