using BodsDotNet;
using BodsDotNet.Schemas.TransXChange;
using BusReliabilityWeb.Database;
using BusReliabilityWeb.Database.Dto;
using BusReliabilityWeb.Timetable;
using Microsoft.Extensions.Logging;

namespace BusReliabilityWeb
{
    public class DataProcessingService : BackgroundService
    {
        private readonly ILogger<DataProcessingService> logger;
        private readonly IServiceProvider serviceProvider;
        private readonly TimetableFileManager timetableFileManager;
        private readonly BodsClient bodsClient;

        public DataProcessingService(ILogger<DataProcessingService> logger, IServiceProvider serviceProvider, TimetableFileManager timetableFileManager, BodsClient bodsClient)
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.timetableFileManager = timetableFileManager;
            this.bodsClient = bodsClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Starting data processing service");

            // TODO: Make configurable immediate calculation system
            await ProcessData(new DateOnly(2025, 03, 01), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait until 4:05AM GMT
                DateTime now = Util.GmtNow;
                DateTime nextProcessDate = new(DateOnly.FromDateTime(now), new TimeOnly(04, 05));
                if (nextProcessDate < now)
                    nextProcessDate = nextProcessDate.AddDays(1);
                TimeSpan timeToWait = nextProcessDate - now;

                logger.LogDebug("Waiting for {waitTime:hh\\:mm\\:ss} time", timeToWait);
                await Task.Delay(timeToWait, stoppingToken);

                await ProcessData(Util.GmtNowDate.AddDays(-1), stoppingToken);
            }
        }

        private async Task ProcessData(DateOnly date, CancellationToken cancellationToken)
        {
            logger.LogInformation("Processing data for {date:dd/MM/yyyy}", date);

            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
            DbController dbController = scope.ServiceProvider.GetRequiredService<DbController>();

            // Process each service and line individually
            foreach (TimetableService timetableService in timetableFileManager.GetAllTimetablesAtDate(date))
            {
                TransXChange txc = await bodsClient.GetTransXChangeFromXmlFile(timetableService.XmlPath, cancellationToken);
                Service service = txc.Services.Service.First(s => s.ServiceCode == timetableService.ServiceCode);
                foreach (Line line in service.Lines)
                {
                    (string journeyCode, string direction)[] journeyCodesTravelled = await dbController.GetJourneyCodesAndDirectionForLineInDay(service.ServiceCode, line.Id, date, cancellationToken);
                    foreach ((string journeyCode, string direction) in journeyCodesTravelled)
                    {
                        // Get the planned vehicle journey
                        (VehicleJourney vj, JourneyPatternStructure jp) = txc.VehicleJourneys.VehicleJourney
                            .Where(vj =>
                                vj.ServiceRef.Value == service.ServiceCode &&
                                vj.LineRef == line.Id &&
                                vj.Operational.TicketMachine.JourneyCode == journeyCode &&
                                vj.OperatingProfile.RunsOnDate(date) && false)
                            .Select(vj => (vj, jp: service.StandardService.JourneyPattern.First(jp => jp.Id == vj.JourneyPatternRef)))
                            .Where(t => (t.vj.Direction != JourneyPatternVehicleDirectionEnumeration.Inherit ? t.vj.Direction.ToString().ToLower() :
                                (t.jp.Direction != JourneyPatternVehicleDirectionEnumeration.Inherit ? t.jp.Direction.ToString().ToLower() :
                                    service.Direction.ToString().ToLower())).Equals(direction, StringComparison.CurrentCultureIgnoreCase))
                            .FirstOrDefault();

                        if (vj == null || jp == null)
                            continue;

                        // Get the route that was taken for this journey
                        TracePoint[] tracePoints = await dbController.GetTracePointsForJourney(service.ServiceCode, line.Id, date, journeyCode, direction, cancellationToken);

                    }
                }
            }
        }
    }
}
