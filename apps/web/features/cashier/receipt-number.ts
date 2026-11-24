const ALPHABET = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

/**
 * A receipt number for the POS simulator: PAR/<sale date>/<6 random characters>, within the API
 * rule (A-Z, 0-9, "/", "-"). Random, so a second click gives a new receipt, not a duplicate (409).
 */
export function newReceiptNumber(saleDate: string): string {
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  const suffix = Array.from(bytes, (b) => ALPHABET[b % ALPHABET.length]).join(
    "",
  );
  const day = saleDate.replaceAll("-", "") || "BEZDATY";
  return `PAR/${day}/${suffix}`;
}
