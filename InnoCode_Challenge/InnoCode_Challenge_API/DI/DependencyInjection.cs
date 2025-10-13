using System.Reflection;
using System.Text;
using BusinessLogic.MappingProfiles;
using Repository.DTOs.AuthDTOs;
using Repository.IRepositories;
using Repository.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using DataAccess.Entities;
using Utility.Constant;
using BusinessLogic.IServices;
using BusinessLogic.Services;

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
            // Configure role‐based policies
            services.AddAuthorization(options =>
            {
                // Only Admin can do certain things
                options.AddPolicy("RequireAdminRole", policy =>
                  policy.RequireRole(RoleConstants.Admin));

                // Staff OR Admin
                options.AddPolicy("RequireStaffOrAdmin", policy =>
                  policy.RequireRole(RoleConstants.Staff, RoleConstants.Admin));

                // Any authenticated User (including Staff/Admin)
                options.AddPolicy("RequireAnyUserRole", policy =>
                  policy.RequireRole(RoleConstants.Student, RoleConstants.Staff, RoleConstants.Admin));
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

        private static void AddAutoMapper(this IServiceCollection services)
        {
            // Register AutoMapper
            services.AddAutoMapper(typeof(UserProfile).Assembly);
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
            services.AddScoped<IMcqTestQuestionService, McqTestQuestionService>();
            services.AddScoped<IMcqAttemptService, McqAttemptService>();
            services.AddScoped<IMcqAttemptItemService, McqAttemptItemService>();
            services.AddScoped<ILeaderboardEntryService, LeaderboardEntryService>();
            services.AddScoped<ICertificateTemplateService, CertificateTemplateService>();
            services.AddScoped<ICertificateService, CertificateService>();
            services.AddScoped<IAppealService, AppealService>();
        }
    }
}
