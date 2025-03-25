interface DataResponse {
    lines: { [name: string]: DataLine };
}

interface DataLine {
    name: string;
    busStops: DataBusStop[];
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
    lateness: number;
}

interface DataLineSection {
    fromStopPointRef: string;
    toStopPointRef: string;
    track: DataTrack[];
}

interface DataTrack {
    longitude: number;
    latitude: number;
}

function dataReviver(key: string, value: any): any {
    if (key === "date") {
        return new Date(value);
    }
    return value;
}