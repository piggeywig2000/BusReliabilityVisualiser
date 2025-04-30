interface Window {
    AppConfig: {
        STADIA_API_KEY: string;
    };
}

class BusData implements DataResponse {
    lines: { [name: string]: BusLine; };

    private constructor(dataResponse: DataResponse) {
        this.lines = {};
        for (const lineName in dataResponse.lines) {
            this.lines[lineName] = BusLine.fromData(dataResponse.lines[lineName]);
        }
    }

    static fromData(dataResponse: DataResponse): BusData {
        return new BusData(dataResponse);
    }
}

class BusLine implements DataLine {
    name: string;
    busStops: { [stopPointRef: string]: DataBusStop };
    lineSections: BusLineSection[];

    private constructor(dataLine: DataLine) {
        this.name = dataLine.name;
        this.busStops = dataLine.busStops;
        this.lineSections = dataLine.lineSections.map(dls => BusLineSection.fromData(dls, this.busStops));
    }

    static fromData(dataLine: DataLine): BusLine {
        return new BusLine(dataLine);
    }
}

class BusLineSection implements DataLineSection {
    fromStopPointRef: string;
    toStopPointRef: string;
    track: DataTrack[];
    usage: DataLineUsage[];

    fromStopPoint: DataBusStop;
    toStopPoint: DataBusStop;

    private constructor(dataLineSection: DataLineSection, busStops: { [stopPointRef: string]: DataBusStop }) {
        this.fromStopPointRef = dataLineSection.fromStopPointRef;
        this.toStopPointRef = dataLineSection.toStopPointRef;
        this.track = dataLineSection.track;
        this.usage = dataLineSection.usage;
        this.fromStopPoint = busStops[this.fromStopPointRef];
        this.toStopPoint = busStops[this.toStopPointRef];
    }

    static fromData(dataLineSection: DataLineSection, busStops: { [stopPointRef: string]: DataBusStop }): BusLineSection {
        return new BusLineSection(dataLineSection, busStops);
    }
}

// Constants
const MAP_GRN_LATENESS = 0;
const MAP_RED_LATENESS = 40;
const TIMELINE_GRN_LATENESS = 0;
const TIMELINE_RED_LATENESS_ALL = 1500;
const TIMELINE_RED_LATENESS_SINGLE = 200;

// References to elements
const dowSelectEle: HTMLSelectElement = document.getElementById("dow-select") as HTMLSelectElement;
const linesList: HTMLDivElement = document.getElementById("lines-list") as HTMLDivElement;
const lineEles: { [name: string]: HTMLInputElement; } = {};
const timelineEle: HTMLDivElement = document.getElementById("timerange-line") as HTMLDivElement;
const tlGrabLeft: HTMLDivElement = document.querySelector("#timerange-line .timerange-grab.timerange-grab-left") as HTMLDivElement;
const tlGrabRight: HTMLDivElement = document.querySelector("#timerange-line .timerange-grab.timerange-grab-right") as HTMLDivElement;
const tlBlocks: NodeListOf<HTMLDivElement> = document.querySelectorAll("#timerange-line .timerange-line-block") as NodeListOf<HTMLDivElement>;

// Data
let data: BusData | null = null;
let currentDowPick: string = "weekdays";
let currentLine: string | null = "all";
let tlRangeLeft: number = 12;
let tlRangeRight: number = 18;
let tlIsResizingLeft: boolean | null;
let tlGrabbingStartX: number | null = null;
let tlGrabbingStartLeft: number | null = null;

// Create map
const map: L.Map = L.map("map").setView([51.3776019, -2.3567216], 14);
let lastZoomLevel = 14;
const LINE_STYLE_ZOOM_THRESHOLD = 14;
//L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
//    maxZoom: 19,
//    attribution: `&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>`,
//    className: "tile-layer-greyscale"
//}).addTo(map); // OpenStreetMap layer
L.tileLayer(`https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png?api_key=${window.AppConfig.STADIA_API_KEY}`, {
    maxZoom: 20,
    minZoom: 12,
    attribution: `&copy; <a href="https://stadiamaps.com/" target="_blank">Stadia Maps</a>, &copy; <a href="https://openmaptiles.org/" target="_blank">OpenMapTiles</a> &copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>`,
}).addTo(map); // Stadia maps layer
//L.tileLayer(`https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png`, {
//    maxZoom: 20,
//    minZoom: 12,
//    attribution: `&copy; <a href="https://stadiamaps.com/" target="_blank">Stadia Maps</a>, &copy; <a href="https://openmaptiles.org/" target="_blank">OpenMapTiles</a> &copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>`,
//}).addTo(map); // Stadia maps layer no key

