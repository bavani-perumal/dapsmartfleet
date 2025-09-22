using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models; // needed for Swagger security
using System.Text;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console()); 


//

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<VehicleDbContext>(opt =>
    opt.UseSqlServer(connectionString));

// Add health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "vehicle-db");

builder.Services.AddEndpointsApiExplorer();

//  Add Swagger + JWT config
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartFleet Vehicle API",
        Version = "v1"
    });

    // JWT security definition
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

    var securityRequirement = new OpenApiSecurityRequirement
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
    };

    c.AddSecurityRequirement(securityRequirement);
});

//  JWT secret
var jwtSecret =
    builder.Configuration["JWT_SECRET"] ?? "SuperSecretKey12345";

//Environment.GetEnvironmentVariable("JWT_SECRET") ?? "FallbackSecret";

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

//  Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin", "admin"));
    options.AddPolicy("AdminOrDispatcher", policy => policy.RequireRole("Admin", "Dispatcher", "admin"));
    options.AddPolicy("DriverOnly", policy => policy.RequireRole("Driver", "admin"));
});

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<VehicleDbContext>();
    try
    {
        context.Database.EnsureCreated();
        app.Logger.LogInformation("Vehicle database initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing vehicle database");
    }
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartFleet Vehicle API v1");
    c.RoutePrefix = string.Empty; // Swagger UI at root "/"
});

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health");

// Debug endpoint to check claims
//app.MapGet("/debug/claims", (HttpContext context) =>
//{
//    var claims = context.User.Claims.Select(c => new { c.Type, c.Value }).ToList();
//    var isAuthenticated = context.User.Identity?.IsAuthenticated ?? false;
//    var customRoles = context.User.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToList();
//    var standardRoles = context.User.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role").Select(c => c.Value).ToList();
//    var isInAdminRole = context.User.IsInRole("Admin");

//    return Results.Ok(new {
//        IsAuthenticated = isAuthenticated,
//        Claims = claims,
//        CustomRoles = customRoles,
//        StandardRoles = standardRoles,
//        IsInAdminRole = isInAdminRole
//    });
//}).RequireAuthorization();

// Protected endpoints
app.MapGet("/vehicles", async (VehicleDbContext db, string? status = null, string? type = null) =>
{
    var query = db.Vehicles.AsQueryable();

    if (!string.IsNullOrEmpty(status))
        query = query.Where(v => v.Status == status);

    if (!string.IsNullOrEmpty(type))
        query = query.Where(v => v.Type == type);

    return await query.ToListAsync();
}).RequireAuthorization("AdminOrDispatcher");

app.MapGet("/vehicles/{id}", async (Guid id, VehicleDbContext db) =>
{
    var vehicle = await db.Vehicles.FindAsync(id);
    return vehicle is not null ? Results.Ok(vehicle) : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/vehicles", async (VehicleCreateDto vehicleDto, VehicleDbContext db, HttpRequest req) =>
{
    var id = req.Headers["Idempotency-Key"].FirstOrDefault();
    if (!string.IsNullOrEmpty(id) && !SmartFleet.Common.IdempotencyStore.TryAdd(id, TimeSpan.FromMinutes(5)))
        return Results.Conflict("Duplicate request.");

    var vehicle = new Vehicle
    {
        Registration = vehicleDto.Registration,
        Type = vehicleDto.Type,
        Capacity = vehicleDto.Capacity,
        Make = vehicleDto.Make,
        Model = vehicleDto.Model,
        Year = vehicleDto.Year,
        VIN = vehicleDto.VIN,
        FuelCapacity = vehicleDto.FuelCapacity,
        FuelType = vehicleDto.FuelType,
        NextMaintenanceDate = vehicleDto.NextMaintenanceDate
    };

    db.Vehicles.Add(vehicle);
    await db.SaveChangesAsync();
    return Results.Created($"/vehicles/{vehicle.Id}", vehicle);
}).RequireAuthorization("AdminOnly");

app.MapPut("/vehicles/{id}", async (Guid id, VehicleUpdateDto updateDto, VehicleDbContext db) =>
{
    var vehicle = await db.Vehicles.FindAsync(id);
    if (vehicle is null)
        return Results.NotFound();

    if (!string.IsNullOrEmpty(updateDto.Status))
        vehicle.Status = updateDto.Status;
    if (updateDto.LastMaintenanceDate.HasValue)
        vehicle.LastMaintenanceDate = updateDto.LastMaintenanceDate.Value;
    if (updateDto.NextMaintenanceDate.HasValue)
        vehicle.NextMaintenanceDate = updateDto.NextMaintenanceDate.Value;
    if (updateDto.Mileage.HasValue)
        vehicle.Mileage = updateDto.Mileage.Value;

    vehicle.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(vehicle);
}).RequireAuthorization("AdminOrDispatcher");

app.MapDelete("/vehicles/{id}", async (Guid id, VehicleDbContext db) =>
{
    var vehicle = await db.Vehicles.FindAsync(id);
    if (vehicle is null)
        return Results.NotFound();

    db.Vehicles.Remove(vehicle);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

app.Run();

// --- Models ---
public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Registration { get; set; } = "";
    public string Type { get; set; } = ""; // e.g., Truck, Van, Car
    public int Capacity { get; set; } // Passenger or cargo capacity
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public int Year { get; set; }
    public string VIN { get; set; } = ""; // Vehicle Identification Number
    public string Status { get; set; } = "Available"; // Available, InUse, Maintenance, OutOfService
    public double FuelCapacity { get; set; }
    public string FuelType { get; set; } = ""; // Diesel, Petrol, Electric, Hybrid
    public DateTime LastMaintenanceDate { get; set; }
    public DateTime NextMaintenanceDate { get; set; }
    public double Mileage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class VehicleCreateDto
{
    public string Registration { get; set; } = "";
    public string Type { get; set; } = "";
    public int Capacity { get; set; }
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public int Year { get; set; }
    public string VIN { get; set; } = "";
    public double FuelCapacity { get; set; }
    public string FuelType { get; set; } = "";
    public DateTime NextMaintenanceDate { get; set; }
}

public class VehicleUpdateDto
{
    public string? Status { get; set; }
    public DateTime? LastMaintenanceDate { get; set; }
    public DateTime? NextMaintenanceDate { get; set; }
    public double? Mileage { get; set; }
}

public class VehicleDbContext : DbContext
{
    public VehicleDbContext(DbContextOptions<VehicleDbContext> options) : base(options) { }
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Registration).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Make).HasMaxLength(50);
            entity.Property(e => e.Model).HasMaxLength(50);
            entity.Property(e => e.VIN).HasMaxLength(17);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.FuelType).HasMaxLength(20);
            entity.HasIndex(e => e.Registration).IsUnique();
            entity.HasIndex(e => e.VIN).IsUnique();
        });
    }
}