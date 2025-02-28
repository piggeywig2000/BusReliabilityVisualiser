using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Serialization;

namespace BodsDotNet
{
    public class BodsClient(string apiKey)
    {
        private readonly string apiKey = apiKey;
        private readonly HttpClient httpClient = new();
        private readonly XmlSerializer transXChangeSerializer = new(typeof(Schemas.TransXChange.TransXChange));
        private readonly XmlSerializer siriSerializer = new(typeof(Schemas.Siri.Siri));

        private static readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        public async Task<Schemas.Timetable.Timetable> GetTimetableById(int id, CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage httpResponse = await httpClient.GetAsync($"https://data.bus-data.dft.gov.uk/api/v1/dataset/{id}?api_key={apiKey}", cancellationToken);
            await using Stream stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                Schemas.Timetable.ErrorResponse? response = await JsonSerializer.DeserializeAsync<Schemas.Timetable.ErrorResponse>(stream, jsonSerializerOptions, cancellationToken);
                throw new BodsRequestException(response?.Detail ?? "An error occurred, but there was no description of what it was.", httpResponse.StatusCode);
            }
            else
            {
                Schemas.Timetable.Timetable? response = await JsonSerializer.DeserializeAsync<Schemas.Timetable.Timetable>(stream, jsonSerializerOptions, cancellationToken);
                return response ?? throw new BodsRequestException("Server returned success, but failed to parse timetable data.", httpResponse.StatusCode);
            }
        }

        public async Task DownloadTransXChangeFromUrl(string url, string downloadPath, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage httpResponse = await httpClient.GetAsync(url, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();
            await using Stream contentStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);

            using ZipArchive zipArchive = new(contentStream, ZipArchiveMode.Read);
            await Task.Run(() => zipArchive.ExtractToDirectory(downloadPath, overwrite), cancellationToken); // No async API for this yet, this is a bodge
        }

        public async Task<IReadOnlyCollection<Schemas.TransXChange.TransXChange>> GetTransXChangeFromUrl(string url, CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage httpResponse = await httpClient.GetAsync(url, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            await using Stream contentStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
            return await GetTransXChangeFromZipStream(contentStream, cancellationToken);
        }

        public async Task<IReadOnlyCollection<Schemas.TransXChange.TransXChange>> GetTransXChangeFromZipFile(string filePath, CancellationToken cancellationToken = default)
        {
            await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
            return await GetTransXChangeFromZipStream(fs, cancellationToken);
        }

        public async Task<Schemas.TransXChange.TransXChange> GetTransXChangeFromXmlFile(string filePath, CancellationToken cancellationToken = default)
        {
            await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
            return await GetTransXChangeFromXmlStream(fs, cancellationToken)
                ?? throw new XmlException($"Failed to parse the TransXChange file at {Path.GetFileName(filePath)}");
        }

        private async Task<IReadOnlyCollection<Schemas.TransXChange.TransXChange>> GetTransXChangeFromZipStream(Stream stream, CancellationToken cancellationToken)
        {
            using ZipArchive zipArchive = new(stream, ZipArchiveMode.Read);
            List<Schemas.TransXChange.TransXChange> txcs = [];

            foreach (ZipArchiveEntry entry in zipArchive.Entries.OrderBy(e => e.FullName))
            {
                await using Stream entryStream = entry.Open();
                Schemas.TransXChange.TransXChange txc = await GetTransXChangeFromXmlStream(entryStream, cancellationToken)
                    ?? throw new XmlException($"Failed to parse the TransXChange file at {entry.Name}");
                txcs.Add(txc);
            }

            return txcs;
        }

        private async Task<Schemas.TransXChange.TransXChange?> GetTransXChangeFromXmlStream(Stream stream, CancellationToken cancellationToken)
        {
            await using MemoryStream memoryStream = new();
            using StreamReader entryReader = new(memoryStream, System.Text.Encoding.UTF8, true);
            await stream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Seek(0, SeekOrigin.Begin);
            return (Schemas.TransXChange.TransXChange?)transXChangeSerializer.Deserialize(entryReader); // TODO: Run in worker thread
        }

        public async Task<Schemas.Siri.Siri> GetLocation(IEnumerable<string> operatorNocs, string line, IReadOnlyCollection<string> blockIds, CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage httpResponse = await httpClient.GetAsync($"https://data.bus-data.dft.gov.uk/api/v1/datafeed?operatorRef={string.Join(',', operatorNocs)}&lineRef={line}&api_key={apiKey}", cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            await using Stream contentStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
            await using MemoryStream memoryStream = new();
            using StreamReader entryReader = new(memoryStream, System.Text.Encoding.UTF8, true);
            await contentStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Seek(0, SeekOrigin.Begin);
            Schemas.Siri.Siri siri = (Schemas.Siri.Siri?)siriSerializer.Deserialize(entryReader) // TODO: Run in worker thread
                ?? throw new XmlException($"Failed to parse Siri format");

            if (siri.ServiceDelivery.VehicleMonitoringDeliverySpecified)
            {
                foreach (Schemas.Siri.VehicleMonitoringDeliveryStructure vmd in siri.ServiceDelivery.VehicleMonitoringDelivery.Where(vmd => vmd.VehicleActivitySpecified))
                {
                    IEnumerable<int> toRemove = vmd.VehicleActivity
                        .Index()
                        .Where(va => !blockIds.Contains(va.Item.MonitoredVehicleJourney.BlockRef.Value))
                        .Select(va => va.Index);

                    foreach (int i in toRemove.OrderDescending())
                        vmd.VehicleActivity.RemoveAt(i); // Delete vehicle activies that aren't on our route
                }
            }

            return siri;
        }

        public async Task<Schemas.Siri.Siri> GetLocation(IEnumerable<string> operatorNocs, CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage httpResponse = await httpClient.GetAsync($"https://data.bus-data.dft.gov.uk/api/v1/datafeed?operatorRef={string.Join(',', operatorNocs)}&api_key={apiKey}", cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            await using Stream contentStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
            await using MemoryStream memoryStream = new();
            using StreamReader entryReader = new(memoryStream, System.Text.Encoding.UTF8, true);
            await contentStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Seek(0, SeekOrigin.Begin);
            Schemas.Siri.Siri siri = (Schemas.Siri.Siri?)siriSerializer.Deserialize(entryReader) // TODO: Run in worker thread
                ?? throw new XmlException($"Failed to parse Siri format");

            return siri;
        }

        [Obsolete]
        public async Task<Schemas.Siri.Siri> GetLocation(double minLongitude, double minLatitude, double maxLongitude, double maxLatitude, IEnumerable<string> operatorNocs, string line)
        {
            using HttpResponseMessage httpResponse = await httpClient.GetAsync($"https://data.bus-data.dft.gov.uk/api/v1/datafeed?boundingBox={minLongitude},{minLatitude},{maxLongitude},{maxLatitude}&operatorRef={string.Join(',', operatorNocs)}&lineRef={line}&api_key={apiKey}");
            httpResponse.EnsureSuccessStatusCode();

            Stream contentStream = await httpResponse.Content.ReadAsStreamAsync();
            using MemoryStream memoryStream = new();
            using StreamReader entryReader = new(memoryStream, System.Text.Encoding.UTF8, true);
            await contentStream.CopyToAsync(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            Schemas.Siri.Siri siri = (Schemas.Siri.Siri?)siriSerializer.Deserialize(entryReader)
                ?? throw new XmlException($"Failed to parse Siri format");

            return siri;
        }
    }
}
