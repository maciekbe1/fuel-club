import type { Locator } from "@playwright/test";
import jsQR from "jsqr";
import { PNG } from "pngjs";

/** Decodes the QR from a screenshot of the rendered element (what a scanner would see). */
export async function decodeQr(locator: Locator) {
  const png = PNG.sync.read(await locator.screenshot());
  const result = jsQR(new Uint8ClampedArray(png.data), png.width, png.height);
  if (!result) throw new Error("No QR code found in the screenshot");
  return result.data;
}