let linesLayer: L.LayerGroup<any> = L.layerGroup().addTo(map);
let stopsLayer: L.LayerGroup<any> = L.layerGroup().addTo(map);

map.on("zoomend", (ev) => {
    let currentZoomLevel = map.getZoom();
    let newLineStyle: L.LineCapShape | null = null;
    let newOpacityMultiplier: number | null = null;
    if (currentZoomLevel >= LINE_STYLE_ZOOM_THRESHOLD && lastZoomLevel < LINE_STYLE_ZOOM_THRESHOLD) {
        newLineStyle = "butt";
        newOpacityMultiplier = 4;
    }
    else if (currentZoomLevel < LINE_STYLE_ZOOM_THRESHOLD && lastZoomLevel >= LINE_STYLE_ZOOM_THRESHOLD) {
        newLineStyle = "round";
        newOpacityMultiplier = 1 / 4;
    }
    if (newLineStyle !== null && newOpacityMultiplier !== null) {
        lastZoomLevel = currentZoomLevel;
        linesLayer.eachLayer((lineLayer) => {
            if (lineLayer instanceof L.Polyline) {
                lineLayer.setStyle({
                    lineCap: newLineStyle,
                    opacity: (lineLayer.options.opacity ?? 1) * newOpacityMultiplier
                });
            }
        });
    }
});

tlGrabLeft.addEventListener("pointerdown", onResizeStart);
tlGrabRight.addEventListener("pointerdown", onResizeStart);

async function init(): Promise<void> {
    await initData();
    initLines();
    initDowPicker();
    redrawMap();
    redrawTimeline(true);
    document.getElementById("loading-background")?.style.setProperty("display", "none");
}

async function initData(): Promise<void> {
    const response: Response = await fetch("api/data");
    const rawData: DataResponse = JSON.parse(await response.text(), dataReviver) as DataResponse;
    data = BusData.fromData(rawData);
}

function initLines(): void {
    if (data === null) return;

    createLineElement("All Bus Lines", "all");

    Object.keys(data.lines).sort().forEach(lineName => {
        if (data === null) return;
        const line: BusLine = data.lines[lineName];
        createLineElement(line.name, line.name);
    });

    lineEles["all"].checked = true;
}

function initDowPicker(): void {
    dowSelectEle.addEventListener("change", (ev: Event) => {
        currentDowPick = dowSelectEle.value;
        redrawMap();
        redrawTimeline(true);
    });
}

function createLineElement(name: string, id: string): void {
    const lineInput: HTMLInputElement = document.createElement("input");
    lineInput.type = "checkbox";
    lineInput.checked = false;
    lineInput.id = `line-item-${id}`;
    lineInput.name = `line-item-${id}`;
    lineInput.value = id;
    const lineLabel: HTMLLabelElement = document.createElement("label");
    lineLabel.htmlFor = `line-item-${id}`;
    lineLabel.classList.add("line-item");
    const lineText: HTMLSpanElement = document.createElement("span");
    lineText.innerText = name;
    lineLabel.appendChild(lineInput).addEventListener("change", onLineChange);
    lineLabel.appendChild(lineText);
    linesList.appendChild(lineLabel);
    lineEles[id] = lineInput;
}

function onLineChange(this: HTMLInputElement, ev: Event): any {
    const lineId: string = this.value;
    // Disable all other lines
    for (const otherLine in lineEles) {
        if (otherLine !== lineId) {
            lineEles[otherLine].checked = false;
        }
    }
    currentLine = this.checked ? lineId : null;
    redrawMap();
    redrawTimeline(true);
}

