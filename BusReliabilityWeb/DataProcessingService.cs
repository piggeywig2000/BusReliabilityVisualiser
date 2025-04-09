using System.Globalization;
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
        private readonly IConfiguration configuration;
        private readonly IServiceProvider serviceProvider;
        private readonly TimetableFileManager timetableFileManager;
        private readonly BodsClient bodsClient;

        public DataProcessingService(ILogger<DataProcessingService> logger, IConfiguration configuration, IServiceProvider serviceProvider, TimetableFileManager timetableFileManager, BodsClient bodsClient)
        {
            this.logger = logger;
            this.configuration = configuration;
            this.serviceProvider = serviceProvider;
            this.timetableFileManager = timetableFileManager;
            this.bodsClient = bodsClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Starting data processing service");

            // Process immediate dates
            foreach (string dateStr in configuration.GetSection("LatenessCalc:Immediate").Get<string[]>() ?? [])
            {
                if (DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsedDate))
                {
                    await ProcessData(parsedDate, stoppingToken);
                }
            }

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

                if (configuration.GetValue("DoDataProcessing", true))
                    await ProcessData(Util.GmtNowDate.AddDays(-1), stoppingToken);

                // Process deferred dates
                foreach (string dateStr in configuration.GetSection("LatenessCalc:Deferred").Get<string[]>() ?? [])
                {
                    if (DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsedDate))
                    {
                        await ProcessData(parsedDate, stoppingToken);
                    }
                }
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

                foreach (TimetableLine line in timetableService.Lines)
                {
                    // Get actual arrival times
                    Dictionary<string, List<DateTime>> actualStopTimes = await GetActualStopDepartureTimesForLine(txc, dbController, timetableService.ServiceCode, line.LineId, date, cancellationToken);

                    // Get planned arrival times
                    Dictionary<string, List<DateTime>> plannedStopTimes = GetPlannedDepartureTimesForLine(txc, timetableService.ServiceCode, line.LineId, date);

                    // Calculate and save lateness values
                    await CalculateLatenessValues(plannedStopTimes, actualStopTimes, dbController, timetableService.ServiceCode, line.LineId, date, cancellationToken);
                }
            }

            logger.LogInformation("Processed data for {date:dd/MM/yyyy}", date);
        }

        private Dictionary<string, List<DateTime>> GetPlannedDepartureTimesForLine(TransXChange txc, string serviceCode, string lineId, DateOnly date)
        {
            Service service = txc.Services.Service.First(s => s.ServiceCode == serviceCode);
            Line line = service.Lines.First(l => l.Id == lineId);
            Util.TransXChangeDicts txcDicts = new(txc);

            Dictionary<string, List<DateTime>> stopToDepartureTimes = [];

            foreach (VehicleJourney vehicleJourney in txc.VehicleJourneys.VehicleJourney
                .Where(vj => vj.ServiceRef.Value == service.ServiceCode &&
                    vj.LineRef == line.Id &&
                    vj.OperatingProfile.RunsOnDate(date)))
            {
                JourneyPatternStructure journeyPattern = service.StandardService.JourneyPattern.First(jp => jp.Id == vehicleJourney.JourneyPatternRef);
                Map.Route plannedRoute = GetPlannedRouteFromTransXChange(vehicleJourney, journeyPattern, txcDicts, date);

                // Save stop departure times
                foreach (Map.Stop stop in plannedRoute.Stops)
                {
                    if (!stopToDepartureTimes.TryGetValue(stop.Naptan, out List<DateTime>? departureTimes))
                    {
                        departureTimes = [];
                        stopToDepartureTimes.Add(stop.Naptan, departureTimes);
                    }
                    departureTimes.Add(stop.DepartureTime);
                    departureTimes.Sort();
                }
            }

            return stopToDepartureTimes;
        }

        private async Task<Dictionary<string, List<DateTime>>> GetActualStopDepartureTimesForLine(TransXChange txc, DbController dbController, string serviceCode, string lineId, DateOnly date, CancellationToken cancellationToken)
        {
            int stopBufferDistance = configuration.GetValue<int>("StopBufferDistance");
            Service service = txc.Services.Service.First(s => s.ServiceCode == serviceCode);
            Line line = service.Lines.First(l => l.Id == lineId);
            Util.TransXChangeDicts txcDicts = new(txc);

            Dictionary<string, List<DateTime>> stopToDepartureTimes = [];

            (string journeyCode, string direction)[] journeyCodesTravelled = await dbController.GetJourneyCodesAndDirectionForLineInDay(service.ServiceCode, line.Id, date, cancellationToken);
            foreach ((string journeyCode, string direction) in journeyCodesTravelled)
            {
                // Get the planned vehicle journey
                (VehicleJourney vehicleJourney, JourneyPatternStructure journeyPattern) = txc.VehicleJourneys.VehicleJourney
                    .Where(vj =>
                        vj.ServiceRef.Value == service.ServiceCode &&
                        vj.LineRef == line.Id &&
                        vj.Operational.TicketMachine.JourneyCode == journeyCode &&
                        vj.OperatingProfile.RunsOnDate(date))
                    .Select(vj => (vj, jp: service.StandardService.JourneyPattern.First(jp => jp.Id == vj.JourneyPatternRef)))
                    .Where(t => (t.vj.Direction != JourneyPatternVehicleDirectionEnumeration.Inherit ? t.vj.Direction.ToString().ToLower() :
                        (t.jp.Direction != JourneyPatternVehicleDirectionEnumeration.Inherit ? t.jp.Direction.ToString().ToLower() :
                            service.Direction.ToString().ToLower())).Equals(direction, StringComparison.CurrentCultureIgnoreCase))
                    .FirstOrDefault();

                if (vehicleJourney == null || journeyPattern == null)
                    continue;

                Map.Route plannedRoute = GetPlannedRouteFromTransXChange(vehicleJourney, journeyPattern, txcDicts, date);
                plannedRoute.CalculateBearings();

                // Get the actual vehicle journey
                TracePoint[] tracePoints = await dbController.GetTracePointsForJourney(service.ServiceCode, line.Id, date, journeyCode, direction, cancellationToken);
                InterpolateNullBearings(tracePoints);

                Map.Route actualRoute = new();
                foreach (TracePoint tracePoint in tracePoints)
                {
                    Map.RoutePoint pnt = new(tracePoint.Easting, tracePoint.Northing, tracePoint.Bearing!.Value)
                    {
                        Time = tracePoint.RecordedAt
                    };
                    actualRoute.AppendPoint(pnt);
                }
                foreach (Map.Stop stop in plannedRoute.Stops)
                    actualRoute.InsertBusStop(stop.Naptan, stop.Name, stop.RouteDistance, stop.Point, stop.DepartureTime);

                // Match to the planned vehicle journey
                foreach (Map.RoutePoint actualPoint in actualRoute.Points)
                {
                    actualPoint.MatchToRoute(plannedRoute);
                }
                actualRoute.ResolveOrder(plannedRoute);
                actualRoute.SetStopTimeFromPoints(stopBufferDistance);

                // Save stop departure times
                foreach (Map.Stop stop in actualRoute.Stops)
                {
                    if (!stopToDepartureTimes.TryGetValue(stop.Naptan, out List<DateTime>? departureTimes))
                    {
                        departureTimes = [];
                        stopToDepartureTimes.Add(stop.Naptan, departureTimes);
                    }
                    departureTimes.Add(stop.DepartureTime);
                    departureTimes.Sort();
                }
            }

            return stopToDepartureTimes;
        }

        private Map.Route GetPlannedRouteFromTransXChange(VehicleJourney vehicleJourney, JourneyPatternStructure journeyPattern, Util.TransXChangeDicts txcDicts, DateOnly date)
        {
            return Map.Route.FromTransXChange(vehicleJourney, journeyPattern, txcDicts, date);
        }

        private static void InterpolateNullBearings(TracePoint[] tracePoints)
        {
            double? startVal = null;
            int consecutiveBlanks = 0;
            for (int i = 0; i <= tracePoints.Length; i++)
            {
                bool isEndOfList = i == tracePoints.Length;
                double? bearing = isEndOfList ? null : tracePoints[i].Bearing;
                if (bearing.HasValue && consecutiveBlanks == 0) // Set start val
                    startVal = bearing;
                if (!bearing.HasValue && !isEndOfList) // Incremement consecutive blanks
                    consecutiveBlanks++;

                if (consecutiveBlanks > 0 && (bearing.HasValue || isEndOfList)) // Hit end of blanks, traverse back
                {
                    double? endVal = bearing;
                    for (int j = 0; j < consecutiveBlanks; j++)
                    {
                        int i2 = i - consecutiveBlanks + j;
                        double scaleFactor = (j + 1.0) / (consecutiveBlanks + 1.0);
                        if (startVal == null)
                            tracePoints[i2].Bearing = endVal.GetValueOrDefault();
                        else if (endVal == null)
                            tracePoints[i2].Bearing = startVal.GetValueOrDefault();
                        else
                        {
                            bool doesWrapAround = Math.Abs(endVal.Value - startVal.Value) > 180;
                            if (doesWrapAround)
                            {
                                // We're wrapping around 0, bump up the one to the right of 0 to make it interpolate over 0
                                if (startVal.Value < endVal.Value)
                                    startVal += 360;
                                else
                                    endVal += 360;
                            }
                            tracePoints[i2].Bearing = (startVal.Value + ((endVal.Value - startVal.Value) * scaleFactor)) % 360;
                        }
                    }
                    startVal = bearing;
                    consecutiveBlanks = 0;
                }
            }
        }

        private async Task CalculateLatenessValues(Dictionary<string, List<DateTime>> plannedStopTimes, Dictionary<string, List<DateTime>> actualStopTimes, DbController dbController, string serviceCode, string lineId, DateOnly date, CancellationToken cancellationToken)
        {
            List<LatenessValue> lvsToAdd = [];

            // Process each bus stop
            foreach ((string naptan, List<DateTime> plannedTimes) in plannedStopTimes)
            {
                // Get the actual stop times
                if (!actualStopTimes.TryGetValue(naptan, out List<DateTime>? actualTimes))
                    actualTimes = [];

                // Calculate the lateness values for each hour
                int wrappedDays = 0;
                for (TimeOnly hour = new(05, 00); hour != new TimeOnly(04, 00); hour = hour.AddHours(1, out int newWrappedDays), wrappedDays = Math.Max(wrappedDays, newWrappedDays))
                {
                    DateTime from = new(date.AddDays(wrappedDays), hour);
                    double? lateness;
                    TimeSpan? plannedWaitTime = CalculateAverageWaitTime(plannedTimes, from, from.AddHours(1));
                    if (!plannedWaitTime.HasValue)
                    {
                        // No planned buses, so no lateness
                        lateness = null;
                    }
                    else
                    {
                        TimeSpan? actualWaitTime = CalculateAverageWaitTime(actualTimes, from, from.AddHours(1));
                        if (!actualWaitTime.HasValue)
                        {
                            lateness = null; // No actual buses, so no lateness (I think this is fine?)

                            //// No actual buses, assume actual wait time is time until 5am
                            //TimeSpan timeUntil5am = new TimeOnly(05, 00).ToTimeSpan() - hour.ToTimeSpan();
                            //if (timeUntil5am <= TimeSpan.Zero)
                            //    timeUntil5am += TimeSpan.FromDays(1);
                            //actualWaitTime = (timeUntil5am / 2) * timeUntil5am.TotalMinutes;
                        }
                        else
                        {
                            lateness = (actualWaitTime.Value - plannedWaitTime.Value).TotalMinutes;
                        }
                    }

                    lvsToAdd.Add(new(serviceCode, lineId, naptan, date, hour.Hour + (24 * wrappedDays), lateness));
                }
            }

            if (lvsToAdd.Count > 0)
                await dbController.AddLatenessValues(lvsToAdd, cancellationToken);
        }

        private TimeSpan? CalculateAverageWaitTime(List<DateTime> stopTimes, DateTime rangeStart, DateTime rangeEnd)
        {
            int? maxWaitTimeMinutes = configuration.GetValue<int?>("MaxWaitTime", null);
            TimeSpan? maxWaitTime = maxWaitTimeMinutes.HasValue ? TimeSpan.FromMinutes(maxWaitTimeMinutes.Value) : null;
            List<TimeSpan> weightedWaitTimes = [];
            while (rangeStart < rangeEnd)
            {
                DateTime nextBus = stopTimes.Find(st => st > rangeStart);
                if (nextBus == default)
                {
                    // No next bus. Don't add anything else to list
                    break;
                }

                // Handle max wait time
                bool isNextBusInRange = nextBus <= rangeEnd;
                if (maxWaitTime != null && !isNextBusInRange && nextBus - rangeEnd > maxWaitTime)
                {
                    // Next bus is out of range, and later than the max wait time
                    break;
                }
                if (maxWaitTime != null && nextBus - maxWaitTime.Value > rangeStart)
                {
                    // Recalculate start to account for max wait time
                    rangeStart = nextBus - maxWaitTime.Value;
                }

                // Calculate average wait time
                TimeSpan rangeDiff = rangeEnd - rangeStart;
                TimeSpan headway = nextBus - rangeStart; // Difference from last bus to this bus
                TimeSpan averageWaitTime = isNextBusInRange ? headway / 2 : ((headway - rangeDiff) + headway) / 2; // Average time each person spends waiting for this next bus
                TimeSpan weight = isNextBusInRange ? headway : rangeDiff; // This takes into account for the fact that more people are affected when the gap is bigger
                weightedWaitTimes.Add(averageWaitTime * weight.TotalMinutes);

                rangeStart = nextBus;
            }

            return weightedWaitTimes.Count == 0 ? null : TimeSpan.FromSeconds(weightedWaitTimes.Average(ts => ts.TotalSeconds));
        }
    }
}
