const ALPHABET = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
// The largest multiple of the alphabet size below 256: bytes above it would favour the first letters.
const LIMIT = 256 - (256 % ALPHABET.length);

/**
 * A receipt number for the POS simulator: PAR/<sale date>/<6 random characters>, within the API
 * rule (A-Z, 0-9, "/", "-"). The date is the sale date at the moment of the click.
 */
export function newReceiptNumber(saleDate: string): string {
  let suffix = "";
  while (suffix.length < 6)
    for (const byte of crypto.getRandomValues(new Uint8Array(8)))
      if (byte < LIMIT && suffix.length < 6)
        suffix += ALPHABET[byte % ALPHABET.length];
  const day = saleDate.replaceAll("-", "") || "BEZDATY";
  return `PAR/${day}/${suffix}`;
}
