using Microsoft.EntityFrameworkCore;
using Serilog;
using Dapper;
using Microsoft.EntityFrameworkCore.SqlServer;
using Microsoft.Data.SqlClient;
using System.Data;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using Microsoft.OpenApi.Models;
/**/


var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console());
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var readConnectionString = builder.Configuration.GetConnectionString("ReadConnection") ?? connectionString;

builder.Services.AddDbContext<TripWriteDb>(opt =>
    opt.UseSqlServer(connectionString));

builder.Services.AddDbContext<TripReadDb>(opt =>
    opt.UseSqlServer(readConnectionString));

// Add health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "trip-db");

builder.Services.AddSingleton<Func<string, IDbConnection>>(sp =>
    connStr => new SqlConnection(connStr));

// gRPC channel to Telemetry service
builder.Services.AddSingleton(sp =>
{
    var channel = GrpcChannel.ForAddress("http://telemetry:5003");
    return new SmartFleet.Telemetry.TelemetryService.TelemetryServiceClient(channel);
});

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SmartFleet Trip API",
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
    var writeContext = scope.ServiceProvider.GetRequiredService<TripWriteDb>();
    var readContext = scope.ServiceProvider.GetRequiredService<TripReadDb>();
    try
    {
        writeContext.Database.EnsureCreated();
        readContext.Database.EnsureCreated();
        app.Logger.LogInformation("Trip databases initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing trip databases");
    }
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartFleet Trip API v1");
    c.RoutePrefix = string.Empty; // Swagger UI at root "/"
});

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health");