function redrawMap(): void {
    if (data === null) return;

    map.removeLayer(linesLayer);
    linesLayer = L.layerGroup().addTo(map);
    map.removeLayer(stopsLayer);
    stopsLayer = L.layerGroup().addTo(map);

    const activeLines: BusLine[] = currentLine === null ? [] : (currentLine === "all" ? Object.keys(data.lines).map(k => data!.lines[k]) : [data.lines[currentLine]]);

    // Calculate max usage
    const maxUsage = activeLines
        .map(l => l.lineSections
            .map(ls => {
                let bphValues = ls.usage
                    .filter(isValidUsageValue)
                    .map(u => u.busesPerHour)
                let sumBphForSection = bphValues.reduce((accumulator, current) => accumulator + current, 0);
                return sumBphForSection / bphValues.length;
            })
            .reduce((maxBph, current) => Math.max(maxBph, current), 0)
        ).reduce((maxBph, current) => Math.max(maxBph, current), 0);

    for (const line of activeLines) {
        for (const lineSection of line.lineSections) {
            // Get lateness values for both end of the line
            let fromTotal = 0;
            let fromLateness = lineSection.fromStopPoint.latenessValues
                .filter(dl => isValidDate(dl.date) && dl.hour >= tlRangeLeft && dl.hour < tlRangeRight)
                .map(dl => dl.lateness)
                .reduce((accumulator, current) => {
                    if (current !== null) {
                        fromTotal++;
                        return (accumulator ?? 0) + current;
                    }
                    return accumulator;
                }, 0);
            let toTotal = 0;
            let toLateness = lineSection.toStopPoint.latenessValues
                .filter(dl => isValidDate(dl.date) && dl.hour >= tlRangeLeft && dl.hour < tlRangeRight)
                .map(dl => dl.lateness)
                .reduce((accumulator, current) => {
                    if (current !== null) {
                        toTotal++;
                        return (accumulator ?? 0) + current;
                    }
                    return accumulator;
                }, 0);

            // Calculate average lateness
            if (fromTotal === 0 || toTotal === 0) {
                continue; // No data on both sides
            }
            fromLateness = fromTotal === 0 ? null : (fromLateness ?? 0) / fromTotal;
            toLateness = toTotal === 0 ? null : (toLateness ?? 0) / toTotal;

            // Calculate average usage
            const busPerHoursEntries = lineSection.usage
                .filter(isValidUsageValue)
                .map(ele => ele.busesPerHour);
            const averageUsage = busPerHoursEntries.reduce((accumulator, current) => accumulator + current, 0) / busPerHoursEntries.length;
            if (averageUsage === 0) {
                continue; // This section never gets used
            }

            // Generate lines on the map
            for (let i = 0; i < lineSection.track.length - 1; i++) {
                let colour: string | null = null;
                if (fromLateness !== null && toLateness !== null) {
                    let proportion = (i + 1) / (lineSection.track.length + 1);
                    colour = latenessToColour(((1 - proportion) * fromLateness) + (proportion * toLateness), MAP_GRN_LATENESS, MAP_RED_LATENESS);
                } else if (fromLateness !== null) {
                    colour = latenessToColour(fromLateness, MAP_GRN_LATENESS, MAP_RED_LATENESS);
                } else if (toLateness !== null) {
                    colour = latenessToColour(toLateness, MAP_GRN_LATENESS, MAP_RED_LATENESS);
                }
                if (colour === null)
                    continue; // Should be impossible
                L.polyline(
                    lineSection.track
                        .slice(i, i + 2)
                        .map(track => L.latLng(track.latitude, track.longitude)),
                    {
                        color: colour,
                        opacity: usageToOpacity(averageUsage, maxUsage),
                        weight: 6,
                        offset: -3,
                        lineCap: map.getZoom() >= LINE_STYLE_ZOOM_THRESHOLD ? "butt" : "round",
                        className: "leaflet-bus-line",
                        interactive: false
                    }).addTo(linesLayer);
            }
        }
    }

    if (activeLines.length === 1) {
        const line = activeLines[0];
        for (const stopPointRef of Object.keys(line.busStops)) {
            const busStop = line.busStops[stopPointRef];

            // Calculate lateness value for bus stop
            const validLatenesses = busStop.latenessValues
                .filter(lv => lv.lateness !== null && isValidDate(lv.date) && lv.hour >= tlRangeLeft && lv.hour < tlRangeRight)
                .map(lv => lv.lateness!);
            if (validLatenesses.length === 0)
                continue; // Stop point has no lateness
            const lateness = validLatenesses.reduce((acc, cur) => acc + cur) / validLatenesses.length;

            // Find position of bus stop
            const locations = [
                ...line.lineSections
                    .filter(ls => ls.fromStopPointRef === busStop.stopPointRef)
                    .map(ls => ls.track[0]),
                ...line.lineSections
                    .filter(ls => ls.toStopPointRef === busStop.stopPointRef)
                    .map(ls => ls.track[ls.track.length - 1])
            ];
            let location = getAverageLocation(locations);
            const prevLocations = line.lineSections
                .filter(ls => ls.toStopPointRef === busStop.stopPointRef)
                .map(ls => ls.track[ls.track.length - 2]);
            const prevLocation = prevLocations.length > 0 ? getAverageLocation(prevLocations) : location;
            const nextLocations = line.lineSections
                .filter(ls => ls.fromStopPointRef === busStop.stopPointRef)
                .map(ls => ls.track[1]);
            const nextLocation = nextLocations.length > 0 ? getAverageLocation(nextLocations) : location;
            const bearing = (getBearingFromPoints(prevLocation, nextLocation) - 90 + 360) % 360; // Re-orient direction to point left
            location = offsetLocationByMetersInDirection(location, bearing, 10);

            // Calculate usage
            let maxAverageUsageForStop = 0;
            for (const lineSection of line.lineSections.filter(ls => ls.fromStopPointRef === busStop.stopPointRef || ls.toStopPointRef === busStop.stopPointRef)) {
                const busPerHoursEntries = lineSection.usage
                    .filter(isValidUsageValue)
                    .map(ele => ele.busesPerHour);
                const averageUsage = busPerHoursEntries.reduce((accumulator, current) => accumulator + current, 0) / busPerHoursEntries.length;
                maxAverageUsageForStop = Math.max(maxAverageUsageForStop, averageUsage);
            }
            if (maxAverageUsageForStop === 0)
                continue; // Stop point has lateness value but never actually gets used this hour
            let opacity = maxAverageUsageForStop / maxUsage;

            L.circleMarker([location.latitude, location.longitude],
                {
                    radius: 6,
                    fill: true,
                    fillColor: latenessToColour(lateness, MAP_GRN_LATENESS, MAP_RED_LATENESS),
                    fillOpacity: opacity,
                    opacity: opacity,
                    color: "black",
                    weight: 2
                })
                .bindTooltip(`<b>${busStop.name}</b><br>Average lateness: ${lateness.toFixed(1)}`)
                .addTo(stopsLayer);
        }
    }
}

