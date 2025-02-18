using System.Globalization;
using BodsDotNet;
using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityScraper
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //await FetchVehiclesOnLine();
            //await LogPositions();
            //await PrintRouteGPX("FBRI-BH_iAkTyiv_oVBjns3.zip");
            await MatchRoute("u1sample3.csv", "FBRI-BH_iAkTyiv_oVBjns3.zip", "../../../../..");
        }

        static async Task MatchRoute(string csvPath, string transXChangePath, string outputPath)
        {
            const string API_KEY = "f3eb2d8601b48191874b770a833b29fc0238e1da";
            const string BUS_LINE = "U1";
            DateOnly DEPARTURE_DATE = new DateOnly(2025, 01, 31);
            TimeSpan DEPARTURE = new TimeSpan(15, 33, 0);
            double BUFFER_DISTANCE = 50;
            BodsClient bodsClient = new(API_KEY);

            IReadOnlyCollection<TransXChange> txcs = await bodsClient.GetTransXChangeFromFile(transXChangePath);
            TransXChange line = txcs
                .Where(txc => txc.Services.Service.Any(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)))
                .OrderByDescending(txc => txc.Services.Service.First(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)).OperatingPeriod.StartDate)
                .First();

            Console.WriteLine($"Found line {BUS_LINE}");

            VehicleJourney vehicleJourney = line.VehicleJourneys.VehicleJourney
                .First(vj => vj.OperatingProfile.RegularDayType.DaysOfWeek.FridaySpecified && vj.DepartureTime.TimeOfDay == DEPARTURE);

            Dictionary<string, AnnotatedStopPointRef> stopPoints = line.StopPoints.AnnotatedStopPointRef.ToDictionary(sp => sp.StopPointRef);
            Dictionary<string, RouteLink> routeLinks = line.RouteSections.RouteSection.SelectMany(rs => rs.RouteLink).ToDictionary(rl => rl.Id);
            Dictionary<string, JourneyPatternSection> journeyPatternSections = line.JourneyPatternSections.JourneyPatternSection.ToDictionary(jps => jps.Id);
            Dictionary<string, VehicleJourneyTimingLink> jptlToVjtl = vehicleJourney.VehicleJourneyTimingLink.ToDictionary(vjtl => vjtl.JourneyPatternTimingLinkRef.Value);

            // Read planned route
            Map.Route plannedRoute = new();
            DateTime departureTime = DEPARTURE_DATE.ToDateTime(TimeOnly.FromTimeSpan(vehicleJourney.DepartureTime.TimeOfDay));
            foreach (JourneyPatternTimingLink jptl in line.Services.Service[0].StandardService.JourneyPattern.First(jp => jp.Id == vehicleJourney.JourneyPatternRef).JourneyPatternSectionRefs
                .SelectMany(jpsRef => journeyPatternSections[jpsRef.Value].JourneyPatternTimingLink))
            {
                RouteLink rl = routeLinks[jptl.RouteLinkRef.Value];
                VehicleJourneyTimingLink vjtl = jptlToVjtl[jptl.Id];

                if (!rl.TrackSpecified)
                    throw new NotImplementedException();

                if (vjtl.From.WaitTimeSpecified)
                    departureTime += vjtl.From.WaitTime;

                // Create or amend From stop
                if (plannedRoute.StopCount == 0 || plannedRoute.Stops[^1].Ref != jptl.From.StopPointRef.Value)
                    plannedRoute.AppendBusStop(jptl.From.StopPointRef.Value, stopPoints[jptl.From.StopPointRef.Value].CommonName.Value, departureTime);
                else
                    plannedRoute.Stops[^1].DepartureTime = departureTime;

                // Insert track points
                foreach (LocationStructure loc in rl.Track.SelectMany(t => t.Mapping))
                {
                    Map.RoutePoint pnt = Map.RoutePoint.FromTransXChange(loc);
                    if (plannedRoute.PointCount > 0 && pnt == plannedRoute.Points[^1])
                        continue; // Same position as last
                    plannedRoute.AppendPoint(pnt);
                }

                departureTime += vjtl.RunTime;
                if (vjtl.To.WaitTimeSpecified)
                    departureTime += vjtl.To.WaitTime;

                // Create To stop
                plannedRoute.AppendBusStop(jptl.To.StopPointRef.Value, stopPoints[jptl.To.StopPointRef.Value].CommonName.Value, departureTime);
            }
            plannedRoute.CalculateBearings();

            Console.WriteLine("Created planned route");

            // Read actual route
            Map.Route actualRoute = new();
            using (StreamReader sr = new(csvPath, System.Text.Encoding.UTF8, true, new FileStreamOptions() { Access = FileAccess.Read, Mode = FileMode.Open }))
            {
                string? csvLine = await sr.ReadLineAsync();
                while (!string.IsNullOrEmpty(csvLine))
                {
                    string[] csvParts = csvLine.Split(',');
                    Map.RoutePoint point = Map.RoutePoint.FromWGS84(double.Parse(csvParts[1]), double.Parse(csvParts[2]), double.Parse(csvParts[3]));
                    point.Time = DateTime.Parse(csvParts[0]);
                    actualRoute.AppendPoint(point);
                    csvLine = await sr.ReadLineAsync();
                }
            }
            foreach (Map.Stop stop in plannedRoute.Stops)
                actualRoute.InsertBusStop(stop.Ref, stop.Name, stop.RouteDistance, stop.Point, stop.DepartureTime);

            Console.WriteLine("Read actual route");

            // Interpolate 0 values between other points
            double? startVal = null;
            int consecutiveBlanks = 0;
            for (int i = 0; i <= actualRoute.Points.Count; i++)
            {
                double? bearing = i < actualRoute.Points.Count ? actualRoute.Points[i].Bearing : null;
                bool isBlank = bearing == 0;
                if (!isBlank && consecutiveBlanks == 0) // Set start val
                    startVal = bearing;
                if (isBlank) // Incremement consecutive blanks
                    consecutiveBlanks++;

                if (consecutiveBlanks > 0 && !isBlank) // Hit end of blanks, traverse back
                {
                    double? endVal = bearing;
                    for (int j = 0; j < consecutiveBlanks; j++)
                    {
                        int i2 = i - consecutiveBlanks + j;
                        double scaleFactor = (j + 1.0) / (consecutiveBlanks + 1.0);
                        if (startVal == null)
                            actualRoute.Points[i2].Bearing = endVal.GetValueOrDefault();
                        else if (endVal == null)
                            actualRoute.Points[i2].Bearing = startVal.GetValueOrDefault();
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
                            actualRoute.Points[i2].Bearing = (startVal.Value + ((endVal.Value - startVal.Value) * scaleFactor)) % 360;
                        }
                    }
                    startVal = bearing;
                    consecutiveBlanks = 0;
                }
            }

            Console.WriteLine("Interpolated bearings in actual route");

            // Match actual route to planned route
            foreach (Map.RoutePoint actualPoint in actualRoute.Points)
            {
                actualPoint.MatchToRoute(plannedRoute);
            }
            Console.WriteLine("Matched actual route to planned route");

            actualRoute.ResolveOrder(plannedRoute);
            Console.WriteLine("Resolved order");

            actualRoute.SetStopTimeFromPoints(BUFFER_DISTANCE);
            Console.WriteLine("Calculated actual stop time");

            await File.WriteAllTextAsync($"{outputPath}/U1 Planned 3.gpx", plannedRoute.GetStopsGPX("U1 Planned 3", true));
            await File.WriteAllTextAsync($"{outputPath}/U1 Actual 3.gpx", actualRoute.GetStopsGPX("U1 Actual 3", true));
            await File.WriteAllTextAsync($"{outputPath}/U1 Route 3.gpx", actualRoute.GetPointsGPX("U1 Route 3", true));
            Console.WriteLine($"Wrote to {outputPath}");
        }

        static async Task PrintRouteGPX(string transXChangePath)
        {
            const string API_KEY = "f3eb2d8601b48191874b770a833b29fc0238e1da";
            const string BUS_LINE = "U1";
            TimeSpan DEPARTURE = new TimeSpan(15, 33, 0);
            BodsClient bodsClient = new(API_KEY);

            IReadOnlyCollection<TransXChange> txcs = await bodsClient.GetTransXChangeFromFile(transXChangePath);
            TransXChange line = txcs
                .Where(txc => txc.Services.Service.Any(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)))
                .OrderByDescending(txc => txc.Services.Service.First(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)).OperatingPeriod.StartDate)
                .First();

            //Console.WriteLine($"Found line {BUS_LINE}");

            // Find GPS trace
            Dictionary<string, RouteLink> routeLinks = [];
            foreach (RouteLink rl in line.RouteSections.RouteSection.SelectMany(rs => rs.RouteLink))
            {
                routeLinks.Add(rl.Id, rl);
            }

            VehicleJourney vehicleJourney = line.VehicleJourneys.VehicleJourney
                .First(vj => vj.OperatingProfile.RegularDayType.DaysOfWeek.FridaySpecified && vj.DepartureTime.TimeOfDay == DEPARTURE);

            DateTime startTime = new DateOnly(2025, 1, 31).ToDateTime(TimeOnly.FromTimeSpan(vehicleJourney.DepartureTime.TimeOfDay));
            LocationStructure locFrom = new()
            {
                LongitudeSpecified = false,
                LatitudeSpecified = false
            };

            // Find journey pattern section
            foreach (JourneyPatternTimingLink jptl in line.Services.Service[0].StandardService.JourneyPattern.First(jp => jp.Id == vehicleJourney.JourneyPatternRef).JourneyPatternSectionRefs
                .SelectMany(jpsRef => line.JourneyPatternSections.JourneyPatternSection.First(jps => jps.Id == jpsRef.Value).JourneyPatternTimingLink))
            {
                // Find related vehicle journey timing link to get timings
                VehicleJourneyTimingLink vjtl = vehicleJourney.VehicleJourneyTimingLink.First(vjtl => vjtl.JourneyPatternTimingLinkRef.Value == jptl.Id);

                if (vjtl.From.WaitTimeSpecified)
                    startTime += vjtl.From.WaitTime;
                DateTime endTime = startTime + vjtl.RunTime;

                RouteLink rl = routeLinks[jptl.RouteLinkRef.Value];
                for (int i = 0; i < rl.Track[0].Mapping.Count; i++)
                {
                    // Skip if position identical to last
                    LocationStructure locTo = rl.Track[0].Mapping[i];

                    if (locFrom.LongitudeSpecified && locFrom.LatitudeSpecified && (double)locFrom.Longitude == (double)locTo.Longitude && (double)locFrom.Latitude == (double)locTo.Latitude)
                        continue;

                    DateTime trackTime = startTime + (((double)i / (double)rl.Track[0].Mapping.Count) * (endTime - startTime));

                    int bearing = 0;
                    if (locFrom.LongitudeSpecified && locFrom.LatitudeSpecified) // Don't calculate if first point
                    {
                        // Source: https://www.movable-type.co.uk/scripts/latlong.html
                        double y = Math.Sin((double)locTo.Longitude - (double)locFrom.Longitude) * Math.Cos((double)locTo.Latitude);
                        double x = Math.Cos((double)locFrom.Latitude) * Math.Sin((double)locTo.Latitude) -
                                   Math.Sin((double)locFrom.Latitude) * Math.Cos((double)locTo.Latitude) * Math.Cos((double)locTo.Longitude - (double)locFrom.Longitude);
                        double theta = Math.Atan2(y, x);
                        bearing = (int)(theta * 180 / Math.PI + 360) % 360;
                    }

                    Console.WriteLine($"<trkpt lat=\"{locTo.Latitude}\" lon=\"{locTo.Longitude}\"><ele>{bearing}</ele><time>{trackTime.ToString("o", CultureInfo.InvariantCulture)}</time></trkpt>");
                    locFrom = locTo;
                }

                if (vjtl.To.WaitTimeSpecified)
                    endTime += vjtl.To.WaitTime;
                startTime = endTime;
            }
        }

        static async Task LogPositions()
        {
            const string API_KEY = "f3eb2d8601b48191874b770a833b29fc0238e1da";
            const string BUS_LINE = "U1";

            BodsClient bodsClient = new(API_KEY);
            BodsDotNet.Schemas.Timetable.Timetable timetable = await bodsClient.GetTimetableById(5813);
            Console.WriteLine(timetable.URL);

            IReadOnlyCollection<TransXChange> txcs = await bodsClient.GetTransXChangeFromUrl(timetable.URL);
            TransXChange line = txcs
                .Where(txc => txc.Services.Service.Any(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)))
                .OrderByDescending(txc => txc.Services.Service.First(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)).OperatingPeriod.StartDate)
                .First();

            Console.WriteLine($"Found line {BUS_LINE}");

            string[] blocks = line.VehicleJourneys.VehicleJourneySpecified ? line.VehicleJourneys.VehicleJourney
                .Select(vj => vj.Operational.Block.BlockNumber).Distinct().ToArray() : [];

            DateTime lastRecordedAt = DateTime.MinValue;
            while (true)
            {
                BodsDotNet.Schemas.Siri.Siri location = await bodsClient.GetLocation(["FBRI"], BUS_LINE, blocks);

                BodsDotNet.Schemas.Siri.VehicleActivityStructure? va = null;
                if (location.ServiceDelivery.VehicleMonitoringDeliverySpecified && location.ServiceDelivery.VehicleMonitoringDelivery[0].VehicleActivitySpecified)
                {
                    va = location.ServiceDelivery.VehicleMonitoringDelivery[0].VehicleActivity.FirstOrDefault(va => va.MonitoredVehicleJourney.VehicleRef.Value == "FBRI-33949");
                }

                if (va != null && va.RecordedAtTime != lastRecordedAt)
                {
                    lastRecordedAt = va.RecordedAtTime;

                    Console.WriteLine($"{lastRecordedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)},{va.MonitoredVehicleJourney.VehicleLocation.Longitude},{va.MonitoredVehicleJourney.VehicleLocation.Latitude},{va.MonitoredVehicleJourney.Bearing}");
                }

                await Task.Delay(5000);
            }
        }

        static async Task FetchVehiclesOnLine()
        {
            const string API_KEY = "f3eb2d8601b48191874b770a833b29fc0238e1da";
            //const double BOUNDING_BOX_MARGIN = 0.01;
            const string BUS_LINE = "U1";

            BodsClient bodsClient = new(API_KEY);
            BodsDotNet.Schemas.Timetable.Timetable timetable = await bodsClient.GetTimetableById(5813);
            Console.WriteLine(timetable.URL);

            IReadOnlyCollection<TransXChange> txcs = await bodsClient.GetTransXChangeFromUrl(timetable.URL);
            TransXChange line = txcs
                .Where(txc => txc.Services.Service.Any(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)))
                .OrderByDescending(txc => txc.Services.Service.First(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)).OperatingPeriod.StartDate)
                .First();

            Console.WriteLine($"Found line {BUS_LINE}");

            //double minLongitude = double.MaxValue, minLatitude = double.MaxValue;
            //double maxLongitude = double.MinValue, maxLatitude = double.MinValue;
            //foreach (LocationStructure loc in line.RouteSections.RouteSection
            //    .SelectMany(rs => rs.RouteLink
            //        .SelectMany(rl => rl.TrackSpecified ? rl.Track
            //            .SelectMany(t => t.Mapping) : [])))
            //{
            //    if (!loc.LongitudeSpecified || !loc.LatitudeSpecified)
            //        throw new InvalidOperationException("Longitude or latitude not specified");

            //    minLongitude = Math.Min(minLongitude, (double)loc.Longitude);
            //    minLatitude = Math.Min(minLatitude, (double)loc.Latitude);
            //    maxLongitude = Math.Max(maxLongitude, (double)loc.Longitude);
            //    maxLatitude = Math.Max(maxLatitude, (double)loc.Latitude);
            //}
            //minLongitude -= BOUNDING_BOX_MARGIN;
            //minLatitude -= BOUNDING_BOX_MARGIN;
            //maxLongitude += BOUNDING_BOX_MARGIN;
            //maxLatitude += BOUNDING_BOX_MARGIN;
            //Console.WriteLine($"Bounding box: {minLongitude}, {minLatitude}, {maxLongitude}, {maxLatitude}");

            string[] blocks = line.VehicleJourneys.VehicleJourneySpecified ? line.VehicleJourneys.VehicleJourney
                .Select(vj => vj.Operational.Block.BlockNumber).Distinct().ToArray() : [];

            BodsDotNet.Schemas.Siri.Siri location = await bodsClient.GetLocation(["FBRI"], BUS_LINE, blocks);
            int numBus = location.ServiceDelivery.VehicleMonitoringDeliverySpecified ?
                location.ServiceDelivery.VehicleMonitoringDelivery
                    .Sum(vm => vm.VehicleActivitySpecified ? vm.VehicleActivity
                        .Count(va => DateTime.Now - va.RecordedAtTime < TimeSpan.FromMinutes(15)) : 0) : 0;
            Console.WriteLine($"Found {numBus} buses on line {BUS_LINE}");
            Console.WriteLine(string.Join('\n', location.ServiceDelivery.VehicleMonitoringDeliverySpecified ? location.ServiceDelivery.VehicleMonitoringDelivery
                .SelectMany(vmd => vmd.VehicleActivitySpecified ? vmd.VehicleActivity
                    .Select(va => $"{va.MonitoredVehicleJourney.VehicleRef.Value}: {va.MonitoredVehicleJourney.VehicleLocation.Latitude:F5}, {va.MonitoredVehicleJourney.VehicleLocation.Longitude:F5}") : []) : []));
        }
    }
}
