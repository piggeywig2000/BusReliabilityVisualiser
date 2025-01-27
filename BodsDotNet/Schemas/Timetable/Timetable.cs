namespace BodsDotNet.Schemas.Timetable
{
    public record Timetable(
        int Id,
        DateTime Created,
        DateTime Modified,
        string OperatorName,
        string[] NOC,
        string Name,
        string Description,
        string Comment,
        TimetablePublishStatus Status,
        string URL,
        string Extension,
        string[] Lines,
        DateTime? FirstStartDate,
        DateTime? FirstEndDate,
        DateTime? LastEndDate,
        AdminArea[] AdminAreas,
        Locality[] Localities,
        string DqScore,
        TimetableRag DqRag,
        bool? BodsCompliance);

    public enum TimetablePublishStatus { Published, Error, Inactive }
    public enum TimetableRag { Red, Amber, Green, Unavailable }
}
