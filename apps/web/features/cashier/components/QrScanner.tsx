"use client";

import jsQR from "jsqr";
import { Camera, CameraOff } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";

type State =
  | { kind: "idle" }
  | { kind: "starting" }
  | { kind: "scanning" }
  | { kind: "error"; message: string };

function cameraError(error: unknown) {
  const name = error instanceof DOMException ? error.name : "";
  if (name === "NotAllowedError" || name === "SecurityError")
    return "Brak zgody na użycie kamery. Zezwól na kamerę w ustawieniach przeglądarki albo wklej kod ręcznie.";
  if (name === "NotFoundError" || name === "OverconstrainedError")
    return "Nie znaleziono kamery. Wklej kod ręcznie.";
  if (name === "NotReadableError")
    return "Kamera jest zajęta przez inną aplikację. Wklej kod ręcznie.";
  return "Nie udało się uruchomić kamery. Wklej kod ręcznie.";
}

/**
 * Reads a QR accepted by `accept` (card or coupon) from the camera and hands the code over; it never
 * submits anything. Only one scanner on the screen may use the camera: the parent disables the other
 * one while `onActiveChange` reports this one active. The camera is
 * stopped after a successful scan, on "Zatrzymaj", when `disabled` turns on (form locked) and when
 * the component unmounts. Every start is an attempt with an id; stopping invalidates it, so a stream
 * that arrives late (permission granted after "Zatrzymaj") is stopped at once and a late scan is ignored.
 */
export function QrScanner({
  onScan,
  accept,
  subject,
  disabled,
  onActiveChange,
}: {
  onScan: (code: string) => void;
  /** Only codes of this kind fill the field; a card QR held to the coupon scanner is ignored. */
  accept: (text: string) => boolean;
  /** "karty klienta" or "kuponu": names the button and the hint. */
  subject: string;
  disabled?: boolean;
  onActiveChange?: (active: boolean) => void;
}) {
  const acceptRef = useRef(accept);
  const onActiveRef = useRef(onActiveChange);
  const [state, setState] = useState<State>({ kind: "idle" });
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const frameRef = useRef<number | null>(null);
  const onScanRef = useRef(onScan);
  const attemptRef = useRef(0);

  useEffect(() => {
    onScanRef.current = onScan;
    acceptRef.current = accept;
    onActiveRef.current = onActiveChange;
  }, [onScan, accept, onActiveChange]);

  const stop = () => {
    attemptRef.current++;
    if (frameRef.current !== null) cancelAnimationFrame(frameRef.current);
    frameRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (videoRef.current) videoRef.current.srcObject = null;
  };

  useEffect(() => stop, []);

  useEffect(() => {
    if (!disabled) return;
    stop();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- stopping an external resource
    setState((current) =>
      current.kind === "error" ? current : { kind: "idle" },
    );
  }, [disabled]);

  const scanFrame = (attempt: number) => {
    const video = videoRef.current;
    if (attempt !== attemptRef.current || !video || !streamRef.current) return;
    if (video.readyState >= video.HAVE_CURRENT_DATA && video.videoWidth > 0) {
      const canvas = document.createElement("canvas");
      canvas.width = video.videoWidth;
      canvas.height = video.videoHeight;
      const context = canvas.getContext("2d", { willReadFrequently: true })!;
      context.drawImage(video, 0, 0);
      const image = context.getImageData(0, 0, canvas.width, canvas.height);
      const result = jsQR(image.data, image.width, image.height, {
        inversionAttempts: "dontInvert",
      });
      if (
        result &&
        acceptRef.current(result.data) &&
        attempt === attemptRef.current
      ) {
        stop();
        setState({ kind: "idle" });
        onScanRef.current(result.data.trim().toUpperCase());
        return;
      }
    }
    frameRef.current = requestAnimationFrame(() => scanFrame(attempt));
  };

  const start = async () => {
    stop();
    const attempt = attemptRef.current;
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      setState({
        kind: "error",
        message:
          "Kamera działa tylko przez HTTPS (lub na localhost). Wklej kod ręcznie.",
      });
      return;
    }
    setState({ kind: "starting" });
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: "environment" },
        audio: false,
      });
      if (attempt !== attemptRef.current) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }
      streamRef.current = stream;
      const video = videoRef.current!;
      video.srcObject = stream;
      await video.play();
      if (attempt !== attemptRef.current) return;
      setState({ kind: "scanning" });
      frameRef.current = requestAnimationFrame(() => scanFrame(attempt));
    } catch (error) {
      if (attempt !== attemptRef.current) return;
      stop();
      setState({ kind: "error", message: cameraError(error) });
    }
  };

  const active = state.kind === "starting" || state.kind === "scanning";

  useEffect(() => {
    onActiveRef.current?.(active);
  }, [active]);
  // Unmounted while scanning (e.g. the section is reset): the camera is free again.
  useEffect(() => () => onActiveRef.current?.(false), []);

  return (
    <div className="flex flex-col gap-3">
      <div
        className={active ? "overflow-hidden rounded-2xl bg-black" : "hidden"}
      >
        <video
          ref={videoRef}
          muted
          playsInline
          className="aspect-video w-full object-cover"
          aria-label="Podgląd kamery"
        />
      </div>
      {state.kind === "scanning" && (
        <p role="status" className="text-sm text-muted-foreground">
          Skieruj kamerę na kod QR {subject}.
        </p>
      )}
      {state.kind === "error" && (
        <p role="alert" className="text-sm text-destructive">
          {state.message}
        </p>
      )}
      {active ? (
        <Button
          type="button"
          variant="outline"
          onClick={() => {
            stop();
            setState({ kind: "idle" });
          }}
        >
          <CameraOff className="size-4" aria-hidden />
          Zatrzymaj kamerę
        </Button>
      ) : (
        <Button
          type="button"
          variant="outline"
          onClick={start}
          disabled={disabled}
        >
          <Camera className="size-4" aria-hidden />
          Skanuj kod QR {subject}
        </Button>
      )}
    </div>
  );
}
