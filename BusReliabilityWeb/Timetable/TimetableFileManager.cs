using BodsDotNet;

namespace BusReliabilityWeb.Timetable
{
    public class TimetableFileManager
    {
        private readonly ILogger<TimetableFileManager> logger;
        private readonly BodsClient bodsClient;
        private readonly string fileDirectory;

        private Dictionary<string, TimetableServiceGroup> services = [];
        private string[] operatorNocs = [];
        private Dictionary<(string, string), TimetableServiceGroup> nocTmscToService = [];

        public TimetableFileManager(ILogger<TimetableFileManager> logger, IConfiguration configuration, BodsClient bodsClient)
        {
            this.logger = logger;
            this.bodsClient = bodsClient;
            fileDirectory = configuration["TimetablePath"] ?? throw new InvalidOperationException("No timetable path provided");
            if (!Directory.Exists(fileDirectory))
                Directory.CreateDirectory(fileDirectory);
        }

        public IReadOnlyCollection<string> OperatorNocs => operatorNocs;

        public IEnumerable<TimetableService> GetAllTimetablesAtDate(DateOnly date) => services.Values
            .Where(tsg => tsg.HasDate(date))
            .Select(tsg => tsg.GetTimetable(date));

        public TimetableService GetTimetable(string serviceCode, DateOnly date) => services[serviceCode].GetTimetable(date);

        public TimetableService? TryGetTimetableFromNocTmsc(string noc, string ticketMachineServiceCode, DateOnly date) =>
            nocTmscToService.TryGetValue((noc, ticketMachineServiceCode), out TimetableServiceGroup? tsg) ?
                tsg.HasDate(date) ? nocTmscToService[(noc, ticketMachineServiceCode)].GetTimetable(date) : null : null;

