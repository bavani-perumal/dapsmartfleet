using Serilog;
using Serilog.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "SmartFleet.NotificationService")
    .WriteTo.Console()
);

// Add Entity Framework
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<NotificationDbContext>(opt =>
    opt.UseSqlServer(connectionString));

// Add health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "notification-db");

// JWT Authentication
var jwtSecret = builder.Configuration["JWT_SECRET"] ?? "SuperSecretKey12345";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin", "admin"));
    options.AddPolicy("AdminOrDispatcher", policy => policy.RequireRole("Admin", "Dispatcher", "admin"));
});

// Add notification services
builder.Services.AddScoped<INotificationService, NotificationService>();

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartFleet Notification API",
        Version = "v1"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter 'Bearer {your JWT token}'",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    };

    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
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
});

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    try
    {
        context.Database.EnsureCreated();
        app.Logger.LogInformation("Notification database initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing notification database");
    }
}

// Configure Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartFleet Notification API v1");
    c.RoutePrefix = string.Empty;
});

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health");

// Middleware: Correlation ID
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString();

    context.Response.Headers["X-Correlation-ID"] = correlationId;

    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        context.TraceIdentifier = correlationId;
        await next();
    }
});

// Endpoints
app.MapPost("/notify/email", async (NotificationRequest request, INotificationService notificationService) =>
{
    var result = await notificationService.SendEmailAsync(request.To, request.Subject, request.Body, request.Type);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization();

app.MapPost("/notify/sms", async (NotificationRequest request, INotificationService notificationService) =>
{
    var result = await notificationService.SendSmsAsync(request.To, request.Body, request.Type);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization();

app.MapPost("/notify/maintenance-alert", async (MaintenanceAlertRequest request, INotificationService notificationService) =>
{
    var result = await notificationService.SendMaintenanceAlertAsync(request.VehicleId, request.MaintenanceType, request.DueDate, request.RecipientEmail);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization("AdminOrDispatcher");

app.MapPost("/notify/route-deviation", async (RouteDeviationRequest request, INotificationService notificationService) =>
{
    var result = await notificationService.SendRouteDeviationAlertAsync(request.TripId, request.VehicleId, request.DriverId, request.CurrentLocation, request.ExpectedLocation);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization("AdminOrDispatcher");

app.MapGet("/notifications", async (NotificationDbContext db, string? type = null, int page = 1, int pageSize = 50) =>
{
    var query = db.Notifications.AsQueryable();

    if (!string.IsNullOrEmpty(type))
        query = query.Where(n => n.Type == type);

    var notifications = await query
        .OrderByDescending(n => n.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(notifications);
}).RequireAuthorization("AdminOrDispatcher");

app.Run();

// Models
public record NotificationRequest(string To, string Subject, string Body, string Type = "General");
public record MaintenanceAlertRequest(string VehicleId, string MaintenanceType, DateTime DueDate, string RecipientEmail);
public record RouteDeviationRequest(string TripId, string VehicleId, string DriverId, string CurrentLocation, string ExpectedLocation);

public class NotificationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = ""; // Email, SMS, MaintenanceAlert, RouteDeviation
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = "Pending"; // Pending, Sent, Failed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RelatedEntityId { get; set; } // TripId, VehicleId, etc.
}

public class NotificationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public Guid? NotificationId { get; set; }
}

// Database Context
public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options) { }
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Recipient).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Subject).HasMaxLength(200);
            entity.Property(e => e.Body).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.Property(e => e.RelatedEntityId).HasMaxLength(50);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}

// Service Interface
public interface INotificationService
{
    Task<NotificationResult> SendEmailAsync(string to, string subject, string body, string type = "Email");
    Task<NotificationResult> SendSmsAsync(string to, string body, string type = "SMS");
    Task<NotificationResult> SendMaintenanceAlertAsync(string vehicleId, string maintenanceType, DateTime dueDate, string recipientEmail);
    Task<NotificationResult> SendRouteDeviationAlertAsync(string tripId, string vehicleId, string driverId, string currentLocation, string expectedLocation);
}

// Service Implementation
public class NotificationService : INotificationService
{
    private readonly NotificationDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(NotificationDbContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<NotificationResult> SendEmailAsync(string to, string subject, string body, string type = "Email")
    {
        var notification = new NotificationRecord
        {
            Type = type,
            Recipient = to,
            Subject = subject,
            Body = body,
            Status = "Pending"
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        try
        {
            // In production, integrate with real email service (SendGrid, AWS SES, etc.)
            _logger.LogInformation("Sending email to {To} | Subject: {Subject}", to, subject);

            // Simulate email sending
            await Task.Delay(100);

            notification.Status = "Sent";
            notification.SentAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new NotificationResult
            {
                Success = true,
                Message = "Email sent successfully",
                NotificationId = notification.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            notification.Status = "Failed";
            notification.ErrorMessage = ex.Message;
            await _context.SaveChangesAsync();

            return new NotificationResult
            {
                Success = false,
                Message = $"Failed to send email: {ex.Message}",
                NotificationId = notification.Id
            };
        }
    }

    public async Task<NotificationResult> SendSmsAsync(string to, string body, string type = "SMS")
    {
        var notification = new NotificationRecord
        {
            Type = type,
            Recipient = to,
            Subject = "SMS",
            Body = body,
            Status = "Pending"
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        try
        {
            // In production, integrate with real SMS service (Twilio, AWS SNS, etc.)
            _logger.LogInformation("Sending SMS to {To} | Body: {Body}", to, body);

            // Simulate SMS sending
            await Task.Delay(100);

            notification.Status = "Sent";
            notification.SentAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new NotificationResult
            {
                Success = true,
                Message = "SMS sent successfully",
                NotificationId = notification.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {To}", to);
            notification.Status = "Failed";
            notification.ErrorMessage = ex.Message;
            await _context.SaveChangesAsync();

            return new NotificationResult
            {
                Success = false,
                Message = $"Failed to send SMS: {ex.Message}",
                NotificationId = notification.Id
            };
        }
    }

    public async Task<NotificationResult> SendMaintenanceAlertAsync(string vehicleId, string maintenanceType, DateTime dueDate, string recipientEmail)
    {
        var subject = $"Maintenance Alert - Vehicle {vehicleId}";
        var body = $"Vehicle {vehicleId} requires {maintenanceType} maintenance. Due date: {dueDate:yyyy-MM-dd}. Please schedule maintenance as soon as possible.";

        var notification = new NotificationRecord
        {
            Type = "MaintenanceAlert",
            Recipient = recipientEmail,
            Subject = subject,
            Body = body,
            Status = "Pending",
            RelatedEntityId = vehicleId
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        // Send email notification
        return await SendEmailAsync(recipientEmail, subject, body, "MaintenanceAlert");
    }

    public async Task<NotificationResult> SendRouteDeviationAlertAsync(string tripId, string vehicleId, string driverId, string currentLocation, string expectedLocation)
    {
        var subject = $"Route Deviation Alert - Trip {tripId}";
        var body = $"Vehicle {vehicleId} (Driver {driverId}) has deviated from the planned route. Current location: {currentLocation}. Expected location: {expectedLocation}. Please investigate immediately.";

        var notification = new NotificationRecord
        {
            Type = "RouteDeviation",
            Recipient = "dispatcher@smartfleet.com", // In production, get from configuration
            Subject = subject,
            Body = body,
            Status = "Pending",
            RelatedEntityId = tripId
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        // Send email notification to dispatcher
        return await SendEmailAsync("dispatcher@smartfleet.com", subject, body, "RouteDeviation");
    }
}
