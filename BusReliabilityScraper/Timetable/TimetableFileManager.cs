using BodsDotNet;

namespace BusReliabilityScraper.Timetable
{
    internal class TimetableFileManager
    {
        private readonly BodsClient bodsClient;
        private readonly string fileDirectory;
        private DateOnly dateLastDownloaded = DateOnly.MinValue;
        private readonly TimeOnly timeOfDayUpdate;

        private readonly Dictionary<string, TimetableService> services = [];

        public TimetableFileManager(BodsClient bodsClient, string fileDirectory, TimeOnly timeOfDayUpdate)
        {
            this.bodsClient = bodsClient;
            this.fileDirectory = fileDirectory;
            this.timeOfDayUpdate = timeOfDayUpdate;
            if (!Directory.Exists(fileDirectory))
                Directory.CreateDirectory(fileDirectory);
        }

        public TimetablePeriod GetTimetable(string serviceCode, DateOnly date) => services[serviceCode].GetTimetable(date);

        public bool IsReadyForUpdate()
        {
            DateTime now = DateTime.UtcNow;
            DateOnly nowDate = DateOnly.FromDateTime(now);
            if (nowDate <= dateLastDownloaded)
            {
                return false; // It's the same day (or before?! shouldn't happen), guarenteed false
            }
            else if (nowDate.AddDays(-2) >= dateLastDownloaded)
            {
                return true; // It's 2 days since, guarenteed true
            }
            else
            {
                return TimeOnly.FromDateTime(now) >= timeOfDayUpdate; // Is it after the time for update?
            }
        }

        public async Task UpdateTimetables()
        {
            if (!IsReadyForUpdate())
                throw new InvalidOperationException("It's too early to update timetables");
            dateLastDownloaded = DateOnly.FromDateTime(DateTime.UtcNow);
            Console.WriteLine("Updating timetables");

            // For now just first bus in Bath. Consider multiple timetable sources?
            BodsDotNet.Schemas.Timetable.Timetable timetable = await bodsClient.GetTimetableById(5813);
            await bodsClient.DownloadTransXChangeFromUrl(timetable.URL, $"{fileDirectory}/{timetable.Id}");

            // Update services dictionary
            services.Clear();
            foreach (string xmlPath in Directory.EnumerateFiles(fileDirectory, "*.xml", SearchOption.AllDirectories))
            {
                BodsDotNet.Schemas.TransXChange.TransXChange txc = await bodsClient.GetTransXChangeFromXmlFile(xmlPath);
                foreach ((int serviceIndex, BodsDotNet.Schemas.TransXChange.Service txcService) in txc.Services.Service.Index())
                {
                    Console.WriteLine($"Process {Path.GetFileName(xmlPath)}: {serviceIndex}");
                    if (!services.TryGetValue(txcService.ServiceCode, out TimetableService? tService))
                    {
                        tService = new TimetableService(txcService.ServiceCode);
                        services.Add(txcService.ServiceCode, tService);
                    }
                    TimetablePeriod periodToAdd = new(xmlPath, serviceIndex,
                        DateOnly.FromDateTime(txcService.OperatingPeriod.StartDate),
                        txcService.OperatingPeriod.EndDateSpecified ? DateOnly.FromDateTime(txcService.OperatingPeriod.EndDate) : null);
                    tService.AddTimetable(periodToAdd);
                }
            }
        }
    }
}
