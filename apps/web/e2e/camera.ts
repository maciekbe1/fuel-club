import { create } from "qrcode";
import { writeFileSync } from "node:fs";

/**
 * Writes a Y4M video (Chromium's fake camera input) showing a QR of `text`: black modules on white,
 * 4-module quiet zone, 10 px per module, centered in 640x480 (I420; chroma neutral).
 */
export function writeQrVideo(path: string, text: string) {
  const width = 640;
  const height = 480;
  const { modules } = create(text, { errorCorrectionLevel: "M" });
  const scale = 10;
  const quiet = 4;
  const side = (modules.size + quiet * 2) * scale;
  const left = (width - side) / 2;
  const top = (height - side) / 2;

  const luma = Buffer.alloc(width * height, 235);
  for (let row = 0; row < modules.size; row++)
    for (let col = 0; col < modules.size; col++) {
      if (!modules.get(row, col)) continue;
      for (let y = 0; y < scale; y++) {
        const offset =
          (top + (row + quiet) * scale + y) * width +
          left +
          (col + quiet) * scale;
        luma.fill(16, offset, offset + scale);
      }
    }
  const chroma = Buffer.alloc((width / 2) * (height / 2), 128);
  const frame = Buffer.concat([Buffer.from("FRAME\n"), luma, chroma, chroma]);
  writeFileSync(
    path,
    Buffer.concat([
      Buffer.from(`YUV4MPEG2 W${width} H${height} F10:1 Ip A1:1 C420jpeg\n`),
      frame,
      frame,
    ]),
  );
}

/** Records every stream the page opens, so a test can check that the camera was stopped. */
export const TRACK_STREAMS = () => {
  const streams: MediaStream[] = [];
  (window as unknown as { __streams: MediaStream[] }).__streams = streams;
  const original = navigator.mediaDevices.getUserMedia.bind(
    navigator.mediaDevices,
  );
  navigator.mediaDevices.getUserMedia = async (constraints) => {
    const stream = await original(constraints);
    streams.push(stream);
    return stream;
  };
};

export const DENY_CAMERA = () => {
  navigator.mediaDevices.getUserMedia = () =>
    Promise.reject(new DOMException("Permission denied", "NotAllowedError"));
};

export const cameraState = () => {
  const streams = (window as unknown as { __streams: MediaStream[] }).__streams;
  const tracks = streams.flatMap((stream) => stream.getTracks());
  return {
    opened: tracks.length,
    live: tracks.filter((track) => track.readyState === "live").length,
  };
};

/** Records streams and holds getUserMedia until `window.__releaseCamera()` (permission prompt still open). */
export const HOLD_CAMERA = () => {
  const streams: MediaStream[] = [];
  const w = window as unknown as {
    __streams: MediaStream[];
    __releaseCamera: () => void;
    __cameraRequested: boolean;
  };
  w.__streams = streams;
  w.__cameraRequested = false;
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  w.__releaseCamera = release;
  const original = navigator.mediaDevices.getUserMedia.bind(
    navigator.mediaDevices,
  );
  navigator.mediaDevices.getUserMedia = async (constraints) => {
    w.__cameraRequested = true;
    await gate;
    const stream = await original(constraints);
    streams.push(stream);
    return stream;
  };
};
