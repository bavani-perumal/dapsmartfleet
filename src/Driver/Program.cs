using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using Serilog;


var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console());

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<DriverDbContext>(opt =>
    opt.UseSqlServer(connectionString));

// Add health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "driver-db");

//builder.Services.AddControllers();

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
    options.AddPolicy("DriverOnly", policy => policy.RequireRole("Driver", "admin"));
});

// Register Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartFleet Driver API",
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
    var context = scope.ServiceProvider.GetRequiredService<DriverDbContext>();
    try
    {
        context.Database.EnsureCreated();
        app.Logger.LogInformation("Driver database initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing driver database");
    }
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartFleet Driver API v1");
    c.RoutePrefix = string.Empty; // Swagger UI at root "/"
});

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health");

app.MapGet("/drivers", async (DriverDbContext db, string? status = null) =>
{
    var query = db.Drivers.Include(d => d.Assignments.Where(a => a.IsActive)).AsQueryable();

    if (!string.IsNullOrEmpty(status))
        query = query.Where(d => d.Status == status);

    return await query.ToListAsync();
}).RequireAuthorization("AdminOrDispatcher");

app.MapGet("/drivers/{id}", async (Guid id, DriverDbContext db) =>
{
    var driver = await db.Drivers
        .Include(d => d.Assignments)
        .FirstOrDefaultAsync(d => d.Id == id);
    return driver is not null ? Results.Ok(driver) : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/drivers", async (DriverCreateDto driverDto, DriverDbContext db, HttpRequest req) =>
{
    var id = req.Headers["Idempotency-Key"].FirstOrDefault();
    if (!string.IsNullOrEmpty(id) && !SmartFleet.Common.IdempotencyStore.TryAdd(id, TimeSpan.FromMinutes(5)))
        return Results.Conflict("Duplicate request.");

    var driver = new Driver
    {
        Name = driverDto.Name,
        Email = driverDto.Email,
        Phone = driverDto.Phone,
        LicenseNumber = driverDto.LicenseNumber,
        LicenseClass = driverDto.LicenseClass,
        LicenseExpiryDate = driverDto.LicenseExpiryDate,
        DateOfBirth = driverDto.DateOfBirth,
        HireDate = driverDto.HireDate,
        Address = driverDto.Address,
        EmergencyContact = driverDto.EmergencyContact,
        EmergencyPhone = driverDto.EmergencyPhone
    };

    db.Drivers.Add(driver);
    await db.SaveChangesAsync();
    return Results.Created($"/drivers/{driver.Id}", driver);
}).RequireAuthorization("AdminOnly");

app.MapPut("/drivers/{id}", async (Guid id, DriverUpdateDto updateDto, DriverDbContext db) =>
{
    var driver = await db.Drivers.FindAsync(id);
    if (driver is null)
        return Results.NotFound();

    if (!string.IsNullOrEmpty(updateDto.Status))
        driver.Status = updateDto.Status;
    if (!string.IsNullOrEmpty(updateDto.Phone))
        driver.Phone = updateDto.Phone;
    if (!string.IsNullOrEmpty(updateDto.Address))
        driver.Address = updateDto.Address;
    if (!string.IsNullOrEmpty(updateDto.EmergencyContact))
        driver.EmergencyContact = updateDto.EmergencyContact;
    if (!string.IsNullOrEmpty(updateDto.EmergencyPhone))
        driver.EmergencyPhone = updateDto.EmergencyPhone;
    if (updateDto.Rating.HasValue)
        driver.Rating = updateDto.Rating.Value;

    driver.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(driver);
}).RequireAuthorization("AdminOrDispatcher");

app.MapPost("/drivers/{id}/assign", async (Guid id, DriverAssignmentDto assignmentDto, DriverDbContext db) =>
{
    var driver = await db.Drivers.FindAsync(id);
    if (driver is null)
        return Results.NotFound("Driver not found");

    // Unassign from current vehicle if any
    var currentAssignment = await db.DriverAssignments
        .FirstOrDefaultAsync(a => a.DriverId == id && a.IsActive);
    if (currentAssignment is not null)
    {
        currentAssignment.IsActive = false;
        currentAssignment.UnassignedAt = DateTime.UtcNow;
    }

    // Create new assignment
    var assignment = new DriverAssignment
    {
        DriverId = id,
        VehicleId = assignmentDto.VehicleId,
        AssignedBy = assignmentDto.AssignedBy
    };

    db.DriverAssignments.Add(assignment);
    driver.Status = "OnTrip";
    driver.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(assignment);
}).RequireAuthorization("AdminOrDispatcher");

app.MapPost("/drivers/{id}/unassign", async (Guid id, DriverDbContext db) =>
{
    var assignment = await db.DriverAssignments
        .FirstOrDefaultAsync(a => a.DriverId == id && a.IsActive);
    if (assignment is null)
        return Results.NotFound("No active assignment found");

    assignment.IsActive = false;
    assignment.UnassignedAt = DateTime.UtcNow;

    var driver = await db.Drivers.FindAsync(id);
    if (driver is not null)
    {
        driver.Status = "Available";
        driver.UpdatedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization("AdminOrDispatcher");

app.Run();

public class Driver
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string LicenseClass { get; set; } = ""; // A, B, C, CDL, etc.
    public DateTime LicenseExpiryDate { get; set; }
    public string Status { get; set; } = "Available"; // Available, OnTrip, OffDuty, Suspended
    public DateTime DateOfBirth { get; set; }
    public DateTime HireDate { get; set; }
    public string Address { get; set; } = "";
    public string EmergencyContact { get; set; } = "";
    public string EmergencyPhone { get; set; } = "";
    public double Rating { get; set; } = 5.0; // Driver performance rating
    public int TotalTrips { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public List<DriverAssignment> Assignments { get; set; } = new();
}

public class DriverAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DriverId { get; set; }
    public Guid VehicleId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnassignedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string AssignedBy { get; set; } = ""; // User who made the assignment

    // Navigation property
    public Driver Driver { get; set; } = null!;
}

public class DriverCreateDto
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string LicenseClass { get; set; } = "";
    public DateTime LicenseExpiryDate { get; set; }
    public DateTime DateOfBirth { get; set; }
    public DateTime HireDate { get; set; }
    public string Address { get; set; } = "";
    public string EmergencyContact { get; set; } = "";
    public string EmergencyPhone { get; set; } = "";
}

public class DriverUpdateDto
{
    public string? Status { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? EmergencyContact { get; set; }
    public string? EmergencyPhone { get; set; }
    public double? Rating { get; set; }
}

public class DriverAssignmentDto
{
    public Guid VehicleId { get; set; }
    public string AssignedBy { get; set; } = "";
}

public class DriverDbContext : DbContext
{
    public DriverDbContext(DbContextOptions<DriverDbContext> options) : base(options) { }
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<DriverAssignment> DriverAssignments => Set<DriverAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Driver>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LicenseNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.LicenseClass).HasMaxLength(10);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.EmergencyContact).HasMaxLength(100);
            entity.Property(e => e.EmergencyPhone).HasMaxLength(20);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.LicenseNumber).IsUnique();
        });

        modelBuilder.Entity<DriverAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AssignedBy).HasMaxLength(100);
            entity.HasOne(e => e.Driver)
                  .WithMany(d => d.Assignments)
                  .HasForeignKey(e => e.DriverId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
