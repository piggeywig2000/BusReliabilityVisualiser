using BodsDotNet;
using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityScraper
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //await FetchVehiclesOnLine();
            await LogPositions();
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
                    va = location.ServiceDelivery.VehicleMonitoringDelivery[0].VehicleActivity.FirstOrDefault(va => va.MonitoredVehicleJourney.VehicleRef.Value == "FBRI-33948");
                }

                if (va != null && va.RecordedAtTime != lastRecordedAt)
                {
                    lastRecordedAt = va.RecordedAtTime;

                    Console.WriteLine($"{lastRecordedAt},{va.MonitoredVehicleJourney.VehicleLocation.Longitude},{va.MonitoredVehicleJourney.VehicleLocation.Latitude}");
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
