namespace BodsDotNet.Schemas.Timetable
{
    public record TimetableResponse(int Count, string? Next, string? Previous, Timetable[] Results);
}
