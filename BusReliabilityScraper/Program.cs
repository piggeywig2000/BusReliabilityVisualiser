using BodsDotNet;
using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityScraper
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            const string API_KEY = "f3eb2d8601b48191874b770a833b29fc0238e1da";
            const double BOUNDING_BOX_MARGIN = 0.01;
            const string BUS_LINE = "U1";

            BodsClient bodsClient = new(API_KEY);
            BodsDotNet.Schemas.Timetable.Timetable timetable = await bodsClient.GetTimetableById(5813);
            Console.WriteLine(timetable.URL);

            IReadOnlyCollection<TransXChange> txcs = await bodsClient.GetTransXChangeFromUrl(timetable.URL);
            TransXChange line1 = txcs
                .Where(txc => txc.Services.Service.Any(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)))
                .OrderByDescending(txc => txc.Services.Service.First(s => s.Lines.Any(l => l.LineName.Value == BUS_LINE)).OperatingPeriod.StartDate)
                .First();

            Console.WriteLine($"Found line {BUS_LINE}");

            double minLongitude = double.MaxValue, minLatitude = double.MaxValue;
            double maxLongitude = double.MinValue, maxLatitude = double.MinValue;
            foreach (LocationStructure loc in line1.RouteSections.RouteSection
                .SelectMany(rs => rs.RouteLink
                    .SelectMany(rl => rl.TrackSpecified ? rl.Track
                        .SelectMany(t => t.Mapping) : [])))
            {
                if (!loc.LongitudeSpecified || !loc.LatitudeSpecified)
                    throw new InvalidOperationException("Longitude or latitude not specified");

                minLongitude = Math.Min(minLongitude, (double)loc.Longitude);
                minLatitude = Math.Min(minLatitude, (double)loc.Latitude);
                maxLongitude = Math.Max(maxLongitude, (double)loc.Longitude);
                maxLatitude = Math.Max(maxLatitude, (double)loc.Latitude);
            }
            minLongitude -= BOUNDING_BOX_MARGIN;
            minLatitude -= BOUNDING_BOX_MARGIN;
            maxLongitude += BOUNDING_BOX_MARGIN;
            maxLatitude += BOUNDING_BOX_MARGIN;
            Console.WriteLine($"Bounding box: {minLongitude}, {minLatitude}, {maxLongitude}, {maxLatitude}");

            BodsDotNet.Schemas.Siri.Siri location = await bodsClient.GetLocation(minLongitude, minLatitude, maxLongitude, maxLatitude, ["FBRI"], BUS_LINE);
            int numBus = location.ServiceDelivery.VehicleMonitoringDeliverySpecified ?
                location.ServiceDelivery.VehicleMonitoringDelivery
                    .Sum(vm => vm.VehicleActivitySpecified ? vm.VehicleActivity
                        .Count(va => DateTime.Now - va.RecordedAtTime < TimeSpan.FromMinutes(15)) : 0)
                : 0;
            Console.WriteLine($"Found {numBus} buses on line {BUS_LINE}");
        }
    }
}
