const money = new Intl.NumberFormat("pl-PL", {
  style: "currency",
  currency: "PLN",
});
const liters = new Intl.NumberFormat("pl-PL", { maximumFractionDigits: 3 });
const points = new Intl.NumberFormat("pl-PL");

export const formatPoints = (value: number) => `${points.format(value)} pkt`;

/** "0,20 zł/l, do 50 l (maks. 10,00 zł)" - the same terms the server will apply. */
export function formatTerms(discountPerLiter: number, maxLiters: number) {
  // Integers (grosz x ml), rounded half away from zero like the server's decimal Math.Round:
  // float multiplication would turn 0,15 x 50,5 = 7,575 into 7,5749… and show 7,57.
  const product =
    Math.round(discountPerLiter * 100) * Math.round(maxLiters * 1000);
  const max = Math.floor((product + 500) / 1000) / 100;
  return `${money.format(discountPerLiter)}/l, do ${liters.format(maxLiters)} l (maks. ${money.format(max)})`;
}
