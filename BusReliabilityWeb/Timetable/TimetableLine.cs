using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public class TimetableLine
    {
        private readonly string[] operatorNocs;
        private readonly string[] ticketMachineServiceCodes;

        public TimetableLine(TransXChange txc, string serviceCode, string lineId)
        {
            Service service = txc.Services.Service.First(s => s.ServiceCode == serviceCode);
            Line line = service.Lines.First(l => l.Id == lineId);

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
        }

        public string ServiceCode { get; }
        public string LineId { get; }
        public string LineName { get; }
        public IReadOnlyList<string> OperatorNOCs => operatorNocs;
        public IReadOnlyList<string> TicketMachineServiceCodes => ticketMachineServiceCodes;
    }
}
