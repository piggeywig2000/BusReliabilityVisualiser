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

        public string ServiceCode { get; } = serviceCode;

        public bool Contains(TimetableService period) => timetables.Exists(tp => tp.XmlPath == period.XmlPath && tp.ServiceCode == period.ServiceCode);

        public void AddTimetable(TimetableService period)
        {
            if (Contains(period))
                throw new InvalidOperationException("Timetable already exists");
            timetables.Add(period);
            //timetables.Sort((a, b) => a.StartDate.CompareTo(b.StartDate));
        }

        public bool Contains(DateOnly date) => timetables.Any(tp => tp.IsValidAtDate(date));

        public TimetableService GetTimetable(DateOnly date) => timetables
            .Where(tp => tp.IsValidAtDate(date))
            .OrderByDescending(tp => tp.StartDate)
            .FirstOrDefault() ?? throw new InvalidOperationException("No timetable for the date specified");
    }
}