// Write model - create trips (idempotent)
app.MapPost("/trips", async (TripCreateModel model, TripWriteDb db, HttpRequest req, SmartFleet.Telemetry.TelemetryService.TelemetryServiceClient telemetryClient) =>
{
    var idempotency = req.Headers["Idempotency-Key"].FirstOrDefault();
    if (!string.IsNullOrEmpty(idempotency) && !SmartFleet.Common.IdempotencyStore.TryAdd(idempotency, TimeSpan.FromMinutes(5)))
        return Results.Conflict("Duplicate request.");

    var trip = new Trip
    {
        Id = Guid.NewGuid(),
        DriverId = model.DriverId,
        VehicleId = model.VehicleId,
        StartLocation = model.StartLocation,
        EndLocation = model.EndLocation,
        Route = model.Route,
        Status = "Scheduled",
        ScheduledStartTime = model.ScheduledStartTime,
        EstimatedEndTime = model.EstimatedEndTime,
        TripType = model.TripType,
        Notes = model.Notes
    };

    db.Trips.Add(trip);
    await db.SaveChangesAsync();

    // Update read model
    using var conn = new SqlConnection(readConnectionString);
    await conn.ExecuteAsync(@"
        INSERT INTO trip_read (id, driverid, vehicleid, startlocation, endlocation, route, status,
                              scheduledstarttime, estimatedendtime, triptype, notes, createdat)
        VALUES (@Id, @DriverId, @VehicleId, @StartLocation, @EndLocation, @Route, @Status,
                @ScheduledStartTime, @EstimatedEndTime, @TripType, @Notes, @CreatedAt)",
        new
        {
            trip.Id,
            trip.DriverId,
            trip.VehicleId,
            trip.StartLocation,
            trip.EndLocation,
            trip.Route,
            trip.Status,
            trip.ScheduledStartTime,
            trip.EstimatedEndTime,
            trip.TripType,
            trip.Notes,
            trip.CreatedAt
        });

    // Send initial telemetry
    try
    {
        var telemetryRequest = new SmartFleet.Telemetry.TelemetryRequest
        {
            TripId = trip.Id.ToString(),
            VehicleId = trip.VehicleId.ToString(),
            Latitude = "0.0",
            Longitude = "0.0",
            Status = trip.Status
        };

        var telemetryResponse = await telemetryClient.RecordTelemetryAsync(telemetryRequest);
        if (!telemetryResponse.Success)
        {
            Console.WriteLine($"Telemetry failed for Trip {trip.Id}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Telemetry service unavailable: {ex.Message}");
    }

    return Results.Created($"/trips/{trip.Id}", trip);
}).RequireAuthorization("AdminOrDispatcher");

// Read model - optimized queries
app.MapGet("/trips", async (string? status, Guid? driverId, Guid? vehicleId, Func<string, IDbConnection> connFactory) =>
{
    using var conn = connFactory(readConnectionString);
    var sql = @"SELECT id, driverid as DriverId, vehicleid as VehicleId, startlocation as StartLocation,
                       endlocation as EndLocation, route, status, scheduledstarttime as ScheduledStartTime,
                       estimatedendtime as EstimatedEndTime, actualstarttime as ActualStartTime,
                       actualendtime as ActualEndTime, triptype as TripType, notes,
                       distancetraveled as DistanceTraveled, fuelconsumed as FuelConsumed,
                       createdat as CreatedAt FROM trip_read WHERE 1=1";

    var parameters = new DynamicParameters();

    if (!string.IsNullOrEmpty(status))
    {
        sql += " AND status = @status";
        parameters.Add("status", status);
    }

    if (driverId.HasValue)
    {
        sql += " AND driverid = @driverId";
        parameters.Add("driverId", driverId);
    }

    if (vehicleId.HasValue)
    {
        sql += " AND vehicleid = @vehicleId";
        parameters.Add("vehicleId", vehicleId);
    }

    sql += " ORDER BY scheduledstarttime DESC";

    var res = await conn.QueryAsync<TripReadModel>(sql, parameters);
    return Results.Ok(res);
}).RequireAuthorization();

app.MapGet("/trips/{id}", async (Guid id, Func<string, IDbConnection> connFactory) =>
{
    using var conn = connFactory(readConnectionString);
    var sql = @"SELECT id, driverid as DriverId, vehicleid as VehicleId, startlocation as StartLocation,
                       endlocation as EndLocation, route, status, scheduledstarttime as ScheduledStartTime,
                       estimatedendtime as EstimatedEndTime, actualstarttime as ActualStartTime,
                       actualendtime as ActualEndTime, triptype as TripType, notes,
                       distancetraveled as DistanceTraveled, fuelconsumed as FuelConsumed,
                       createdat as CreatedAt FROM trip_read WHERE id = @id";

    var trip = await conn.QueryFirstOrDefaultAsync<TripReadModel>(sql, new { id });
    return trip is not null ? Results.Ok(trip) : Results.NotFound();
}).RequireAuthorization();


// Update trip - change route or status
app.MapPut("/trips/{id}", async (Guid id, TripUpdateModel update, TripWriteDb db, SmartFleet.Telemetry.TelemetryService.TelemetryServiceClient telemetryClient) =>
{
    var trip = await db.Trips.FindAsync(id);

    if (trip is null)
        return Results.NotFound($"Trip {id} not found");

    // Update fields
    if (!string.IsNullOrEmpty(update.Route))
        trip.Route = update.Route;
    if (!string.IsNullOrEmpty(update.Status))
        trip.Status = update.Status;
    if (update.ActualStartTime.HasValue)
        trip.ActualStartTime = update.ActualStartTime;
    if (update.ActualEndTime.HasValue)
        trip.ActualEndTime = update.ActualEndTime;
    if (update.DistanceTraveled.HasValue)
        trip.DistanceTraveled = update.DistanceTraveled;
    if (update.FuelConsumed.HasValue)
        trip.FuelConsumed = update.FuelConsumed;
    if (!string.IsNullOrEmpty(update.Notes))
        trip.Notes = update.Notes;

    trip.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Update read model
    using var conn = new SqlConnection(readConnectionString);
    await conn.ExecuteAsync(@"
        UPDATE trip_read SET
            route = @Route, status = @Status, actualstarttime = @ActualStartTime,
            actualendtime = @ActualEndTime, distancetraveled = @DistanceTraveled,
            fuelconsumed = @FuelConsumed, notes = @Notes
        WHERE id = @Id",
        new
        {
            trip.Route,
            trip.Status,
            trip.ActualStartTime,
            trip.ActualEndTime,
            trip.DistanceTraveled,
            trip.FuelConsumed,
            trip.Notes,
            trip.Id
        });

    // Send telemetry update
    try
    {
        await telemetryClient.RecordTelemetryAsync(new SmartFleet.Telemetry.TelemetryRequest
        {
            TripId = trip.Id.ToString(),
            VehicleId = trip.VehicleId.ToString(),
            Latitude = "12.9716", // This should come from GPS device
            Longitude = "77.5946",
            Status = trip.Status
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Telemetry service unavailable: {ex.Message}");
    }

    return Results.Ok(trip);
}).RequireAuthorization("AdminOrDispatcher");

// Start trip endpoint
app.MapPost("/trips/{id}/start", async (Guid id, TripWriteDb db, SmartFleet.Telemetry.TelemetryService.TelemetryServiceClient telemetryClient) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null)
        return Results.NotFound($"Trip {id} not found");

    if (trip.Status != "Scheduled")
        return Results.BadRequest("Trip can only be started from Scheduled status");

    trip.Status = "InProgress";
    trip.ActualStartTime = DateTime.UtcNow;
    trip.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Update read model
    using var conn = new SqlConnection(readConnectionString);
    await conn.ExecuteAsync(@"
        UPDATE trip_read SET status = @Status, actualstarttime = @ActualStartTime
        WHERE id = @Id",
        new { trip.Status, trip.ActualStartTime, trip.Id });

    // Send telemetry
    try
    {
        await telemetryClient.RecordTelemetryAsync(new SmartFleet.Telemetry.TelemetryRequest
        {
            TripId = trip.Id.ToString(),
            VehicleId = trip.VehicleId.ToString(),
            Status = trip.Status
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Telemetry service unavailable: {ex.Message}");
    }

    return Results.Ok(trip);
}).RequireAuthorization("DriverOnly");

// Complete trip endpoint
app.MapPost("/trips/{id}/complete", async (Guid id, TripWriteDb db, SmartFleet.Telemetry.TelemetryService.TelemetryServiceClient telemetryClient) =>
{
    var trip = await db.Trips.FindAsync(id);
    if (trip is null)
        return Results.NotFound($"Trip {id} not found");

    if (trip.Status != "InProgress")
        return Results.BadRequest("Trip can only be completed from InProgress status");

    trip.Status = "Completed";
    trip.ActualEndTime = DateTime.UtcNow;
    trip.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Update read model
    using var conn = new SqlConnection(readConnectionString);
    await conn.ExecuteAsync(@"
        UPDATE trip_read SET status = @Status, actualendtime = @ActualEndTime
        WHERE id = @Id",
        new { trip.Status, trip.ActualEndTime, trip.Id });

    // Send telemetry
    try
    {
        await telemetryClient.RecordTelemetryAsync(new SmartFleet.Telemetry.TelemetryRequest
        {
            TripId = trip.Id.ToString(),
            VehicleId = trip.VehicleId.ToString(),
            Status = trip.Status
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Telemetry service unavailable: {ex.Message}");
    }

    return Results.Ok(trip);
}).RequireAuthorization("DriverOnly");


app.Run();

public class TripCreateModel
{
    public Guid DriverId { get; set; }
    public Guid VehicleId { get; set; }
    public string StartLocation { get; set; } = "";
    public string EndLocation { get; set; } = "";
    public string Route { get; set; } = "";
    public DateTime ScheduledStartTime { get; set; }
    public DateTime EstimatedEndTime { get; set; }
    public string TripType { get; set; } = "Regular"; // Regular, Emergency, Maintenance
    public string Notes { get; set; } = "";
}

public class Trip
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid VehicleId { get; set; }
    public string StartLocation { get; set; } = "";
    public string EndLocation { get; set; } = "";
    public string Route { get; set; } = "";
    public string Status { get; set; } = "Scheduled"; // Scheduled, InProgress, Completed, Cancelled
    public DateTime ScheduledStartTime { get; set; }
    public DateTime EstimatedEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public string TripType { get; set; } = "Regular";
    public string Notes { get; set; } = "";
    public double? DistanceTraveled { get; set; }
    public double? FuelConsumed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TripReadModel
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid VehicleId { get; set; }
    public string StartLocation { get; set; } = "";
    public string EndLocation { get; set; } = "";
    public string Route { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime ScheduledStartTime { get; set; }
    public DateTime EstimatedEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public string TripType { get; set; } = "";
    public string Notes { get; set; } = "";
    public double? DistanceTraveled { get; set; }
    public double? FuelConsumed { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TripUpdateModel
{
    public string? Route { get; set; }
    public string? Status { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public double? DistanceTraveled { get; set; }
    public double? FuelConsumed { get; set; }
    public string? Notes { get; set; }
}

public class TripWriteDb : DbContext
{
    public TripWriteDb(DbContextOptions<TripWriteDb> options) : base(options) { }
    public DbSet<Trip> Trips => Set<Trip>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Trip>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StartLocation).IsRequired().HasMaxLength(200);
            entity.Property(e => e.EndLocation).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Route).HasMaxLength(1000);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.TripType).HasMaxLength(20);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DriverId);
            entity.HasIndex(e => e.VehicleId);
            entity.HasIndex(e => e.ScheduledStartTime);
        });
    }
}

public class TripReadDb : DbContext
{
    public TripReadDb(DbContextOptions<TripReadDb> options) : base(options) { }
    public DbSet<TripReadModel> TripReads => Set<TripReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TripReadModel>(entity =>
        {
            entity.ToTable("trip_read");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DriverId).HasColumnName("driverid");
            entity.Property(e => e.VehicleId).HasColumnName("vehicleid");
            entity.Property(e => e.StartLocation).HasColumnName("startlocation").HasMaxLength(200);
            entity.Property(e => e.EndLocation).HasColumnName("endlocation").HasMaxLength(200);
            entity.Property(e => e.Route).HasColumnName("route").HasMaxLength(1000);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.ScheduledStartTime).HasColumnName("scheduledstarttime");
            entity.Property(e => e.EstimatedEndTime).HasColumnName("estimatedendtime");
            entity.Property(e => e.ActualStartTime).HasColumnName("actualstarttime");
            entity.Property(e => e.ActualEndTime).HasColumnName("actualendtime");
            entity.Property(e => e.TripType).HasColumnName("triptype").HasMaxLength(20);
            entity.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(1000);
            entity.Property(e => e.DistanceTraveled).HasColumnName("distancetraveled");
            entity.Property(e => e.FuelConsumed).HasColumnName("fuelconsumed");
            entity.Property(e => e.CreatedAt).HasColumnName("createdat");
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DriverId);
            entity.HasIndex(e => e.VehicleId);
            entity.HasIndex(e => e.ScheduledStartTime);
        });
    }
}
