
using Ocelot.Middleware;
using Ocelot.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
namespace SmartFleet.ApiGateway
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Configuration.AddJsonFile("ocelotconfig.json");
            // Add services to the container.
            // JWT secret must match Auth service
           // AddJwtBearer("Bearer", options =>
            // Add health check endpoint
            
            var jwtSecret = builder.Configuration["JWT_SECRET"] ?? "SuperSecretKey12345";
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
               .AddJwtBearer("Bearer", options =>
               {
                   options.TokenValidationParameters = new TokenValidationParameters
                   {
                       ValidateIssuer = false,
                       ValidateAudience = false,
                       ValidateIssuerSigningKey = true,
                       IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                       RoleClaimType = ClaimTypes.Role
                   };
               });
            builder.Services.AddAuthorization();
            builder.Services.AddOcelot();
            builder.Services.AddControllers();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapWhen(context => context.Request.Path.StartsWithSegments("/health"),
                 appBuilder => {
                     appBuilder.Run(async context => {
                         context.Response.StatusCode = 200;
                         await context.Response.WriteAsync("Healthy");
                     });
                 });
            await app.UseOcelot();
            // app.MapControllers();

            app.Run();
        }
    }
}
