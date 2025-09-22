using SmartFleet.Telemetry.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace SmartFleet.Telemetry
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add Serilog
            builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console());

            // Add Entity Framework
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            builder.Services.AddDbContext<TelemetryDbContext>(opt =>
                opt.UseSqlServer(connectionString));

            // Add health checks
            builder.Services.AddHealthChecks()
                .AddSqlServer(connectionString, name: "telemetry-db");

            // Add services to the container.
            builder.Services.AddGrpc();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.MapGrpcService<SmartFleet.Telemetry.Services.TelemetryService>();

            // Health check endpoint
            app.MapHealthChecks("/health");

            app.MapGet("/", () => "SmartFleet Telemetry Service - gRPC endpoints available");

            app.Run();
        }
    }

    public class TelemetryRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string TripId { get; set; } = "";
        public string VehicleId { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Speed { get; set; }
        public double FuelLevel { get; set; }
        public string Status { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class TelemetryDbContext : DbContext
    {
        public TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : base(options) { }
        public DbSet<TelemetryRecord> TelemetryRecords => Set<TelemetryRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TelemetryRecord>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TripId).HasMaxLength(50);
                entity.Property(e => e.VehicleId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasIndex(e => e.VehicleId);
                entity.HasIndex(e => e.TripId);
                entity.HasIndex(e => e.Timestamp);
            });
        }
    }
}