using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusReliabilityWeb.Timetable
{
    public class TimetableServiceGroup(string serviceCode)
    {
        private readonly List<TimetableService> timetables = [];

        public IReadOnlyCollection<TimetableService> Timetables => timetables;
        public string ServiceCode { get; } = serviceCode;
        public IEnumerable<string> OperatorNOCs => timetables.SelectMany(t => t.Lines.SelectMany(l => l.OperatorNOCs)).Distinct();

        public bool Contains(TimetableService period) => timetables.Exists(tp => tp.XmlPath == period.XmlPath && tp.ServiceCode == period.ServiceCode);

        public void AddTimetable(TimetableService period)
        {
            if (Contains(period))
                throw new InvalidOperationException("Timetable already exists");
            timetables.Add(period);
            timetables.Sort((a, b) => b.StartDate.CompareTo(a.StartDate)); // Sort descending
        }

        public bool HasDate(DateOnly date) => timetables.Any(tp => tp.IsValidAtDate(date));

        public TimetableService GetTimetable(DateOnly date) => timetables
            .Where(tp => tp.IsValidAtDate(date))
            .FirstOrDefault() ?? throw new InvalidOperationException("No timetable for the date specified");
    }
}
