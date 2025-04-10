using BusReliabilityWeb.Controllers.Dto;
using BusReliabilityWeb.Database;
using BusReliabilityWeb.Database.Dto;
using BusReliabilityWeb.Timetable;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BusReliabilityWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DataController : ControllerBase
    {
        private readonly ILogger<DataController> logger;
        private readonly IConfiguration configuration;
        private readonly DbController dbController;
        private readonly TimetableFileManager timetableFileManager;

        public DataController(ILogger<DataController> logger, IConfiguration configuration, DbController dbController, TimetableFileManager timetableFileManager)
        {
            this.logger = logger;
            this.configuration = configuration;
            this.dbController = dbController;
            this.timetableFileManager = timetableFileManager;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            // Check if we have a date override in the config
            string? timetableDateStr = configuration.GetValue<string>("VisualisationDateOverride");
            if (string.IsNullOrEmpty(timetableDateStr) || !DateOnly.TryParse(timetableDateStr, out DateOnly timetableDate))
            {
                timetableDate = Util.GmtNowDate;
            }
            // Build all line data
            Dictionary<string, DataLine> lines = [];
            string[] deletedLines = ["373", "376a", "376x", "55", "OS1"]; // We don't want to bother with these lines. They're far away from Bath and quite long (laggy)
            foreach (TimetableLine line in timetableFileManager.GetAllTimetablesAtDate(timetableDate).SelectMany(s => s.Lines))
            {
                if (Array.IndexOf(deletedLines, line.LineName) >= 0)
                    continue; // Don't include some deleted lines
                LatenessValue[] latenessValues = await dbController.GetLatenessValuesForLine(line.ServiceCode, line.LineId, cancellationToken);
                Dictionary<string, DataBusStop> stops = [];
                Dictionary<string, List<DataLateness>> naptanToLateness = [];

                // Group all lateness values by stop
                foreach (LatenessValue lateness in latenessValues)
                {
                    if (!naptanToLateness.TryGetValue(lateness.StopNaptan, out List<DataLateness>? latenessList))
                    {
                        latenessList = [];
                        naptanToLateness[lateness.StopNaptan] = latenessList;
                    }
                    latenessList.Add(new(lateness.TimetableDate, lateness.Hour, lateness.Lateness));
                }

                // Add stops with lateness to final data list
                foreach (TimetableBusStop stop in line.BusStops)
                {
                    stops.Add(stop.StopPointRef, new(stop.StopPointRef, stop.Name, naptanToLateness.GetValueOrDefault(stop.StopPointRef, [])));
                }

                // Add line
                lines.Add(line.LineName, new(line.LineName, stops, line.LineSections));
            }
            DataResponse response = new(lines);
            return Ok(response);
        }
    }
}
