using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SmartFleet.Telemetry.Protos;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console());
builder.Services.AddGrpc();
builder.Services.AddOpenTelemetryMetrics(builder => builder.AddAspNetCoreInstrumentation());
var app = builder.Build();

app.MapGet("/", () => "Telemetry gRPC service. Use gRPC clients to stream telemetry.");
app.MapGrpcService<TelemetryServiceImpl>();

app.Run();

public class TelemetryServiceImpl : Telemetry.TelemetryBase
{
    public override async Task StreamTelemetry(IAsyncStreamReader<TelemetryData> requestStream, IServerStreamWriter<TelemetryAck> responseStream, ServerCallContext context)
    {
        await foreach (var item in requestStream.ReadAllAsync())
        {
            Console.WriteLine($"Telemetry from {item.VehicleId} at {item.Timestamp}: lat={item.Latitude}, lon={item.Longitude}, speed={item.Speed}, fuel={item.FuelLevel}");
            var ack = new TelemetryAck { Message = $"Received {item.VehicleId} @ {item.Timestamp}" };
            await responseStream.WriteAsync(ack);
        }
    }
}
