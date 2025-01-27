using System.Text.Json;
using System.Text.Json.Serialization;

namespace BodsDotNet
{
    public class BodsClient(string apiKey)
    {
        private readonly string apiKey = apiKey;
        private readonly HttpClient httpClient = new();

        private static readonly JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions()
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
    }
}
