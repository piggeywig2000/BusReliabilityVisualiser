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
            await PrintRouteGPX("FBRI-BH_iAkTyiv_oVBjns3.zip");
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

            // Find journey pattern section
            foreach ((int indexJptl, JourneyPatternTimingLink jptl) in line.Services.Service[0].StandardService.JourneyPattern.First(jp => jp.Id == vehicleJourney.JourneyPatternRef).JourneyPatternSectionRefs
                .SelectMany(jpsRef => line.JourneyPatternSections.JourneyPatternSection.First(jps => jps.Id == jpsRef.Value).JourneyPatternTimingLink)
                .Index())
            {
                // Find related vehicle journey timing link to get timings
                VehicleJourneyTimingLink vjtl = vehicleJourney.VehicleJourneyTimingLink.First(vjtl => vjtl.JourneyPatternTimingLinkRef.Value == jptl.Id);

                if (vjtl.From.WaitTimeSpecified)
                    startTime += vjtl.From.WaitTime;
                DateTime endTime = startTime + vjtl.RunTime;

                RouteLink rl = routeLinks[jptl.RouteLinkRef.Value];
                for (int i = 0; i < rl.Track[0].Mapping.Count; i++)
                {
                    if (indexJptl > 0 && i == 0) // Skip first point in each section as it'll be identical to previous
                        continue;

                    DateTime trackTime = startTime + (((double)i / (double)rl.Track[0].Mapping.Count) * (endTime - startTime));

                    LocationStructure locTo = rl.Track[0].Mapping[i];
                    int bearing = 0;
                    if (i > 0)
                    {
                        LocationStructure locFrom = rl.Track[0].Mapping[i - 1];
                        // Source: https://www.movable-type.co.uk/scripts/latlong.html
                        double y = Math.Sin((double)locTo.Longitude - (double)locFrom.Longitude) * Math.Cos((double)locTo.Latitude);
                        double x = Math.Cos((double)locFrom.Latitude) * Math.Sin((double)locTo.Latitude) -
                                   Math.Sin((double)locFrom.Latitude) * Math.Cos((double)locTo.Latitude) * Math.Cos((double)locTo.Longitude - (double)locFrom.Longitude);
                        double theta = Math.Atan2(y, x);
                        bearing = (int)(theta * 180 / Math.PI + 360) % 360;
                    }

                    Console.WriteLine($"<trkpt lat=\"{locTo.Latitude}\" lon=\"{locTo.Longitude}\"><ele>{bearing}</ele><time>{trackTime.ToString("o", CultureInfo.InvariantCulture)}</time></trkpt>");
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
