using Grpc.Core;
using SmartFleet.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace SmartFleet.Telemetry.Services
{
    public class TelemetryService : SmartFleet.Telemetry.TelemetryService.TelemetryServiceBase
    {
        private readonly TelemetryDbContext _context;
        private readonly ILogger<TelemetryService> _logger;

        public TelemetryService(TelemetryDbContext context, ILogger<TelemetryService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public override async Task StreamTelemetry(
            IAsyncStreamReader<TelemetryData> requestStream,
            IServerStreamWriter<TelemetryAck> responseStream,
            ServerCallContext context)
        {
            await foreach (var item in requestStream.ReadAllAsync())
            {
                try
                {
                    // Store telemetry data in database
                    var telemetryRecord = new TelemetryRecord
                    {
                        TripId = item.TripId,
                        VehicleId = item.VehicleId,
                        Latitude = item.Latitude,
                        Longitude = item.Longitude,
                        Speed = item.Speed,
                        FuelLevel = item.FuelLevel,
                        Status = item.Status,
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(item.Timestamp).DateTime
                    };

                    _context.TelemetryRecords.Add(telemetryRecord);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Telemetry from {item.VehicleId}: speed={item.Speed}, fuel={item.FuelLevel}, location=({item.Latitude},{item.Longitude})");

                    await responseStream.WriteAsync(new TelemetryAck
                    {
                        Message = $"Received and stored telemetry from {item.VehicleId}"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing telemetry from {item.VehicleId}");
                    await responseStream.WriteAsync(new TelemetryAck
                    {
                        Message = $"Error processing telemetry from {item.VehicleId}: {ex.Message}"
                    });
                }
            }
        }

        public override async Task<TelemetryResponse> RecordTelemetry(TelemetryRequest request, ServerCallContext context)
        {
            try
            {
                var telemetryRecord = new TelemetryRecord
                {
                    TripId = request.TripId,
                    VehicleId = request.VehicleId,
                    Latitude = double.TryParse(request.Latitude, out var lat) ? lat : 0.0,
                    Longitude = double.TryParse(request.Longitude, out var lng) ? lng : 0.0,
                    Speed = request.Speed,
                    FuelLevel = request.FuelLevel,
                    Status = request.Status
                };

                _context.TelemetryRecords.Add(telemetryRecord);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Recorded telemetry for Trip {request.TripId}, Vehicle {request.VehicleId}");

                return new TelemetryResponse
                {
                    Success = true,
                    Message = "Telemetry recorded successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording telemetry for Trip {request.TripId}");
                return new TelemetryResponse
                {
                    Success = false,
                    Message = $"Error recording telemetry: {ex.Message}"
                };
            }
        }
    }
}
