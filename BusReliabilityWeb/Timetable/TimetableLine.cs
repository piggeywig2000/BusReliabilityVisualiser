using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public class TimetableLine
    {
        private readonly string[] operatorNocs;
        private readonly string[] ticketMachineServiceCodes;
        private readonly TimetableBusStop[] busStops;
        private readonly TimetableLineSection[] lineSections;

        public TimetableLine(TransXChange txc, string serviceCode, string lineId)
        {
            Service service = txc.Services.Service.First(s => s.ServiceCode == serviceCode);
            Line line = service.Lines.First(l => l.Id == lineId);

            Util.TransXChangeDicts txcDicts = new(txc);

            // If no ticket machine service code, just throw an exception. We can't match it to location data
            if (service.TicketMachineServiceCode == null && txc.VehicleJourneys.VehicleJourney
                .Where(vj => vj.ServiceRef.Value == service.ServiceCode && vj.LineRef == line.Id)
                .Any(vj => string.IsNullOrEmpty(vj.Operational.TicketMachine.TicketMachineServiceCode)))
                throw new NotImplementedException("Ticket machine service code is empty");

            ServiceCode = service.ServiceCode;
            LineId = line.Id;
            LineName = line.LineName.Value;

            // Get all national operator codes used by this line
            operatorNocs = txc.VehicleJourneys.VehicleJourney
                .Where(vj => vj.ServiceRef.Value == service.ServiceCode && vj.LineRef == line.Id)
                .Select(vj => txc.Operators.Operator.First(o => o.Id == vj.OperatorRef.Value).NationalOperatorCode)
                .Distinct()
                .ToArray();

            // Get the ticket machine service codes
            ticketMachineServiceCodes = txc.VehicleJourneys.VehicleJourney
                .Where(vj => vj.ServiceRef.Value == service.ServiceCode && vj.LineRef == line.Id)
                .Select(vj => vj.Operational.TicketMachine.TicketMachineServiceCode ?? service.TicketMachineServiceCode)
                .Distinct()
                .OfType<string>() // Where not null. Makes compiler happy :)
                .ToArray();

            // Get all bus stops used by this line
            busStops = txc.VehicleJourneys.VehicleJourney
                .Where(vj => vj.ServiceRef.Value == service.ServiceCode && vj.LineRef == line.Id)
                .DistinctBy(vj => vj.JourneyPatternRef)
                .SelectMany(vj => service.StandardService.JourneyPattern.First(jp => jp.Id == vj.JourneyPatternRef).JourneyPatternSectionRefs)
                .SelectMany(jpsRef => txcDicts.JourneyPatternSections[jpsRef.Value].JourneyPatternTimingLink)
                .SelectMany(jptl => new string[] { jptl.From.StopPointRef.Value, jptl.To.StopPointRef.Value })
                .Distinct()
                .Select(stopPointRef => new TimetableBusStop(txcDicts.StopPoints[stopPointRef]))
                .ToArray();

            // Get all line sections used by this line
            List<Map.Route> journeys = [];
            foreach (VehicleJourney vehicleJourney in txc.VehicleJourneys.VehicleJourney
            .Where(vj => vj.ServiceRef.Value == service.ServiceCode &&
                vj.LineRef == line.Id))
            {
                vehicleJourney.OperatingProfile.GetRegularDays(out DayOfWeek[] regularDays);
                if (regularDays.Length == 0)
                    continue;
                JourneyPatternStructure journeyPattern = service.StandardService.JourneyPattern.First(jp => jp.Id == vehicleJourney.JourneyPatternRef);

                foreach (DayOfWeek dow in regularDays)
                {
                    DateOnly date = new DateOnly(1, 1, 1).AddDays((((int)dow) + 6) % 7); // Date will have correct day of week
                    if (date.DayOfWeek != dow)
                        throw new InvalidOperationException("Day of week calculation failed. This should never happen!");
                    Map.Route plannedRoute = Map.Route.FromTransXChange(vehicleJourney, journeyPattern, txcDicts, date);
                    journeys.Add(plannedRoute);
                }
            }
            Map.Route[] journeysArr = [.. journeys];

            lineSections = txc.VehicleJourneys.VehicleJourney
                .Where(vj => vj.ServiceRef.Value == service.ServiceCode && vj.LineRef == line.Id)
                .DistinctBy(vj => vj.JourneyPatternRef)
                .SelectMany(vj => service.StandardService.JourneyPattern.First(jp => jp.Id == vj.JourneyPatternRef).JourneyPatternSectionRefs)
                .SelectMany(jpsRef => txcDicts.JourneyPatternSections[jpsRef.Value].JourneyPatternTimingLink)
                .Select(jptl => txcDicts.RouteLinks[jptl.RouteLinkRef.Value])
                .DistinctBy(rl => (rl.From.StopPointRef.Value, rl.To.StopPointRef.Value))
                .Select(rl => new TimetableLineSection(rl, journeysArr))
                .ToArray();
        }

        public string ServiceCode { get; }
        public string LineId { get; }
        public string LineName { get; }
        public IReadOnlyList<string> OperatorNOCs => operatorNocs;
        public IReadOnlyList<string> TicketMachineServiceCodes => ticketMachineServiceCodes;
        public IReadOnlyList<TimetableBusStop> BusStops => busStops;
        public IReadOnlyList<TimetableLineSection> LineSections => lineSections;
    }
}