function latenessToColour(lateness: number, greenLateness: number, redLateness: number): string {
    const hue = Math.max(0, Math.min(120, 120 - ((lateness - greenLateness) / (redLateness - greenLateness)) * 120));
    return `hsl(${hue}, 100%, 50%)`;
}

function usageToOpacity(averageUsage: number, maxUsage: number): number {
    let opacity = (averageUsage / maxUsage) * (currentLine === "all" ? 0.5 : 0.8);
    return map.getZoom() < LINE_STYLE_ZOOM_THRESHOLD ? opacity / 4 : opacity;
}

function isValidUsageValue(usage: DataLineUsage): boolean {
    return usage.hour >= tlRangeLeft && usage.hour < tlRangeRight && isValidDayOfWeek(usage.dayOfWeek);
}

function isValidDayOfWeek(dayOfWeek: DayOfWeek): boolean {
    switch (currentDowPick) {
        case "weekdays":
            return dayOfWeek === DayOfWeek.Monday ||
                dayOfWeek === DayOfWeek.Tuesday ||
                dayOfWeek === DayOfWeek.Wednesday ||
                dayOfWeek === DayOfWeek.Thursday ||
                dayOfWeek === DayOfWeek.Friday;
        case "saturdays":
            return dayOfWeek === DayOfWeek.Saturday;
        case "sundays":
            return dayOfWeek === DayOfWeek.Sunday;
        default:
            return false;
    }
}

