using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public class TimetableService
    {
        private readonly Dictionary<string, TimetableLine> lines;
        private readonly Dictionary<string, TimetableLine> lineNameToLine;

        public TimetableService(string xmlPath, string serviceCode, TransXChange txc)
        {
            XmlPath = xmlPath;
            Service service = txc.Services.Service.First(s => s.ServiceCode == serviceCode);
            ServiceCode = service.ServiceCode;

            StartDate = DateOnly.FromDateTime(service.OperatingPeriod.StartDate);
            EndDate = service.OperatingPeriod.EndDateSpecified ? DateOnly.FromDateTime(service.OperatingPeriod.EndDate) : null;
            lines = service.Lines
                .Select(l => new TimetableLine(txc, ServiceCode, l.Id))
                .ToDictionary(l => l.LineId);
            lineNameToLine = lines.Values.ToDictionary(l => l.LineName);
        }

        public string XmlPath { get; }
        public string ServiceCode { get; }
        public DateOnly StartDate { get; }
        public DateOnly? EndDate { get; }
        public IReadOnlyCollection<TimetableLine> Lines => lines.Values;

        public bool IsValidAtDate(DateOnly date) => date >= StartDate && (!EndDate.HasValue || date <= EndDate);

        public TimetableLine GetLine(string lineId) => lines[lineId];

        public TimetableLine? TryGetLineFromName(string lineName)
        {
            lineNameToLine.TryGetValue(lineName, out TimetableLine? line);
            return line;
        }

        public void RemoveLine(string lineId)
        {
            if (!lines.TryGetValue(lineId, out TimetableLine? line))
                return;
            lines.Remove(lineId);
            lineNameToLine.Remove(line.LineName);
        }
    }
}
