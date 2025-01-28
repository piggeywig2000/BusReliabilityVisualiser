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

        public async Task<Schemas.Timetable.Timetable> GetTimetableById(int id)
        {
            using HttpResponseMessage httpResponse = await httpClient.GetAsync($"https://data.bus-data.dft.gov.uk/api/v1/dataset/{id}?api_key={apiKey}");
            using Stream stream = await httpResponse.Content.ReadAsStreamAsync();

            if (!httpResponse.IsSuccessStatusCode)
            {
                Schemas.Timetable.ErrorResponse? response = await JsonSerializer.DeserializeAsync<Schemas.Timetable.ErrorResponse>(stream, jsonSerializerOptions);
                throw new BodsRequestException(response?.Detail ?? "An error occurred, but there was no description of what it was.", httpResponse.StatusCode);
            }
            else
            {
                Schemas.Timetable.Timetable? response = await JsonSerializer.DeserializeAsync<Schemas.Timetable.Timetable>(stream, jsonSerializerOptions);
                return response ?? throw new BodsRequestException("Server returned success, but failed to parse timetable data.", httpResponse.StatusCode);
            }
        }

        public async Task<IReadOnlyCollection<Schemas.TransXChange.TransXChange>> GetTransXChangeFromUrl(string url)
        {
            using HttpResponseMessage httpResponse = await httpClient.GetAsync(url);
            httpResponse.EnsureSuccessStatusCode();

            using Stream contentStream = await httpResponse.Content.ReadAsStreamAsync();
            return await GetTransXChangeFromStream(contentStream);
        }

        public async Task<IReadOnlyCollection<Schemas.TransXChange.TransXChange>> GetTransXChangeFromFile(string filePath)
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
            return await GetTransXChangeFromStream(fs);
        }

        private async Task<IReadOnlyCollection<Schemas.TransXChange.TransXChange>> GetTransXChangeFromStream(Stream stream)
        {
            using ZipArchive zipArchive = new(stream, ZipArchiveMode.Read);
            List<Schemas.TransXChange.TransXChange> txcs = [];

            foreach (ZipArchiveEntry entry in zipArchive.Entries.OrderBy(e => e.FullName))
            {
                using Stream entryStream = entry.Open();
                using MemoryStream memoryStream = new();
                using StreamReader entryReader = new(memoryStream, System.Text.Encoding.UTF8, true);
                await entryStream.CopyToAsync(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                Schemas.TransXChange.TransXChange txc = (Schemas.TransXChange.TransXChange?)transXChangeSerializer.Deserialize(entryReader)
                    ?? throw new XmlException($"Failed to parse the TransXChange file at {entry.FullName}");
                txcs.Add(txc);
            }

            return txcs;
        }

        public async Task<Schemas.Siri.Siri> GetLocation(double minLongitude, double minLatitude, double maxLongitude, double maxLatitude, string[] operatorNocs, string line)
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
