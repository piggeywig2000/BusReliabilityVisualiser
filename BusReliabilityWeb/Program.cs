using BodsDotNet;
using BusReliabilityWeb.Database;
using BusReliabilityWeb.Timetable;
using MySqlConnector;

namespace BusReliabilityWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add secret config file
            builder.Configuration.AddJsonFile("appsettings.Private.json", true, true);

            // Add services to the container.
            builder.Services.AddSingleton<BodsClient>(_ => new(builder.Configuration["BodsApiKey"] ?? throw new InvalidOperationException("No BODS API key provided")));
            builder.Services.AddScoped<MySqlConnection>(_ => new(builder.Configuration.GetConnectionString("bus_visualiser")));
            builder.Services.AddScoped<DbController>();
            builder.Services.AddSingleton<TimetableFileManager>();
            builder.Services.AddHostedService<TimetableUpdateService>();
            if (builder.Configuration.GetValue("DoLocationScraping", true))
                builder.Services.AddHostedService<LocationScraperService>();
            builder.Services.AddHostedService<DataProcessingService>();
            builder.Services.AddRazorPages();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseRouting();

            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapRazorPages()
               .WithStaticAssets();

            app.Run();
        }
    }
}
