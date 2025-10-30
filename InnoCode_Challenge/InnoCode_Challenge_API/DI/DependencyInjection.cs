using BusinessLogic.IServices;
using BusinessLogic.IServices.Appeals;
using BusinessLogic.IServices.Certificates;
using BusinessLogic.IServices.Contests;
using BusinessLogic.IServices.Mcqs;
using BusinessLogic.IServices.Mentors;
using BusinessLogic.IServices.Schools;
using BusinessLogic.IServices.Students;
using BusinessLogic.IServices.Submissions;
using BusinessLogic.IServices.Users;
using BusinessLogic.MappingProfiles.Users;
using BusinessLogic.IServices.FileStorages;
using BusinessLogic.Services.FileStorages;
using Utility.Helpers;
using BusinessLogic.Services;
using BusinessLogic.Services.Appeals;
using BusinessLogic.Services.Certificates;
using BusinessLogic.Services.Contests;
using BusinessLogic.Services.Mcqs;
using BusinessLogic.Services.Mentors;
using BusinessLogic.Services.Schools;
using BusinessLogic.Services.Students;
using BusinessLogic.Services.Submissions;
using BusinessLogic.Services.Users;
using DataAccess.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Repository.DTOs.AuthDTOs;
using Repository.IRepositories;
using Repository.Repositories;
using System.Reflection;
using System.Text;
using Utility.Constant;

namespace InnoCode_Challenge_API.DI
{
    public static class DependencyInjection
    {
        public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.ConfigSwagger(configuration);
            services.AddAuthenJwt(configuration);
            services.AddAuthor(configuration);
            services.AddDatabase(configuration);
            services.ConfigRoute();
            services.ConfigCors();
            services.AddRepository();
            services.AddAutoMapper();
            services.AddCloudinary(configuration);
            services.AddSignalR();
            services.AddServices();
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="services"></param>
        public static void ConfigCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins",
                    builder =>
                    {
                        builder.AllowAnyOrigin()
                               .AllowAnyHeader()
                               .AllowAnyMethod();
                    });
            });
        }

        public static void ConfigRoute(this IServiceCollection services)
        {
            services.Configure<RouteOptions>(options =>
            {
                options.LowercaseUrls = true;
            });
        }

        public static void AddAuthenJwt(this IServiceCollection services, IConfiguration configuration)
        {
            // Bind JwtSettings from appsettings.json
            services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

            // Add Authentication + JWT Bearer
            var jwtSection = configuration.GetSection("JwtSettings");
            var jwtConfig = jwtSection.Get<JwtSettings>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = true;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtConfig.Issuer,
                    ValidAudience = jwtConfig.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtConfig.Key))
                };
            });
        }

        public static void AddAuthor(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy("RequireAdminRole",
                    policy => policy.RequireRole(RoleConstants.Admin));

                options.AddPolicy("RequireStaffOrAdmin",
                    policy => policy.RequireRole(RoleConstants.Staff, RoleConstants.Admin));

                options.AddPolicy("RequireAnyUserRole",
                    policy => policy.RequireRole(
                        RoleConstants.Student,
                        RoleConstants.Mentor,
                        RoleConstants.Judge,
                        RoleConstants.Staff,
                        RoleConstants.ContestOrganizer,
                        RoleConstants.Admin));

                options.AddPolicy("RequireMentorRole",
                    policy => policy.RequireRole(RoleConstants.Mentor));

                options.AddPolicy("RequireJudgeRole",
                    policy => policy.RequireRole(RoleConstants.Judge));

                options.AddPolicy("RequireOrganizerOrAdmin",
                    policy => policy.RequireRole(RoleConstants.ContestOrganizer, RoleConstants.Admin));
            });
        
        }

        public static void ConfigSwagger(this IServiceCollection services, IConfiguration configuration)
        {
            // Add Swagger services with XML comments
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Swagger API",
                    Version = "v1"
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme.\r\n\r\n Enter your token in the text input below.\r\n\r\nExample: \"eyJhb...\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] {}
                    }
                });

                // Add XML Comments
                var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
            });

            // Make all route lowercase
            services.Configure<RouteOptions>(options =>
            {
                options.LowercaseUrls = true;
            });
        }

        public static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<ContestDbContext>(options =>
            {
                options.UseSqlServer(configuration.GetConnectionString("MyCnn"));
            });
        }

        public static void AddRepository(this IServiceCollection services)
        {
            // Register Repositories
            services.AddScoped<IUOW, UOW>();
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        }

        public static void AddAutoMapper(this IServiceCollection services)
        {
            // Register AutoMapper
            services.AddAutoMapper(typeof(UserProfile).Assembly);
        }

        public static void AddCloudinary(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<CloudinarySettings>(
                configuration.GetSection("CloudinarySettings"));
        }

        public static void AddSignalR(this IServiceCollection services)
        {
            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            });
        }

        public static void AddServices(this IServiceCollection services)
        {
            services.AddLogging();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IContestService, ContestService>();
            services.AddScoped<IRoundService, RoundService>();
            services.AddScoped<IMcqQuestionService, McqQuestionService>();
            services.AddScoped<IMcqOptionService, McqOptionService>();
            services.AddScoped<IMcqTestService, McqTestService>();
            services.AddScoped<IMcqTestQuestionService, McqTestQuestionService>();
            services.AddScoped<IMcqAttemptService, McqAttemptService>();
            services.AddScoped<IMcqAttemptItemService, McqAttemptItemService>();
            services.AddScoped<ILeaderboardEntryService, LeaderboardEntryService>();
            services.AddScoped<ICertificateTemplateService, CertificateTemplateService>();
            services.AddScoped<ICertificateService, CertificateService>();
            services.AddScoped<IAppealService, AppealService>();
            services.AddScoped<IAppealEvidenceService, AppealEvidenceService>();
            services.AddScoped<IProblemService, ProblemService>();
            services.AddScoped<ITestCaseService, TestCaseService>();
            services.AddScoped<IProvinceService, ProvinceService>();
            services.AddScoped<ISchoolService, SchoolService>();
            services.AddScoped<IStudentService, StudentService>();
            services.AddScoped<ITeamService, TeamService>();
            services.AddScoped<IMentorService, MentorService>();
            services.AddScoped<ITeamMemberService, TeamMemberService>();
            services.AddScoped<ISubmissionService, SubmissionService>();
            services.AddScoped<ISubmissionDetailService, SubmissionDetailService>();
            services.AddScoped<ISubmissionArtifactService, SubmissionArtifactService>();
            services.AddScoped<IMentorRegistrationService, MentorRegistrationService>();
            services.AddScoped<IJudge0Service, Judge0Service>();
            services.AddScoped<IQuizService, QuizService>();
            services.AddScoped<ICloudinaryService, CloudinaryService>();
            services.AddScoped<IConfigService, ConfigService>();
            services.AddScoped<IAttachmentService, AttachmentService>();
            services.AddScoped<IActivityLogService, ActivityLogService>();
            services.AddScoped<ITeamInviteService, TeamInviteService>();
            services.AddScoped<ILeaderboardEntryService, LeaderboardEntryService>();
            services.AddScoped<ILeaderboardRealtimeService, LeaderboardRealtimeService>();
        }
    }
}
