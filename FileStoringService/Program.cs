using FileStoringService.Services;
using FileStoringService.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddGrpc();
builder.Services.AddControllers();

// Add DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
Console.WriteLine($"Database connection string: {connectionString}");
builder.Services.AddDbContext<FileDbContext>(options =>
    options.UseSqlite(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FileDbContext>();
    Console.WriteLine("Creating database...");
    dbContext.Database.EnsureCreated();
    Console.WriteLine("Database created successfully");
}

app.UseHttpsRedirection();

// Добавляем маршрутизацию контроллеров
app.MapControllers();

// Map gRPC service
app.MapGrpcService<FileStorageService>();

app.Run();