        public async Task UpdateTimetables(CancellationToken cancellationToken)
        {
            logger.LogInformation("Updating timetables");

            // For now just first bus in Bath. Consider multiple timetable sources?
            BodsDotNet.Schemas.Timetable.Timetable timetable = await bodsClient.GetTimetableById(5813, cancellationToken);
            string[] extractedXmls = await bodsClient.DownloadTransXChangeFromUrl(timetable.URL, $"{fileDirectory}/{timetable.Id}/", true, cancellationToken);

            // Update services dictionary
            Dictionary<string, TimetableServiceGroup> newServices = [];
            foreach (string xmlPath in Directory.EnumerateFiles(fileDirectory, "*.xml", SearchOption.AllDirectories).Select(Path.GetFullPath))
            {
                //if (!Path.GetFileName(xmlPath).StartsWith("U1"))
                //    continue;
                BodsDotNet.Schemas.TransXChange.TransXChange txc = await bodsClient.GetTransXChangeFromXmlFile(xmlPath, cancellationToken);

                // Ensure that this TXC is something we can handle
                if (!txc.VehicleJourneys.VehicleJourneySpecified)
                {
                    logger.LogWarning("Cannot use {xmlPath} as it does not specify vehicle journeys", xmlPath);
                    continue;
                }

                foreach (BodsDotNet.Schemas.TransXChange.Service txcService in txc.Services.Service)
                {
                    logger.LogDebug("Process {fileName}: {serviceIndex}", Path.GetFileName(xmlPath), txcService.ServiceCode);

                    // Ensure that this TXC service is something we can handle
                    if (!txcService.StandardService.JourneyPatternSpecified)
                    {
                        logger.LogWarning("Cannot use {xmlPath}: {serviceIndex} as it does not specify journey patterns", xmlPath, txcService.ServiceCode);
                        continue;
                    }

                    TimetableService periodToAdd;
                    try
                    {
                        periodToAdd = new(xmlPath, txcService.ServiceCode, txc);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Failed to parse {xmlPath}", xmlPath);
                        continue; // Just don't bother dealing with it. Don't collect data on this service
                    }
                    if (!newServices.TryGetValue(txcService.ServiceCode, out TimetableServiceGroup? tGroup))
                    {
                        tGroup = new TimetableServiceGroup(txcService.ServiceCode);
                        newServices.Add(txcService.ServiceCode, tGroup);
                    }
                    tGroup.AddTimetable(periodToAdd);
                }
            }

            // Remove retracted services from service groups
            RemoveRetractedServices(newServices, extractedXmls);

            // If multiple services have same NOC and TMSC, we can't differentiate them. Just drop the services and don't collect data
            Dictionary<(string, string), TimetableServiceGroup> newNocTmscToService = [];
            List<(string, string, TimetableServiceGroup)> pairsToRemove = [];
            foreach ((string noc, string tmsc, TimetableServiceGroup tsg) in newServices.Values
                .SelectMany(tsg => tsg.Timetables
                    .SelectMany(s => s.Lines
                        .SelectMany(l => l.OperatorNOCs
                            .SelectMany(noc => l.TicketMachineServiceCodes
                                .Select(tmsc => (noc, tmsc, tsg))))))
                .Distinct())
            {
                // If we have a key clash, multiple TxC files are for the same service. We're not going to deal with this
                if (newNocTmscToService.TryGetValue((noc, tmsc), out TimetableServiceGroup? clashedTsg))
                {
                    pairsToRemove.Add((noc, tmsc, tsg));
                    pairsToRemove.Add((noc, tmsc, clashedTsg));
                }
                else
                {
                    newNocTmscToService[(noc, tmsc)] = tsg;
                }
            }

            // Remove clashed lines
            foreach ((string noc, string tmsc, TimetableServiceGroup tsg) in pairsToRemove)
            {
                TimetableLine[] linesToRemove = tsg.Timetables
                    .SelectMany(t => t.Lines)
                    .Where(l => l.OperatorNOCs.Contains(noc) && l.TicketMachineServiceCodes.Contains(tmsc))
                    .Distinct()
                    .ToArray();

                foreach (TimetableLine lineToRemove in linesToRemove)
                {
                    TimetableService? service = tsg.Timetables.FirstOrDefault(t => t.Lines.Contains(lineToRemove));
                    service?.RemoveLine(lineToRemove.LineId);
                }

                newNocTmscToService.Remove((noc, tmsc));
            }
            // Remove now empty services and service groups
            foreach (TimetableServiceGroup tsg in newServices.Values)
                tsg.RemoveEmptyTimetables();
            string[] servicesToRemove = newServices.Values
                .Where(tsg => tsg.Timetables.Count == 0)
                .Select(tsg => tsg.ServiceCode)
                .ToArray();
            foreach (string serviceCode in servicesToRemove)
                newServices.Remove(serviceCode);

            services = newServices;
            operatorNocs = newServices.Values
                .SelectMany(tsg => tsg.OperatorNOCs)
                .Distinct()
                .ToArray();
            nocTmscToService = newNocTmscToService!;

            logger.LogInformation("Updated timetables");
        }

        private void RemoveRetractedServices(Dictionary<string, TimetableServiceGroup> services, string[] newXmls)
        {
            for (int i = 0; i < newXmls.Length; i++)
                newXmls[i] = Path.GetFullPath(newXmls[i]);

            foreach (TimetableServiceGroup tsg in services.Values)
            {
                // Get services that were in the ZIP we just downloaded
                TimetableService[] currentServices = tsg.Timetables
                    .Where(t => Array.IndexOf(newXmls, t.XmlPath) >= 0)
                    .ToArray();
                if (currentServices.Length == 0)
                    continue;
                DateOnly currentRangeStart = currentServices.Min(t => t.StartDate);
                DateOnly currentRangeEnd = currentServices.Max(t => t.StartDate);

                // Delete services not in the ZIP we just downloaded (but within the date range)
                TimetableService[] servicesToRemove = tsg.Timetables
                    .Where(t => t.StartDate >= currentRangeStart && t.StartDate <= currentRangeEnd && Array.IndexOf(currentServices, t) < 0)
                    .ToArray();
                foreach (TimetableService serviceToRemove in servicesToRemove)
                {
                    logger.LogDebug("Deleting timetable file {fileName}", Path.GetFileName(serviceToRemove.XmlPath));
                    tsg.RemoveTimetable(serviceToRemove);
                    File.Delete(serviceToRemove.XmlPath);
                }
            }
        }
    }
}
