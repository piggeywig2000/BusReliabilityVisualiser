using System.Xml;
using BodsDotNet;
using BodsDotNet.Schemas.Siri;
using BusReliabilityWeb.Database;
using BusReliabilityWeb.Database.Dto;
using BusReliabilityWeb.Map;
using BusReliabilityWeb.Timetable;

namespace BusReliabilityWeb
{
    public class LocationScraperService : BackgroundService
    {
        private readonly ILogger<LocationScraperService> logger;
        private readonly BodsClient bodsClient;
        private readonly IServiceProvider serviceProvider;
        private readonly TimetableFileManager timetableFileManager;
        private readonly Dictionary<string, DateTime> vehicleToRecordedTime = [];
        private readonly TimeSpan loopLength;

        public LocationScraperService(ILogger<LocationScraperService> logger, IConfiguration configuration, BodsClient bodsClient, IServiceProvider serviceProvider, TimetableFileManager timetableFileManager)
        {
            this.logger = logger;
            this.bodsClient = bodsClient;
            this.serviceProvider = serviceProvider;
            this.timetableFileManager = timetableFileManager;
            loopLength = TimeSpan.FromSeconds(configuration.GetValue<int>("ScraperLoopSeconds"));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Starting location scraper service");

            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime startTime = DateTime.UtcNow;

                try
                {
                    await ScrapeOnce(stoppingToken);
                }
                catch (Exception e)
                {
                    if (e is OperationCanceledException)
                    {
                        throw; // Don't catch TaskCanceledExceptions, let it throw
                    }
                    logger.LogError(e, "An exception occurred during location scraping. Scraping will continue.");
                }

                // Wait until next cycle
                TimeSpan elapsed = DateTime.UtcNow - startTime;
                logger.LogDebug("Scraping took {elapsed:F0}ms out of {budget:F0}ms, waiting for {waitLength:F0}ms", elapsed.TotalMilliseconds, loopLength.TotalMilliseconds, (loopLength - elapsed).TotalMilliseconds);
                if (elapsed < loopLength)
                {
                    await Task.Delay(loopLength - elapsed, stoppingToken);
                }
                else
                {
                    logger.LogWarning("Exceeded loop budget. Scraping took {elapsed:F0}ms out of {budget:F0}ms, exceeding the budget by {exceeded:F0}ms", elapsed.TotalMilliseconds, loopLength.TotalMilliseconds, (elapsed - loopLength).TotalMilliseconds);
                }
            }
        }

        private async Task ScrapeOnce(CancellationToken cancellationToken)
        {
            DateTime now = Util.GmtNow;
            DateOnly timetableDate = DateOnly.FromDateTime(now);
            TimeOnly timetableTime = TimeOnly.FromDateTime(now);
            if (timetableTime >= new TimeOnly(04, 00) && timetableTime < new TimeOnly(05, 00))
                return; // Don't bother if between 4am and 5am (timetable switches to next day, I can't be bothered to deal with this)
            else if (timetableTime <= new TimeOnly(04, 00))
                timetableDate = timetableDate.AddDays(-1); // Use yesterday's timetable if before 4am

            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
            DbController dbController = scope.ServiceProvider.GetRequiredService<DbController>();

            // TODO: Parallelise this, when we have lots of lines this will not run very fast
            foreach (TimetableLine line in timetableFileManager.GetAllTimetablesAtDate(timetableDate).SelectMany(s => s.Lines))
            {
                List<TracePoint> tpsToAdd = [];

                Siri loc = await bodsClient.GetLocation(line.Operators, line.LineName, line.BlockIds, cancellationToken);
                if (!loc.ServiceDelivery.VehicleMonitoringDeliverySpecified)
                    continue;

                tpsToAdd.Clear();

                foreach (VehicleActivityStructure va in loc.ServiceDelivery.VehicleMonitoringDelivery
                    .Where(vmd => vmd.VehicleActivitySpecified)
                    .SelectMany(vmd => vmd.VehicleActivity)
                    .Where(va => TimeOnly.FromDateTime(va.RecordedAtTime) < new TimeOnly(04, 00) || TimeOnly.FromDateTime(va.RecordedAtTime) >= new TimeOnly(05, 00)))
                {
                    if (vehicleToRecordedTime.TryGetValue(va.MonitoredVehicleJourney.VehicleRef.Value, out DateTime recordedAtTime) && recordedAtTime >= va.RecordedAtTime)
                        continue; // We've already got this data point
                    vehicleToRecordedTime[va.MonitoredVehicleJourney.VehicleRef.Value] = va.RecordedAtTime;

                    // It could still be in database but not in cache - double check from database
                    TracePoint? latestTp = await dbController.GetLatestTracePoint(va.MonitoredVehicleJourney.VehicleRef.Value, cancellationToken);
                    if (latestTp != null && latestTp.RecordedAt >= va.RecordedAtTime)
                    {
                        vehicleToRecordedTime[va.MonitoredVehicleJourney.VehicleRef.Value] = latestTp.RecordedAt;
                        continue; // We've already got this point in the database
                    }

                    // Convert location to BNG
                    RoutePoint point = RoutePoint.FromSiri(va.MonitoredVehicleJourney.VehicleLocation);

                    XmlNamespaceManager nsManager = new(va.Extensions.Any[0].OwnerDocument.NameTable);
                    nsManager.AddNamespace("siri", "http://www.siri.org.uk/siri");

                    // Add new point to database
                    tpsToAdd.Add(new(
                        va.RecordedAtTime,
                        va.MonitoredVehicleJourney.VehicleRef.Value,
                        line.ServiceCode,
                        line.LineId,
                        va.Extensions.Any[0].SelectSingleNode("/siri:Operational/siri:TicketMachine/siri:TicketMachineServiceCode", nsManager)!.InnerText,
                        va.Extensions.Any[0].SelectSingleNode("/siri:Operational/siri:TicketMachine/siri:JourneyCode", nsManager)!.InnerText,
                        point.Easting,
                        point.Northing,
                        va.MonitoredVehicleJourney.BearingSpecified ? va.MonitoredVehicleJourney.Bearing : null));
                }

                if (tpsToAdd.Count > 0)
                    await dbController.AddTracePoints(tpsToAdd, cancellationToken);
            }
        }
    }
}
