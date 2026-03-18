using InventoryHold.Infrastructure;
using InventoryHold.Infrastructure.Configuration;
using InventoryHold.WebApi.Middleware;
using InventoryHold.WebApi.Workers;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure (MongoDB, RabbitMQ, Outbox, HoldService) ─────────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ── WebApi workers ────────────────────────────────────────────────────────────
builder.Services.Configure<HoldExpirySettings>(
    builder.Configuration.GetSection(HoldExpirySettings.SectionName));
builder.Services.AddHostedService<HoldExpiryWorker>();

// ── Controllers ───────────────────────────────────────────────────────────────
builder.Services.AddControllers();

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseHttpsRedirection();
app.MapControllers();

app.Run();

