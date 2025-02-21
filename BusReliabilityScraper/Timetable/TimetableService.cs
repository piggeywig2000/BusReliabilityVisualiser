using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusReliabilityScraper.Timetable
{
    internal class TimetableService(string serviceCode)
    {
        private readonly List<TimetablePeriod> timetables = [];

        public string ServiceCode { get; } = serviceCode;

        public bool Contains(TimetablePeriod period) => timetables.Exists(tp => tp.XmlPath == period.XmlPath && tp.ServiceIndex == period.ServiceIndex);

        public void AddTimetable(TimetablePeriod period)
        {
            if (Contains(period))
                throw new InvalidOperationException("Timetable already exists");
            timetables.Add(period);
            //timetables.Sort((a, b) => a.StartDate.CompareTo(b.StartDate));
        }

        public bool Contains(DateOnly date) => timetables.Any(tp => tp.IsValidAtDate(date));

        public TimetablePeriod GetTimetable(DateOnly date) => timetables
            .Where(tp => tp.IsValidAtDate(date))
            .OrderByDescending(tp => tp.StartDate)
            .FirstOrDefault() ?? throw new InvalidOperationException("No timetable for the date specified");
    }
}
