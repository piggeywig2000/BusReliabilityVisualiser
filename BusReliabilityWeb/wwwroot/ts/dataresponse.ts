interface DataResponse {
    lines: { [name: string]: DataLine };
}

interface DataLine {
    name: string;
    busStops: { [stopPointRef: string]: DataBusStop };
    lineSections: DataLineSection[];
}

interface DataBusStop {
    stopPointRef: string;
    name: string;
    latenessValues: DataLateness[];
}

interface DataLateness {
    date: Date;
    hour: number;
    lateness: number | null;
}

interface DataLineSection {
    fromStopPointRef: string;
    toStopPointRef: string;
    track: DataTrack[];
    usage: DataLineUsage[];
}

interface DataTrack {
    longitude: number;
    latitude: number;
}

interface DataLineUsage {
    dayOfWeek: DayOfWeek;
    hour: number;
    busesPerHour: number;
}

enum DayOfWeek {
    Monday = "Monday",
    Tuesday = "Tuesday",
    Wednesday = "Wednesday",
    Thursday = "Thursday",
    Friday = "Friday",
    Saturday = "Saturday",
    Sunday = "Sunday"
}

function dataReviver(key: string, value: any): any {
    if (key === "date") {
        return new Date(value);
    }
    if (key === "dayOfWeek") {
        return DayOfWeek[value as keyof typeof DayOfWeek];
    }
    return value;
}