function isValidDate(date: Date): boolean {
    switch (currentDowPick) {
        case "weekdays":
            return date.getDay() >= 1 && date.getDay() <= 5;
        case "saturdays":
            return date.getDay() === 6;
        case "sundays":
            return date.getDay() === 0;
        default:
            return false;
    }
}

function getNumDaysPerWeek(): number {
    return currentDowPick === "weekdays" ? 5 : 1;
}

function getAverageLocation(locs: DataTrack[]): DataTrack {
    let location: DataTrack = locs.reduce((acc, cur) => { return { longitude: acc.longitude + cur.longitude, latitude: acc.latitude + cur.latitude } });
    location = { longitude: location.longitude / locs.length, latitude: location.latitude / locs.length };
    return location;
}

function degToRad(degrees: number): number {
    return (degrees * Math.PI) / 180;
}

function radToDeg(radians: number): number {
    return (radians * 180) / Math.PI;
}

// https://www.movable-type.co.uk/scripts/latlong.html#bearing for maths
function getBearingFromPoints(from: DataTrack, to: DataTrack): number {
    const lat1 = degToRad(from.latitude);
    const lat2 = degToRad(to.latitude);
    const dLon = degToRad(to.longitude - from.longitude);

    const y = Math.sin(dLon) * Math.cos(lat2);
    const x = Math.cos(lat1) * Math.sin(lat2) - Math.sin(lat1) * Math.cos(lat2) * Math.cos(dLon);

    const bearing = Math.atan2(y, x);
    return (radToDeg(bearing) + 360) % 360;
}

// https://www.movable-type.co.uk/scripts/latlong.html#rhumblines for maths
function offsetLocationByMetersInDirection(loc: DataTrack, bearing: number, meters: number): DataTrack {
    const R = 6371000; // Earth radius in meters
    const d = meters;
    const brng = degToRad(bearing);

    const lat1 = degToRad(loc.latitude);
    const lon1 = degToRad(loc.longitude);

    const lat2 = Math.asin(Math.sin(lat1) * Math.cos(d / R) + Math.cos(lat1) * Math.sin(d / R) * Math.cos(brng));
    const lon2 = lon1 + Math.atan2(Math.sin(brng) * Math.sin(d / R) * Math.cos(lat1), Math.cos(d / R) - Math.sin(lat1) * Math.sin(lat2));

    return { longitude: radToDeg(lon2), latitude: radToDeg(lat2) }
}

function onResizeStart(this: HTMLDivElement, ev: PointerEvent): void {
    const isLeft: boolean = this === tlGrabLeft;
    if (!isLeft && this !== tlGrabRight) return; // Safety check
    tlIsResizingLeft = isLeft;
    window.addEventListener("pointermove", onResizeMove);
    window.addEventListener("pointerup", onResizeEnd);
    window.addEventListener("pointercancel", onResizeEnd);
    timelineEle.classList.add("timeline-resizing");
}

function onResizeMove(this: Window, ev: PointerEvent): void {
    if (tlIsResizingLeft === null) return // Safety check
    const blockXs: number[] = [];
    for (const blockEle of tlBlocks) {
        const rect = blockEle.getBoundingClientRect()
        blockXs.push(rect.left);
    }
    blockXs.push(tlBlocks[tlBlocks.length - 1].getBoundingClientRect().right);
    // Find target block to drag to
    let closestBlockIndex: number = 0;
    let closestBlockDistance: number = Infinity;
    for (const [blockIndex, blockX] of blockXs.entries()) {
        const blockDistance = Math.abs(blockX - ev.clientX);
        if (blockDistance < closestBlockDistance) {
            closestBlockDistance = blockDistance;
            closestBlockIndex = blockIndex;
        }
    }
    let targetHour: number = closestBlockIndex + 5;
    // Continue if we're not already at target block
    if (targetHour === (tlIsResizingLeft ? tlRangeLeft : tlRangeRight)) {
        return;
    }
    if (tlIsResizingLeft && targetHour >= tlRangeRight)
        return; // Past right side
    if (!tlIsResizingLeft && targetHour <= tlRangeLeft)
        return; // Past left side
    if (tlIsResizingLeft)
        tlRangeLeft = targetHour;
    else
        tlRangeRight = targetHour;
    redrawTimeline(false);
    redrawMap();
}

