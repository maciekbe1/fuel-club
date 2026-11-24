"use client";

import { usePointsAwardedToasts } from "../hooks";

/** Mounted for customers only: listens to the loyalty hub, renders nothing itself. */
export function PointsAwardedToasts() {
  usePointsAwardedToasts();
  return null;
}
