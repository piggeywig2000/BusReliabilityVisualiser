using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public class TimetableService
    {
        private readonly Dictionary<string, TimetableLine> lines;

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
        }

        public string XmlPath { get; }
        public string ServiceCode { get; }
        public DateOnly StartDate { get; }
        public DateOnly? EndDate { get; }
        public IReadOnlyCollection<TimetableLine> Lines => lines.Values;

        public bool IsValidAtDate(DateOnly date) => date >= StartDate && (!EndDate.HasValue || date <= EndDate);

        public TimetableLine GetLine(string lineId) => lines[lineId];
    }
}