function onResizeEnd(this: Window, ev: PointerEvent): void {
    window.removeEventListener("pointermove", onResizeMove);
    window.removeEventListener("pointerup", onResizeEnd);
    window.removeEventListener("pointercancel", onResizeEnd);
    tlIsResizingLeft = null;
    timelineEle.classList.remove("timeline-resizing");
}

function onGrabStart(this: HTMLDivElement, ev: PointerEvent): void {
    tlGrabbingStartX = ev.clientX;
    tlGrabbingStartLeft = tlRangeLeft;
    window.addEventListener("pointermove", onGrabMove);
    window.addEventListener("pointerup", onGrabEnd);
    window.addEventListener("pointercancel", onGrabEnd);
    timelineEle.classList.add("timeline-grabbing");
}

function onGrabMove(this: Window, ev: PointerEvent): void {
    if (tlGrabbingStartX === null || tlGrabbingStartLeft === null) return; // Safety check
    const pxPerBlock: number = tlBlocks[0].getBoundingClientRect().width;
    const blockShift: number = ~~((ev.clientX - tlGrabbingStartX) / pxPerBlock);
    const newLeft: number = Math.min(Math.max(tlGrabbingStartLeft + blockShift, 5), 28 - (tlRangeRight - tlRangeLeft));
    if (newLeft === tlRangeLeft)
        return;
    tlRangeRight = newLeft + (tlRangeRight - tlRangeLeft);
    tlRangeLeft = newLeft;
    redrawTimeline(false);
    redrawMap();
}

function onGrabEnd(this: Window, ev: PointerEvent): void {
    window.removeEventListener("pointermove", onGrabMove);
    window.removeEventListener("pointerup", onGrabEnd);
    window.removeEventListener("pointercancel", onGrabEnd);
    tlGrabbingStartX = null;
    tlGrabbingStartLeft = null;
    timelineEle.classList.remove("timeline-grabbing");
}

