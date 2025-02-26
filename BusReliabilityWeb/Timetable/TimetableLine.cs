using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public class TimetableLine
    {
        private readonly string[] operators;
        private readonly string[] blockIds;

        public TimetableLine(TransXChange txc, string serviceCode, string lineId)
        {
            Service service = txc.Services.Service.First(s => s.ServiceCode == serviceCode);
            Line line = service.Lines.First(l => l.Id == lineId);
            ServiceCode = service.ServiceCode;
            LineId = line.Id;
            LineName = line.LineName.Value;
            // Get all national operator codes used by this line
            operators = txc.VehicleJourneys.VehicleJourney
                .Where(vj => vj.ServiceRef.Value == service.ServiceCode && vj.LineRef == line.Id)
                .Select(vj => txc.Operators.Operator.First(o => o.Id == vj.OperatorRef.Value).NationalOperatorCode)
                .Distinct()
                .ToArray();
            // Get all block ids used by this line
            blockIds = txc.VehicleJourneys.VehicleJourney
                .Where(vj => vj.ServiceRef.Value == service.ServiceCode && vj.LineRef == line.Id)
                .Select(vj => vj.Operational.Block.BlockNumber)
                .Distinct()
                .ToArray();
        }

        public string ServiceCode { get; }
        public string LineId { get; }
        public string LineName { get; }
        public IReadOnlyCollection<string> Operators => operators;
        public IReadOnlyCollection<string> BlockIds => blockIds;
    }
}
