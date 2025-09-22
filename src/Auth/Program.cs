using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console());

// Add Entity Framework
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuthDbContext>(opt =>
    opt.UseSqlServer(connectionString));

// Add health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "auth-db");

// Add services
builder.Services.AddScoped<IUserService, UserService>();

// JWT secret
var jwtSecret = builder.Configuration["JWT_SECRET"] ?? "SuperSecretKey12345";

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SmartFleet Auth API", Version = "v1" });

    // Add JWT security for Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer {token}'"
    });

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
            new string[]{}
        }
    });
});

// JWT auth validation
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType = ClaimTypes.Role //  important
        };
    });
builder.Services.AddAuthorization();
var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    try
    {
        context.Database.EnsureCreated();
        app.Logger.LogInformation("Database initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing database");
    }
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartFleet Auth API v1");
    c.RoutePrefix = string.Empty; // Swagger UI at root
});

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health");


// ----------------------
// AUTHENTICATION ENDPOINTS
// ----------------------
app.MapPost("/token", async (LoginRequest login, IUserService userService) =>
{
    var user = await userService.ValidateUserAsync(login.Username, login.Password);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    var token = GenerateJwtToken(user.Username, user.Role, jwtSecret);
    return Results.Ok(new {
        token = token,
        user = new {
            id = user.Id,
            username = user.Username,
            role = user.Role,
            email = user.Email
        }
    });
});

app.MapPost("/register", async (RegisterRequest request, IUserService userService) =>
{
    try
    {
        var user = await userService.CreateUserAsync(request.Username, request.Password, request.Email, request.Role);
        return Results.Created($"/users/{user.Id}", new {
            id = user.Id,
            username = user.Username,
            role = user.Role,
            email = user.Email
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/users", async (AuthDbContext db, string? role = null) =>
{
    var query = db.Users.AsQueryable();

    if (!string.IsNullOrEmpty(role))
        query = query.Where(u => u.Role == role);

    var users = await query
        .Select(u => new { u.Id, u.Username, u.Email, u.Role, u.IsActive, u.CreatedAt })
        .ToListAsync();

    return Results.Ok(users);
}).RequireAuthorization();

app.MapPut("/users/{id}/status", async (Guid id, UserStatusRequest request, AuthDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user == null)
        return Results.NotFound();

    user.IsActive = request.IsActive;
    user.UpdatedAt = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new { message = "User status updated successfully" });
}).RequireAuthorization();

app.Run();


// ----------------------
// Helper Methods
// ----------------------
string GenerateJwtToken(string username, string role, string secret)
{
    var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, username),
        new Claim("role", role)   //  IMPORTANT
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddHours(24),
        signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}


// ----------------------
// DTOs
// ----------------------
public record LoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Password, string Email, string Role);
public record UserStatusRequest(bool IsActive);

// ----------------------
// Models
// ----------------------
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = ""; // Admin, Dispatcher, Driver
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

// ----------------------
// Database Context
// ----------------------
public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Seed default users
        var adminPasswordHash = HashPassword("admin123");
        var dispatcherPasswordHash = HashPassword("dispatcher123");
        var driverPasswordHash = HashPassword("driver123");

        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Username = "admin",
                Email = "admin@smartfleet.com",
                PasswordHash = adminPasswordHash,
                Role = "Admin",
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Username = "dispatcher",
                Email = "dispatcher@smartfleet.com",
                PasswordHash = dispatcherPasswordHash,
                Role = "Dispatcher",
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Username = "driver",
                Email = "driver@smartfleet.com",
                PasswordHash = driverPasswordHash,
                Role = "Driver",
                CreatedAt = DateTime.UtcNow
            }
        );
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "SmartFleetSalt"));
        return Convert.ToBase64String(hashedBytes);
    }
}

// ----------------------
// Service Interface
// ----------------------
public interface IUserService
{
    Task<User?> ValidateUserAsync(string username, string password);
    Task<User> CreateUserAsync(string username, string password, string email, string role);
    Task<User?> GetUserByIdAsync(Guid id);
    Task<User?> GetUserByUsernameAsync(string username);
}

// ----------------------
// Service Implementation
// ----------------------
public class UserService : IUserService
{
    private readonly AuthDbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(AuthDbContext context, ILogger<UserService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<User?> ValidateUserAsync(string username, string password)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null)
            return null;

        var passwordHash = HashPassword(password);
        if (user.PasswordHash != passwordHash)
            return null;

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {Username} logged in successfully", username);
        return user;
    }

    public async Task<User> CreateUserAsync(string username, string password, string email, string role)
    {
        // Validate role
        var validRoles = new[] { "Admin", "Dispatcher", "Driver" };
        if (!validRoles.Contains(role))
            throw new InvalidOperationException("Invalid role specified");

        // Check if username already exists
        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username || u.Email == email);

        if (existingUser != null)
            throw new InvalidOperationException("Username or email already exists");

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = HashPassword(password),
            Role = role
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {Username} created successfully with role {Role}", username, role);
        return user;
    }

    public async Task<User?> GetUserByIdAsync(Guid id)
    {
        return await _context.Users.FindAsync(id);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "SmartFleetSalt"));
        return Convert.ToBase64String(hashedBytes);
    }
}