function redrawTimeline(updateColour: boolean): void {
    // Remove event handlers on existing selected items
    (document.querySelectorAll("#timerange-line .timerange-line-selected") as NodeListOf<HTMLDivElement>).forEach(ele => ele.removeEventListener("pointerdown", onGrabStart));
    // Update classes of blocks
    for (const blockEle of tlBlocks) {
        blockEle.classList.remove("timerange-line-selected", "timerange-line-selected-left", "timerange-line-selected-right");
        const blockHour: number = parseInt(blockEle.getAttribute("hour")!);
        if (blockHour >= tlRangeLeft && blockHour < tlRangeRight)
            blockEle.classList.add("timerange-line-selected");
        if (blockHour === tlRangeLeft)
            blockEle.classList.add("timerange-line-selected-left");
        if (blockHour === tlRangeRight - 1)
            blockEle.classList.add("timerange-line-selected-right");
    }
    // Update grabber position
    tlGrabLeft.style.left = `calc(${(tlRangeLeft - 5) / 23 * 100}% - 1.2rem)`;
    tlGrabRight.style.left = `calc(${(tlRangeRight - 5) / 23 * 100}% - 1.2rem)`;
    // Add event handlers for new selected items
    (document.querySelectorAll("#timerange-line .timerange-line-selected") as NodeListOf<HTMLDivElement>).forEach(ele => ele.addEventListener("pointerdown", onGrabStart));
    // Update colour of timeline
    if (!updateColour || data === null)
        return;
    const activeLines: BusLine[] = currentLine === null ? [] : (currentLine === "all" ? Object.keys(data.lines).map(k => data!.lines[k]) : [data.lines[currentLine]]);
    const hourlyLateness: Map<number, number> = new Map();
    // Get lateness values for each hour
    for (const line of activeLines) {
        // Calculate how much each stop is used each hour
        const stopUsage: Map<string, Map<number, number>> = new Map();
        for (const lineSection of line.lineSections) { // For each line section
            const fromUsage = stopUsage.get(lineSection.fromStopPointRef) ?? new Map<number, number>();
            const toUsage = stopUsage.get(lineSection.toStopPointRef) ?? new Map<number, number>();
            for (const usage of lineSection.usage) { // For each usage item in the line section
                if (!isValidDayOfWeek(usage.dayOfWeek))
                    continue;
                const fromHourlyUsage = fromUsage.get(usage.hour) ?? 0;
                fromUsage.set(usage.hour, fromHourlyUsage + (usage.busesPerHour / getNumDaysPerWeek()));
                const toHourlyUsage = toUsage.get(usage.hour) ?? 0;
                toUsage.set(usage.hour, toHourlyUsage + (usage.busesPerHour / getNumDaysPerWeek()));
            }
            stopUsage.set(lineSection.fromStopPointRef, fromUsage);
            stopUsage.set(lineSection.toStopPointRef, toUsage);
        }
        // Calculate sum of usage per hour
        const hourlyUsageSum: Map<number, number> = new Map();
        for (const stopHourlyUsage of stopUsage.values()) { // For each stop
            for (const [hour, usage] of stopHourlyUsage.entries()) { // For each hour in the stop
                const existingUsageSum = hourlyUsageSum.get(hour) ?? 0;
                hourlyUsageSum.set(hour, existingUsageSum + usage);
            }
        }
        // Calculate average lateness for each hour, weighted by usage
        const hourlyLatenessLine: Map<number, number[]> = new Map();
        for (const stopPointRef of Object.keys(line.busStops)) {
            const busStop = line.busStops[stopPointRef];
            const hourlyUsage = stopUsage.get(busStop.stopPointRef);
            if (hourlyUsage === undefined)
                continue; // No usage for this stop
            for (const latenessVal of busStop.latenessValues) {
                if (latenessVal.lateness == null || !isValidDate(latenessVal.date))
                    continue;
                const usage = hourlyUsage.get(latenessVal.hour);
                const usageSumForHour = hourlyUsageSum.get(latenessVal.hour);
                if (usage === undefined || usage === 0 || usageSumForHour === undefined || usageSumForHour === 0)
                    continue; // No usage for this hour
                const latenessArr = hourlyLatenessLine.get(latenessVal.hour) ?? [];
                latenessArr.push((latenessVal.lateness * (usage / usageSumForHour)) / getNumDaysPerWeek());
                hourlyLatenessLine.set(latenessVal.hour, latenessArr);
            }
        }
        // Add average of hourly lateness to final values
        for (let hour = 5; hour < 28; hour++) {
            const latenessArr = hourlyLatenessLine.get(hour) ?? [];
            if (latenessArr.length === 0)
                continue; // No data for this hour
            const averageLateness = latenessArr.reduce((accumulator, current) => accumulator + current, 0);
            const existingLateness = hourlyLateness.get(hour) ?? 0;
            hourlyLateness.set(hour, existingLateness + averageLateness);
        }
    }
    // Set colour of timeline based on lateness
    for (const blockEle of tlBlocks) {
        const blockHour: number = parseInt(blockEle.getAttribute("hour")!);
        const lateness = hourlyLateness.get(blockHour) ?? 0;
        const prevLateness = hourlyLateness.get(blockHour - 1) ?? 0;
        const nextLateness = hourlyLateness.get(blockHour + 1) ?? 0;
        const colour = latenessToColour(lateness, TIMELINE_GRN_LATENESS, activeLines.length > 1 ? TIMELINE_RED_LATENESS_ALL : TIMELINE_RED_LATENESS_SINGLE);
        const prevColour = latenessToColour((prevLateness + lateness) / 2, TIMELINE_GRN_LATENESS, activeLines.length > 1 ? TIMELINE_RED_LATENESS_ALL : TIMELINE_RED_LATENESS_SINGLE);
        const nextColour = latenessToColour((nextLateness + lateness) / 2, TIMELINE_GRN_LATENESS, activeLines.length > 1 ? TIMELINE_RED_LATENESS_ALL : TIMELINE_RED_LATENESS_SINGLE);
        blockEle.style.background = `linear-gradient(to right in hsl shorter hue, ${prevColour}, ${colour} 10% 90%, ${nextColour})`;
    }
}

init();