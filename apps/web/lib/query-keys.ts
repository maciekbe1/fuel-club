export const queryKeys = {
  me: ["me"] as const,
  wallet: ["wallet"] as const,
  loyaltyCard: ["loyalty-card"] as const,
  transactions: ["transactions"] as const,
  rewards: ["rewards"] as const,
  coupons: ["coupons"] as const,
  cashier: {
    station: ["cashier", "station"] as const,
    couponPreviews: ["cashier", "coupon-preview"] as const,
    couponPreview: (cardCode: string, couponCode: string) =>
      ["cashier", "coupon-preview", cardCode, couponCode] as const,
    /** Mutation key of the reward handover: the purchase form is locked while it runs. */
    rewardHandover: ["cashier", "reward-handover"] as const,
  },
  support: {
    all: ["support"] as const,
    current: ["support", "current"] as const,
    queue: ["support", "queue"] as const,
    conversations: ["support", "conversation"] as const,
    conversation: (side: string, id: string) =>
      ["support", "conversation", side, id] as const,
  },
  admin: {
    all: ["admin"] as const,
    users: ["admin", "users"] as const,
    rewards: ["admin", "rewards"] as const,
    reward: (id: string) => ["admin", "rewards", id] as const,
    observability: ["admin", "observability"] as const,
    observabilitySummary: (hours: number, filters: object) =>
      ["admin", "observability", "summary", hours, filters] as const,
    reviewQueue: ["admin", "observability", "review"] as const,
  },
};

export const invalidates = {
  exchange: [queryKeys.wallet, queryKeys.transactions, queryKeys.coupons],
  activate: [queryKeys.coupons],
  fuelPurchase: [queryKeys.admin.users],
  rewardChange: [queryKeys.rewards, queryKeys.admin.rewards],
  redemption: [queryKeys.admin.all],
} as const;
