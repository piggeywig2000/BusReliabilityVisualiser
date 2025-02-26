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

        public LocationScraperService(ILogger<LocationScraperService> logger, BodsClient bodsClient, IServiceProvider serviceProvider, TimetableFileManager timetableFileManager)
        {
            this.logger = logger;
            this.bodsClient = bodsClient;
            this.serviceProvider = serviceProvider;
            this.timetableFileManager = timetableFileManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Starting location scraper service");

            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
            DbController dbController = scope.ServiceProvider.GetRequiredService<DbController>();

            foreach (TimetableLine line in timetableFileManager.GetAllTimetablesAtDate(DateOnly.FromDateTime(Util.GmtNow)).SelectMany(s => s.Lines)) // TODO: Make this work with night buses following yesterday's timetable
            {
                List<TracePoint> tpsToAdd = [];

                Siri loc = await bodsClient.GetLocation(line.Operators, line.LineName, line.BlockIds, stoppingToken);
                if (!loc.ServiceDelivery.VehicleMonitoringDeliverySpecified)
                    continue;

                tpsToAdd.Clear();

                foreach (VehicleActivityStructure va in loc.ServiceDelivery.VehicleMonitoringDelivery
                    .Where(vmd => vmd.VehicleActivitySpecified)
                    .SelectMany(vmd => vmd.VehicleActivity))
                {
                    if (vehicleToRecordedTime.ContainsKey(va.MonitoredVehicleJourney.VehicleRef.Value) && vehicleToRecordedTime[va.MonitoredVehicleJourney.VehicleRef.Value] >= va.RecordedAtTime)
                        continue; // We've already got this data point
                    vehicleToRecordedTime[va.MonitoredVehicleJourney.VehicleRef.Value] = va.RecordedAtTime;

                    // It could still be in database but not in cache - double check from database
                    TracePoint? latestTp = await dbController.GetLatestTracePoint(va.MonitoredVehicleJourney.VehicleRef.Value);
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

                await dbController.AddTracePoints(tpsToAdd);
            }
        }
    }
}
