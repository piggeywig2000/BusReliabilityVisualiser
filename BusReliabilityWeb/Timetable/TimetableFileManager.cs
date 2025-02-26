using BodsDotNet;

namespace BusReliabilityWeb.Timetable
{
    public class TimetableFileManager
    {
        private readonly ILogger<TimetableFileManager> logger;
        private readonly BodsClient bodsClient;
        private readonly string fileDirectory;

        private Dictionary<string, TimetableServiceGroup> services = [];

        public TimetableFileManager(ILogger<TimetableFileManager> logger, IConfiguration configuration, BodsClient bodsClient)
        {
            this.logger = logger;
            this.bodsClient = bodsClient;
            fileDirectory = configuration["TimetablePath"] ?? throw new InvalidOperationException("No timetable path provided");
            if (!Directory.Exists(fileDirectory))
                Directory.CreateDirectory(fileDirectory);
        }

        public TimetableService[] GetAllTimetablesAtDate(DateOnly date) => services.Values
            .Where(tsg => tsg.Contains(date))
            .Select(tsg => tsg.GetTimetable(date))
            .ToArray();

        public TimetableService GetTimetable(string serviceCode, DateOnly date) => services[serviceCode].GetTimetable(date);

        public async Task UpdateTimetables(CancellationToken cancellationToken)
        {
            logger.LogInformation("Updating timetables");

            // For now just first bus in Bath. Consider multiple timetable sources?
            BodsDotNet.Schemas.Timetable.Timetable timetable = await bodsClient.GetTimetableById(5813, cancellationToken);
            await bodsClient.DownloadTransXChangeFromUrl(timetable.URL, $"{fileDirectory}/{timetable.Id}", true, cancellationToken);

            // Update services dictionary
            Dictionary<string, TimetableServiceGroup> newServices = [];
            foreach (string xmlPath in Directory.EnumerateFiles(fileDirectory, "*.xml", SearchOption.AllDirectories))
            {
                if (!Path.GetFileName(xmlPath).StartsWith("U1"))
                    continue;
                BodsDotNet.Schemas.TransXChange.TransXChange txc = await bodsClient.GetTransXChangeFromXmlFile(xmlPath, cancellationToken);
                foreach (BodsDotNet.Schemas.TransXChange.Service txcService in txc.Services.Service)
                {
                    logger.LogDebug("Process {fileName}: {serviceIndex}", Path.GetFileName(xmlPath), txcService.ServiceCode);
                    if (!newServices.TryGetValue(txcService.ServiceCode, out TimetableServiceGroup? tGroup))
                    {
                        tGroup = new TimetableServiceGroup(txcService.ServiceCode);
                        newServices.Add(txcService.ServiceCode, tGroup);
                    }
                    TimetableService periodToAdd = new(xmlPath, txcService.ServiceCode, txc);
                    tGroup.AddTimetable(periodToAdd);
                }
            }
            services = newServices;

            logger.LogInformation("Updated timetables");
        }
    }
}
