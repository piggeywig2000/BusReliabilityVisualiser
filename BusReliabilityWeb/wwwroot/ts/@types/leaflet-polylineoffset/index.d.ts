

declare namespace L {
    export interface PolylineOptions extends PathOptions {
        offset?: number;
    }

    export interface Polyline {
        setOffset(offset: number): this;
    }
}